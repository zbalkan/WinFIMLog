using System;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Win32;
using NUlid;
using WinFIMLog.IO.Security;
using WinFIMLog.Utils;

namespace WinFIMLog.FIM
{
    public partial class RegistryChange : Change
    {
        internal RegistryChange()
        { }

        public RegistryChange(RegistryTraceData data, string fullName)
        {
            Id = Ulid.NewUlid().ToString();
            EventName = data.OpcodeName;
            ProcessID = data.ProcessID;
            ProcessName = data.ProcessName;
            ValueName = data.ValueName ?? string.Empty;
            ConfigChangeType = ConfigChangeType.Registry;
            SourceComputer = Environment.MachineName;
            DateTime = data.TimeStamp;
            KeyName = data.KeyName;
            ChangeCategory = Category((RegistryEventCategory)(int)data.Opcode);
            Entity = fullName;

            var hive = ParseHive(fullName);
            Hive = Enum.GetName(hive) ?? string.Empty;
            CaptureEvidence(hive, fullName);
            CaptureAttribution(data);
        }

        /// <summary>Availability of optional live registry metadata for this already observed ETW event.</summary>
        public string EvidenceStatus { get; set; } = "Available";

        /// <summary>Machine-readable reason why live registry metadata is incomplete.</summary>
        public string? EvidenceMissingReason { get; set; }

        public string EventName { get; set; }

        public string Hive { get; set; }

        public string KeyName { get; set; }

        public string? ValueData { get; set; }

        public string? ValueName { get; set; }

        public override string ToString() => $"Timestamp: {DateTime:O}\nEvent Name: {EventName}\nChange Category: {ChangeCategory}\nEntity: {Entity}\nKey Name: {KeyName}\nValue Name: {ValueName}\nValue Data: {ValueData}\nProcess: {ProcessName} (PID: {ProcessID})\nUser Info: {Username} (SID: {UserSID})\nAttribution Status: {AttributionStatus}\nEvidence Status: {EvidenceStatus}";

        internal static (string Value, string? MissingReason) GetEvidenceOrEmpty(Func<string> collect)
        {
            try
            {
                return (collect(), null);
            }
            catch (Exception exception)
            {
                return (string.Empty, exception.GetType().Name);
            }
        }

        private static ChangeCategory Category(RegistryEventCategory category) => category switch
        {
            RegistryEventCategory.Create => ChangeCategory.Created,
            RegistryEventCategory.SetValue or RegistryEventCategory.SetInformation => ChangeCategory.Changed,
            RegistryEventCategory.Delete or RegistryEventCategory.DeleteValue => ChangeCategory.Deleted,
            _ => ChangeCategory.Changed
        };

        private void CaptureAttribution(RegistryTraceData data)
        {
            var attribution = ProcessAttribution.Resolve(data.ProcessID, data.ProcessName, processId =>
            {
                using var process = Process.GetProcessById(processId);
                var userInfo = SidUserInfoCache.Get(process);
                return (process.ProcessName, userInfo.Username, userInfo.SID);
            });
            AttributionStatus = attribution.Status;
            AttributionMethod = "RegistryETWPostEventPid";
            AttributionConfidence = attribution.Status == AttributionStatus.Attributed ? "Low" : "None";
            AttributionSourceTimestamp = new DateTimeOffset(data.TimeStamp);
            AttributionMissingReason = attribution.Status == AttributionStatus.Unavailable
                ? "ProcessExitedOrAccessDenied" : null;
            ProcessName = attribution.ProcessName;
            Username = attribution.Username;
            UserSID = attribution.UserSid;
        }

        private void CaptureEvidence(RegistryHive hive, string fullName)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(StripFullName(fullName, ValueName ?? string.Empty), false);
                if (key is null)
                {
                    MarkEvidenceUnavailable("KeyNotAvailable");
                    return;
                }

                if (KeyName?.Length == 0)
                {
                    KeyName = key.Name;
                }
                if (ChangeCategory != ChangeCategory.Deleted)
                {
                    ValueData = ExtractValueData(key);
                }

                var acl = GetEvidenceOrEmpty(key.GetACL);
                ACLs = acl.Value;
                if (acl.MissingReason is not null)
                {
                    MarkEvidencePartial(acl.MissingReason);
                }
            }
            catch (Exception exception)
            {
                MarkEvidenceUnavailable(exception.GetType().Name);
            }
        }

        private string? ExtractValueData(RegistryKey key)
        {
            if (string.IsNullOrEmpty(ValueName))
            {
                return null;
            }

            try
            {
                var value = key.GetValue(ValueName);
                if (value is null)
                {
                    return null;
                }

                return key.GetValueKind(ValueName) switch
                {
                    RegistryValueKind.DWord => Convert.ToString((int)value),
                    RegistryValueKind.QWord => Convert.ToString((long)value),
                    RegistryValueKind.Binary => FormatBinaryValue((byte[])value),
                    RegistryValueKind.MultiString => string.Join(" ", (string[])value),
                    _ => ValueOrNull(value)
                };
            }
            catch (Exception exception)
            {
                MarkEvidencePartial(exception.GetType().Name);
                return null;
            }
        }

        internal static string FormatBinaryValue(byte[] value)
        {
            if (value.Length == 0)
            {
                return string.Empty;
            }

            return string.Create(value.Length * 3 - 1, value, static (destination, bytes) =>
            {
                const string hexCharacters = "0123456789abcdef";
                var written = 0;
                for (var index = 0; index < bytes.Length; index++)
                {
                    if (index > 0)
                    {
                        destination[written++] = ' ';
                    }

                    var current = bytes[index];
                    destination[written++] = hexCharacters[current >> 4];
                    destination[written++] = hexCharacters[current & 0x0f];
                }
            });
        }

        private static string? ValueOrNull(object value)
        {
            var text = value.ToString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private void MarkEvidencePartial(string reason)
        {
            if (EvidenceStatus == "Available")
            {
                EvidenceStatus = "Partial";
            }

            EvidenceMissingReason ??= reason;
        }

        private void MarkEvidenceUnavailable(string reason)
        {
            EvidenceStatus = "Unavailable";
            EvidenceMissingReason ??= reason;
        }

        private static RegistryHive ParseHive(string keyName)
        {
            if (keyName.Contains("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
            {
                return RegistryHive.LocalMachine;
            }

            if (keyName.Contains("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
            {
                return RegistryHive.CurrentUser;
            }

            if (keyName.Contains("HKEY_CURRENT_CONFIG", StringComparison.OrdinalIgnoreCase))
            {
                return RegistryHive.CurrentConfig;
            }

            if (keyName.Contains("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase))
            {
                return RegistryHive.ClassesRoot;
            }

            return RegistryHive.Users;
        }

        internal static string StripFullName(string fullName, string valueName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return string.Empty;
            }

            var end = fullName.Length;
            if (!string.IsNullOrEmpty(valueName) &&
                end > valueName.Length &&
                fullName.AsSpan(0, end).EndsWith(valueName, StringComparison.Ordinal) &&
                fullName[end - valueName.Length - 1] == '\\')
            {
                end -= valueName.Length + 1;
            }

            var path = fullName.AsSpan(0, end);
            var hiveSeparator = path.IndexOf('\\');
            if (hiveSeparator <= 0 || hiveSeparator == path.Length - 1)
            {
                return end == fullName.Length ? fullName : fullName[..end];
            }

            // The registry API consumes this subkey string synchronously; materialize only the
            // final retained argument rather than a regex pattern, intermediate replacement, and
            // captured-group result.
            return path[(hiveSeparator + 1)..].ToString();
        }
    }
}
