using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using WinFIMLog.IO.Security;

namespace WinFIMLog.Snapshots
{
    /// <summary>Portable enumerator for Tier 0 filesystem evidence.</summary>
    public sealed class FileSystemSnapshotSource
    {
        private readonly long hashSizeLimit;

        public FileSystemSnapshotSource(int hashLimitMb) => hashSizeLimit = hashLimitMb * 1024L * 1024L;

        public IReadOnlyList<BaselineMember> Capture(IEnumerable<string> roots)
        {
            var output = new List<BaselineMember>();
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase)) CaptureNode(root, output);
            return output;
        }

        private void CaptureNode(string path, List<BaselineMember> output)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }
            catch (UnauthorizedAccessException) { output.Add(Unavailable(path, EvidenceAvailability.AccessDenied)); return; }
            catch { output.Add(Unavailable(path, EvidenceAvailability.Failed)); return; }

            var reparse = attributes.HasFlag(FileAttributes.ReparsePoint);
            var directory = attributes.HasFlag(FileAttributes.Directory);
            var member = new BaselineMember
            {
                Identity = Normalise(path), Path = Path.GetFullPath(path),
                NodeType = reparse ? SnapshotNodeType.ReparsePoint : directory ? SnapshotNodeType.Directory : SnapshotNodeType.File,
                HashState = directory || reparse ? HashEvidenceState.NotApplicable : HashEvidenceState.Failed,
                StreamNames = directory || reparse ? Array.Empty<string>() : EnumerateStreamNames(path),
                IsSystem = attributes.HasFlag(FileAttributes.System),
                IsSparse = attributes.HasFlag(FileAttributes.SparseFile),
                IsTemporary = attributes.HasFlag(FileAttributes.Temporary),
                IsOffline = attributes.HasFlag(FileAttributes.Offline),
                LinkCount = !directory && !reparse ? FileLinkCount.TryGet(path) : null
            };
            CaptureAcl(path, member);
            if (!directory && !reparse) CaptureHash(path, member);
            output.Add(member);

            // Reparse points are evidence nodes, never traversal roots.
            if (!directory || reparse) return;
            try
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(path)) CaptureNode(child, output);
            }
            catch (UnauthorizedAccessException) { member.AclState = EvidenceAvailability.AccessDenied; }
            catch (IOException) { member.AclState = EvidenceAvailability.Failed; }
        }

        private void CaptureHash(string path, BaselineMember member)
        {
            try
            {
                if (new FileInfo(path).Length > hashSizeLimit)
                { member.HashState = HashEvidenceState.SkippedBySizeCap; return; }
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                member.ContentHash = Convert.ToHexString(SHA256.HashData(stream));
                member.HashState = HashEvidenceState.Hashed;
            }
            catch (UnauthorizedAccessException) { member.HashState = HashEvidenceState.AccessDenied; }
            catch (FileNotFoundException) { member.HashState = HashEvidenceState.Vanished; }
            catch (IOException) { member.HashState = HashEvidenceState.Locked; }
            catch { member.HashState = HashEvidenceState.Failed; }
        }

        private static void CaptureAcl(string path, BaselineMember member)
        {
            try { member.AclEvidence = path.GetACL(); member.AclState = EvidenceAvailability.Available; }
            catch (UnauthorizedAccessException) { member.AclState = EvidenceAvailability.AccessDenied; }
            catch (FileNotFoundException) { member.AclState = EvidenceAvailability.Vanished; }
            catch { member.AclState = EvidenceAvailability.Failed; }
        }

        private static string[] EnumerateStreamNames(string path)
        {
            // The unnamed $DATA stream is represented by ContentHash. Named stream discovery is
            // available on Windows through FindFirstStreamW; an empty array means none observed.
            return OperatingSystem.IsWindows() ? AlternateDataStreams.Enumerate(path) : Array.Empty<string>();
        }

        private static BaselineMember Unavailable(string path, EvidenceAvailability state) => new()
        { Identity = Normalise(path), Path = Path.GetFullPath(path), NodeType = SnapshotNodeType.File,
          HashState = state == EvidenceAvailability.AccessDenied ? HashEvidenceState.AccessDenied : HashEvidenceState.Failed,
          AclState = state };

        internal static string Normalise(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();
    }
}
