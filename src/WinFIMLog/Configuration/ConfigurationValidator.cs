using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinFIMLog.Configuration
{
    public static partial class ConfigurationValidator
    {
        private static readonly string[] Hives =
        [
            "HKEY_LOCAL_MACHINE", "HKEY_CURRENT_USER", "HKEY_USERS",
            "HKEY_CURRENT_CONFIG", "HKEY_CLASSES_ROOT"
        ];

        public static void Validate(IEnumerable<string> monitoredPaths, IEnumerable<string> excludedPaths,
            IEnumerable<string> monitoredKeys, IEnumerable<string> excludedKeys)
        {
            ValidateValues("MonitoredPaths", monitoredPaths, ValidatePath, allowEmpty: false);
            ValidateValues("ExcludedPaths", excludedPaths, ValidatePath, allowEmpty: true);
            ValidateValues("MonitoredKeys", monitoredKeys, ValidateKey, allowEmpty: false);
            ValidateValues("ExcludedKeys", excludedKeys, ValidateKey, allowEmpty: true);
            ScopeIdentity.RejectProtectedExclusions(excludedKeys);
        }

        public static void ValidateKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Invalid("registry key", value, "must not be empty");
            }

            if (value.Contains('*') || value.Contains('?'))
            {
                throw Invalid("registry key", value, "wildcards are not supported");
            }

            if (!Hives.Any(hive => value.Equals(hive, StringComparison.OrdinalIgnoreCase) ||
                                   value.StartsWith(hive + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                throw Invalid("registry key", value, "must start with a supported full hive name");
            }
        }

        public static void ValidatePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Invalid("path", value, "must not be empty");
            }

            var expanded = Environment.ExpandEnvironmentVariables(value);
            if (!AbsolutePathPattern().IsMatch(expanded))
            {
                throw Invalid("path", value, "must be an absolute path");
            }

            if (expanded.Contains('?'))
            {
                throw Invalid("path", value, "only a whole-segment '*' wildcard is supported");
            }

            var segments = expanded.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Count(segment => segment.Contains('*')) > 1 ||
                segments.Any(segment => segment.Contains('*') && segment != "*"))
            {
                throw Invalid("path", value, "'*' must occupy one complete path segment");
            }
        }

        private static ConfigurationValidationException Invalid(string kind, string value, string reason) =>
            new($"The {kind} '{value}' {reason}.");

        private static void ValidateValues(string setting, IEnumerable<string> values, Action<string> validator, bool allowEmpty)
        {
            var materialised = values?.ToArray() ?? throw new ConfigurationValidationException($"{setting} is missing.");
            if (!allowEmpty && materialised.Length == 0)
            {
                throw new ConfigurationValidationException($"{setting} must contain at least one value.");
            }

            foreach (var value in materialised.Where(value => !allowEmpty || !string.IsNullOrEmpty(value)))
            {
                try { validator(value); }
                catch (ConfigurationValidationException ex)
                {
                    throw new ConfigurationValidationException($"Invalid {setting} value '{value}': {ex.Message}");
                }
            }
        }

        [GeneratedRegex(@"^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+)", RegexOptions.CultureInvariant)]
        private static partial Regex AbsolutePathPattern();
    }
}
