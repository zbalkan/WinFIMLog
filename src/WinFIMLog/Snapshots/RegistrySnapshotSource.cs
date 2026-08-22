using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using Microsoft.Win32;
using WinFIMLog.IO.Security;

namespace WinFIMLog.Snapshots
{
    /// <summary>
    /// Enumerates configured registry subtrees without relying on ETW state.
    /// </summary>
    public sealed class RegistrySnapshotSource
    {
        private readonly Func<string, bool> isIncluded;

        public RegistrySnapshotSource(Func<string, bool>? isIncluded = null) =>
            this.isIncluded = isIncluded ?? (static _ => true);

        public static IReadOnlyList<string> ResolveRoots(IEnumerable<string> roots) =>
            RemoveOverlappingRoots(ExpandCurrentUserRoots(roots));

        public IReadOnlyList<BaselineMember> Capture(IEnumerable<string> roots)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Registry snapshots require Windows.");
            }

            return CaptureResolved(ResolveRoots(roots));
        }

        public IReadOnlyList<BaselineMember> CaptureResolved(IEnumerable<string> resolvedRoots)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Registry snapshots require Windows.");
            }

            var members = new List<BaselineMember>();
            foreach (var root in RemoveOverlappingRoots(resolvedRoots))
            {
                CaptureRoot(root, members);
            }

            return members;
        }

        internal static IEnumerable<string> ExpandCurrentUserRoots(IEnumerable<string> roots)
        {
            foreach (var root in roots)
            {
                const string prefix = "HKEY_CURRENT_USER";
                if (!root.Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !root.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
                { yield return root; continue; }

                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("Registry snapshots require Windows.");
                }

                var suffix = root.Length == prefix.Length ? string.Empty : root[prefix.Length..];
                foreach (var sid in Registry.Users.GetSubKeyNames()
                    .Where(static name => name.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) &&
                                   !name.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return "HKEY_USERS\\" + sid + suffix;
                }
            }
        }

        internal static string Identity(string path, SnapshotNodeType nodeType) =>
            (nodeType is SnapshotNodeType.RegistryValue ? "VALUE|" : "KEY|") + path.ToUpperInvariant();

        /// <summary>
        /// Removes roots already covered by a retained segment-delimited ancestor.
        /// </summary>
        /// <remarks>
        /// The hash set makes ancestor membership expected constant time for each path segment and
        /// preserves siblings such as SOFT and SOFTWARE. Do not replace this with an all-roots Any
        /// check, which makes root reconciliation quadratic.
        /// </remarks>
        internal static IReadOnlyList<string> RemoveOverlappingRoots(IEnumerable<string> roots)
        {
            var normalised = roots.Select(static root => root.TrimEnd('\\'))
                .Where(static root => root.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var retained = new List<string>(normalised.Length);
            var retainedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in normalised)
            {
                var hasAncestor = false;
                for (var separator = root.IndexOf('\\'); separator >= 0;
                    separator = root.IndexOf('\\', separator + 1))
                {
                    if (retainedSet.Contains(root[..separator]))
                    {
                        hasAncestor = true;
                        break;
                    }
                }

                if (hasAncestor)
                {
                    continue;
                }

                retained.Add(root);
                retainedSet.Add(root);
            }

            return retained;
        }

        private static RegistryHive ParseHive(string hive) => hive.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKEY_USERS" => RegistryHive.Users,
            "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig,
            _ => throw new ArgumentException($"Unsupported registry hive: {hive}"),
        };

        private static string[] Safe(Func<string[]> action)
        { try { return action(); } catch { return []; } }

        private static byte[]? Serialise(object? value) => value switch
        { null => null, byte[] bytes => bytes, string[] strings => Encoding.UTF8.GetBytes(string.Join("\0", strings)), _ => Encoding.UTF8.GetBytes(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty), };

        private static BaselineMember Unavailable(string path, EvidenceAvailability state) =>
            Unavailable(path, SnapshotNodeType.RegistryKey, state);

        private static BaselineMember Unavailable(string path, SnapshotNodeType nodeType, EvidenceAvailability state) => new()
        {
            Identity = Identity(path, nodeType),
            Path = path,
            NodeType = nodeType,
            HashState = HashEvidenceState.NotApplicable,
            AclState = state,
        };

        /// <summary>
        /// Captures a Registry subtree with iterative depth-first traversal.
        /// </summary>
        /// <remarks>
        /// Pending work stores paths rather than open RegistryKey handles. This keeps handle use
        /// constant with respect to sibling count while the explicit stack avoids recursion depth
        /// limits. Opening every child before pushing it can exhaust handles on wide keys.
        /// </remarks>
        private void CaptureKey(RegistryKey hive, string subKey, string path, List<BaselineMember> output)
        {
            var pending = new Stack<(string SubKey, string Path)>();
            pending.Push((subKey, path));
            while (pending.Count > 0)
            {
                var work = pending.Pop();
                try
                {
                    using var key = hive.OpenSubKey(work.SubKey, writable: false);
                    if (key is not null)
                    {
                        CaptureKeyNode(key, work.SubKey, work.Path, output, pending);
                    }
                }
                catch (Exception exception) when (IsAccessDenied(exception))
                { output.Add(Unavailable(work.Path, EvidenceAvailability.AccessDenied)); }
                catch { output.Add(Unavailable(work.Path, EvidenceAvailability.Failed)); }
            }
        }

        private void CaptureKeyNode(RegistryKey key, string subKey, string path, List<BaselineMember> output,
            Stack<(string SubKey, string Path)> pending)
        {
            if (!isIncluded(path))
            {
                return;
            }

            var keyMember = new BaselineMember
            {
                Identity = Identity(path, SnapshotNodeType.RegistryKey),
                Path = path,
                NodeType = SnapshotNodeType.RegistryKey,
                HashState = HashEvidenceState.NotApplicable,
            };
            try { keyMember.AclEvidence = key.GetACL(); keyMember.AclState = EvidenceAvailability.Available; }
            catch (Exception exception) when (IsAccessDenied(exception)) { keyMember.AclState = EvidenceAvailability.AccessDenied; }
            catch { keyMember.AclState = EvidenceAvailability.Failed; }
            output.Add(keyMember);

            foreach (var valueName in Safe(key.GetValueNames))
            {
                if (!isIncluded(path + "\\" + valueName))
                {
                    continue;
                }

                try
                {
                    var value = key.GetValue(valueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    output.Add(new BaselineMember
                    {
                        Identity = Identity(path + "\\" + valueName, SnapshotNodeType.RegistryValue),
                        Path = path + "\\" + valueName,
                        NodeType = SnapshotNodeType.RegistryValue,
                        HashState = HashEvidenceState.NotApplicable,
                        AclState = keyMember.AclState,
                        AclEvidence = keyMember.AclEvidence,
                        RegistryValueKind = key.GetValueKind(valueName).ToString(),
                        RegistryValueData = Serialise(value),
                    });
                }
                catch (Exception exception) when (IsAccessDenied(exception)) { output.Add(Unavailable(path + "\\" + valueName, SnapshotNodeType.RegistryValue, EvidenceAvailability.AccessDenied)); }
                catch { output.Add(Unavailable(path + "\\" + valueName, SnapshotNodeType.RegistryValue, EvidenceAvailability.Failed)); }
            }

            var childNames = Safe(key.GetSubKeyNames);
            for (var index = childNames.Length - 1; index >= 0; index--)
            {
                var childName = childNames[index];
                if (!isIncluded(path + "\\" + childName))
                {
                    continue;
                }

                pending.Push((subKey.Length is 0 ? childName : subKey + "\\" + childName,
                    path + "\\" + childName));
            }
        }

        private void CaptureRoot(string fullName, List<BaselineMember> output)
        {
            if (!isIncluded(fullName))
            {
                return;
            }

            var separator = fullName.IndexOf('\\');
            var hiveName = separator < 0 ? fullName : fullName[..separator];
            var subKey = separator < 0 ? string.Empty : fullName[(separator + 1)..];
            using var hive = RegistryKey.OpenBaseKey(ParseHive(hiveName), RegistryView.Default);
            CaptureKey(hive, subKey, fullName.TrimEnd('\\'), output);
        }

        internal static bool IsAccessDenied(Exception exception) =>
            exception is UnauthorizedAccessException or SecurityException;
    }
}
