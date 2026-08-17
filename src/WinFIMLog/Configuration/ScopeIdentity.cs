using System;
using System.Collections.Generic;
using System.Linq;

namespace WinFIMLog.Configuration
{
    /// <summary>Creates a stable identity for an effective monitoring scope.</summary>
    public static class ScopeIdentity
    {
        public const string PolicyKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\WinFIMLog";
        public const string PreferenceKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\WinFIMLog";

        public static string Compute(IEnumerable<string> monitoredPaths, IEnumerable<string> excludedPaths,
             IEnumerable<string> excludedExtensions, IEnumerable<string> monitoredKeys, IEnumerable<string> excludedKeys)
        {
            var canonical = string.Join("\n", new[]
            {
                Canonicalise("MP", monitoredPaths), Canonicalise("XP", excludedPaths),
                Canonicalise("XE", excludedExtensions), Canonicalise("MK", monitoredKeys),
                Canonicalise("XK", excludedKeys)
            });

            Span<byte> hash = stackalloc byte[32];
            var byteCount = System.Text.Encoding.UTF8.GetByteCount(canonical);

            if (byteCount <= 1024)
            {
                Span<byte> utf8Bytes = stackalloc byte[byteCount];
                System.Text.Encoding.UTF8.GetBytes(canonical, utf8Bytes);
                System.Security.Cryptography.SHA256.HashData(utf8Bytes, hash);
            }
            else
            {
                var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
                System.Security.Cryptography.SHA256.HashData(utf8Bytes, hash);
            }

            return Convert.ToHexStringLower(hash);
        }

        public static void EnsureConfigurationKeysMonitored(ICollection<string> monitoredKeys)
        {
            AddIfMissing(monitoredKeys, PolicyKey);
            AddIfMissing(monitoredKeys, PreferenceKey);
        }

        public static void RejectProtectedExclusions(IEnumerable<string> excludedKeys)
        {
            foreach (var exclusion in excludedKeys.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (Covers(exclusion, PolicyKey) || Covers(exclusion, PreferenceKey))
                {
                    throw new ConfigurationValidationException($"ExcludedKeys value '{exclusion}' covers the protected configuration key. Configuration keys cannot be excluded.");
                }
            }
        }

        private static void AddIfMissing(ICollection<string> values, string value)
        {
            if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(value);
            }
        }

        private static string Canonicalise(string name, IEnumerable<string> values) => name + "=" +
                    string.Join("|", values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().TrimEnd('\\').ToUpperInvariant())
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));

        private static bool Covers(string candidate, string protectedKey)
        {
            candidate = candidate.Trim().TrimEnd('\\');
            return protectedKey.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                   protectedKey.StartsWith(candidate + "\\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
