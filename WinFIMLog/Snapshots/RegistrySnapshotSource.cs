using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using WinFIMLog.IO.Security;

namespace WinFIMLog.Snapshots
{
    /// <summary>Enumerates configured registry subtrees without relying on ETW state.</summary>
    public sealed class RegistrySnapshotSource
    {
        public IReadOnlyList<BaselineMember> Capture(IEnumerable<string> roots)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Registry snapshots require Windows.");
            var members = new List<BaselineMember>();
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase)) CaptureRoot(root, members);
            return members;
        }

        private static void CaptureRoot(string fullName, List<BaselineMember> output)
        {
            var separator = fullName.IndexOf('\\');
            var hiveName = separator < 0 ? fullName : fullName[..separator];
            var subKey = separator < 0 ? string.Empty : fullName[(separator + 1)..];
            using var hive = RegistryKey.OpenBaseKey(ParseHive(hiveName), RegistryView.Default);
            try
            {
                using var key = hive.OpenSubKey(subKey, false);
                if (key is not null) CaptureKey(key, fullName.TrimEnd('\\'), output);
            }
            catch (UnauthorizedAccessException) { output.Add(Unavailable(fullName, EvidenceAvailability.AccessDenied)); }
            catch { output.Add(Unavailable(fullName, EvidenceAvailability.Failed)); }
        }

        private static void CaptureKey(RegistryKey key, string path, List<BaselineMember> output)
        {
            var keyMember = new BaselineMember { Identity = path.ToUpperInvariant(), Path = path,
                NodeType = SnapshotNodeType.RegistryKey, HashState = HashEvidenceState.NotApplicable };
            try { keyMember.AclEvidence = key.GetACL(); keyMember.AclState = EvidenceAvailability.Available; }
            catch (UnauthorizedAccessException) { keyMember.AclState = EvidenceAvailability.AccessDenied; }
            catch { keyMember.AclState = EvidenceAvailability.Failed; }
            output.Add(keyMember);

            foreach (var valueName in Safe(() => key.GetValueNames()))
            {
                try
                {
                    var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    output.Add(new BaselineMember { Identity = (path + "\\" + valueName).ToUpperInvariant(),
                        Path = path + "\\" + valueName, NodeType = SnapshotNodeType.RegistryValue,
                        HashState = HashEvidenceState.NotApplicable, AclState = keyMember.AclState,
                        AclEvidence = keyMember.AclEvidence, RegistryValueKind = key.GetValueKind(valueName).ToString(),
                        RegistryValueData = Serialise(value) });
                }
                catch (UnauthorizedAccessException) { output.Add(Unavailable(path + "\\" + valueName, EvidenceAvailability.AccessDenied)); }
                catch { output.Add(Unavailable(path + "\\" + valueName, EvidenceAvailability.Failed)); }
            }
            foreach (var childName in Safe(() => key.GetSubKeyNames()))
            {
                try { using var child = key.OpenSubKey(childName, false); if (child is not null) CaptureKey(child, path + "\\" + childName, output); }
                catch (UnauthorizedAccessException) { output.Add(Unavailable(path + "\\" + childName, EvidenceAvailability.AccessDenied)); }
                catch { output.Add(Unavailable(path + "\\" + childName, EvidenceAvailability.Failed)); }
            }
        }

        private static string[] Safe(Func<string[]> action) { try { return action(); } catch { return Array.Empty<string>(); } }
        private static byte[]? Serialise(object? value) => value switch
        { null => null, byte[] bytes => bytes, string[] strings => Encoding.UTF8.GetBytes(string.Join("\0", strings)), _ => Encoding.UTF8.GetBytes(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty) };
        private static RegistryHive ParseHive(string hive) => hive.ToUpperInvariant() switch
        { "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine, "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
          "HKEY_USERS" => RegistryHive.Users, "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
          "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig, _ => throw new ArgumentException($"Unsupported registry hive: {hive}") };
        private static BaselineMember Unavailable(string path, EvidenceAvailability state) => new()
        { Identity = path.ToUpperInvariant(), Path = path, NodeType = SnapshotNodeType.RegistryKey,
          HashState = HashEvidenceState.NotApplicable, AclState = state };
    }
}
