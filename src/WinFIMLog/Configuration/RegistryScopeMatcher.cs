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
            if (IsWithin(configuredName, eventName)) return true;
            if (!configuredName.StartsWith(CurrentUser, StringComparison.OrdinalIgnoreCase) ||
                !eventName.StartsWith(Users + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var afterUsers = eventName[(Users.Length + 1)..];
            var separator = afterUsers.IndexOf('\\');
            if (separator <= 0) return false;
            var sid = afterUsers[..separator];
            if (!sid.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) return false;
            var perUserName = CurrentUser + afterUsers[separator..];
            return IsWithin(configuredName, perUserName);
        }

        public bool IsMatch(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return false;
            return _included.Any(pattern => Matches(pattern, keyName)) &&
                   !_excluded.Any(pattern => Matches(pattern, keyName));
        }

        private static bool IsWithin(string configuredName, string eventName) =>
            eventName.Equals(configuredName, StringComparison.OrdinalIgnoreCase) ||
            eventName.StartsWith(configuredName.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
