using System;
using System.Collections.Generic;
using System.IO;

namespace WinFIMLog.Configuration
{
    /// <summary>Matches file-system paths against directory scopes without prefix collisions.</summary>
    internal static class PathScopeMatcher
    {
        internal static bool IsWithinAny(IEnumerable<string> roots, string path)
        {
            foreach (var root in roots)
            {
                if (IsWithin(root, path))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsWithin(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalisedRoot = Path.TrimEndingDirectorySeparator(root);
            var normalisedPath = Path.TrimEndingDirectorySeparator(path);
            if (string.Equals(normalisedRoot, normalisedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalisedRoot.EndsWith('\\') || normalisedRoot.EndsWith('/'))
            {
                return normalisedPath.StartsWith(normalisedRoot, StringComparison.OrdinalIgnoreCase);
            }

            return normalisedPath.AsSpan().StartsWith(normalisedRoot, StringComparison.OrdinalIgnoreCase) &&
                   normalisedPath.Length > normalisedRoot.Length &&
                   (normalisedPath[normalisedRoot.Length] == '\\' ||
                    normalisedPath[normalisedRoot.Length] == '/');
        }
    }
}
