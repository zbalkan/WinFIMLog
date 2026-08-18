using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Win32;
using NUlid;
using WinFIMLog.IO.Security;
using WinFIMLog.Utils;

namespace WinFIMLog.FIM
{
    public partial class RegistryChange : Change
    {
        private readonly RegistryKey? _key;

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

            switch ((RegistryEventCategory)(int)data.Opcode)
            {
                case RegistryEventCategory.Create:
                    ChangeCategory = ChangeCategory.Created;
                    break;

                case RegistryEventCategory.SetValue:
                case RegistryEventCategory.SetInformation:
                    ChangeCategory = ChangeCategory.Changed;
                    break;

                case RegistryEventCategory.Delete:
                case RegistryEventCategory.DeleteValue:
                    ChangeCategory = ChangeCategory.Deleted;
                    break;
            }

            Entity = fullName;

            var hive = ParseHive(fullName);

            Hive = Enum.GetName(hive) ?? string.Empty;

            using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
            {
                _key = baseKey.OpenSubKey(StripFullName(fullName, ValueName), false);
                if (_key != null)
                {
                    if (KeyName?.Length == 0)
                    {
                        KeyName = _key.Name;
                    }
                    if (ChangeCategory != ChangeCategory.Deleted)
                    {
                        ValueData = ExtractValueData();
                    }
                }
            }

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

            ACLs = _key?.GetACL() ?? string.Empty;
        }

        public string EventName { get; set; }
        public string Hive { get; set; }

        public string KeyName { get; set; }

        public string? ValueData { get; set; }

        public string? ValueName { get; set; }

        public override string ToString() => $"Timestamp: {DateTime:O}\nEvent Name: {EventName}\nChange Category: {ChangeCategory}\nEntity: {Entity}\nKey Name: {KeyName}\nValue Name: {ValueName}\nValue Data: {ValueData}\nProcess: {ProcessName} (PID: {ProcessID})\nUser Info: {Username} (SID: {UserSID})\nAttribution Status: {AttributionStatus}";

        private static RegistryHive ParseHive(string keyName)
        {
            if (keyName.Contains("HKEY_LOCAL_MACHINE"))
            {
                return RegistryHive.LocalMachine;
            }

            if (keyName.Contains("HKEY_CURRENT_USER"))
            {
                return RegistryHive.CurrentUser;
            }

            if (keyName.Contains("HKEY_CURRENT_CONFIG"))
            {
                return RegistryHive.CurrentConfig;
            }

            if (keyName.Contains("HKEY_CLASSES_ROOT"))
            {
                return RegistryHive.ClassesRoot;
            }
            else
            {
                return RegistryHive.Users;
            }
        }

        [GeneratedRegex(@"^[^\\]+\\(.+?)(\\)?$")]
        private static partial Regex StrippedKeyNameRegex();

        private string? ExtractValueData()
        {
            if (string.IsNullOrEmpty(ValueName))
            {
                return null;
            }

            var o = _key!.GetValue(ValueName);
            string? result = null;
            if (o != null && !string.IsNullOrEmpty(o.ToString()))
            {
                switch (_key.GetValueKind(ValueName))
                {
                    case RegistryValueKind.DWord:
                        result = Convert.ToString((int)o);
                        break;

                    case RegistryValueKind.QWord:
                        result = Convert.ToString((long)o);
                        break;

                    case RegistryValueKind.String:
                    case RegistryValueKind.ExpandString:
                        result = o!.ToString();
                        break;

                    case RegistryValueKind.Binary:
                        result = string.Join(" ", ((byte[])o).Select(b => $"{b:x2}"));
                        break;

                    case RegistryValueKind.MultiString:
                        result = string.Join(" ", (string[])o);
                        break;
                }
            }
            return result;
        }

        private string StripFullName(string fullName, string valueName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return string.Empty;
            }

            // Remove the ValueName if provided
            if (!string.IsNullOrEmpty(valueName))
            {
                var valuePattern = $@"\\{Regex.Escape(valueName)}$";
                fullName = Regex.Replace(fullName, valuePattern, string.Empty); // Remove ValueName
            }

            // Apply regex to strip the hive name and clean the full key path
            return StrippedKeyNameRegex().Replace(fullName, "$1");
        }
    }
}
