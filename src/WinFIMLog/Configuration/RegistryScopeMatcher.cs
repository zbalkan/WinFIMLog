using System;
using System.Collections.Generic;
using System.Linq;

namespace WinFIMLog.Configuration
{
    /// <summary>Matches registry names using an all-loaded-user-hives HKCU policy.</summary>
    public sealed class RegistryScopeMatcher
    {
        private const string CurrentUser = "HKEY_CURRENT_USER";
        private const string Users = "HKEY_USERS";
        private readonly string[] _excluded;
        private readonly string[] _included;

        public RegistryScopeMatcher(IEnumerable<string> included, IEnumerable<string> excluded)
        {
            _included = included.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            _excluded = excluded.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        }

        public static bool Matches(string configuredName, string eventName)
        {
            if (IsWithin(configuredName.AsSpan(), eventName.AsSpan()))
            {
                return true;
            }

            var configured = configuredName.AsSpan();
            var eventPath = eventName.AsSpan();
            if (!configured.StartsWith(CurrentUser, StringComparison.OrdinalIgnoreCase) ||
                !eventPath.StartsWith(Users, StringComparison.OrdinalIgnoreCase) ||
                eventPath.Length <= Users.Length || eventPath[Users.Length] != '\\')
            {
                return false;
            }

            var afterUsers = eventPath[(Users.Length + 1)..];
            var separator = afterUsers.IndexOf('\\');
            if (separator <= 0 ||
                !afterUsers[..separator].StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Compare the configured HKCU suffix to the event's SID-hive suffix directly.
            // This avoids creating the short-lived "HKEY_CURRENT_USER" + suffix string for
            // every loaded-user-hive event.
            return IsWithin(configured[CurrentUser.Length..], afterUsers[separator..]);
        }

        public bool IsMatch(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName))
            {
                return false;
            }

            return _included.Any(pattern => Matches(pattern, keyName)) &&
                   !_excluded.Any(pattern => Matches(pattern, keyName));
        }

        private static bool IsWithin(ReadOnlySpan<char> configuredName, ReadOnlySpan<char> eventName)
        {
            if (eventName.Equals(configuredName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var configuredLength = configuredName.TrimEnd('\\').Length;
            return eventName.Length > configuredLength &&
                   eventName.StartsWith(configuredName[..configuredLength], StringComparison.OrdinalIgnoreCase) &&
                   eventName[configuredLength] == '\\';
        }
    }
}
