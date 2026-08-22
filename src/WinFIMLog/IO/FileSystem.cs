using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Filesystem.Ntfs;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Win32;
using MicrosoftRegistry = Microsoft.Win32.Registry;
using WinFIMLog.Utils;

namespace WinFIMLog.IO
{
    public static partial class FileSystem
    {
        private const string DownloadsFolderId = "{374DE290-123F-4565-9164-39C4925E467B}";
        private const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
        private const string UserShellFoldersPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";

        /// <summary>
        /// Calculate <see cref="SHA256" /> digest of a file.
        /// </summary>
        public static string CalculateFileHash(string path)
        {
            var digest = string.Empty;

            if (Path.Exists(path))
            {
                try
                {
                    using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var bufferedStream = new BufferedStream(fileStream, 1024 * 32);
                    using var sha = SHA256.Create();
                    digest = Convert.ToHexString(sha.ComputeHash(bufferedStream));
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.WriteLine(ex);
                }
                catch (IOException ex)
                {
                    Debug.WriteLine(ex);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
            return digest;
        }

        /// <summary>
        /// Reads file list from NTFS indexes.
        /// </summary>
        public static ConcurrentBag<string> InvokeNtfsSearch()
        {
            var ntfsDrives = DriveInfo.GetDrives()
                .Where(static d => string.Equals(d.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase));

            var allPaths = new ConcurrentBag<string>();

            Parallel.ForEach(ntfsDrives, driveToAnalyze =>
            {
                var ntfsReader = new NtfsReader(driveToAnalyze, RetrieveMode.All);
                var files = ntfsReader.GetNodesParallel(driveToAnalyze.Name)
                    .Where(static node => (node.Attributes &
                        (Attributes.Temporary |
                         Attributes.System |
                         Attributes.Device |
                         Attributes.Directory |
                         Attributes.Offline |
                         Attributes.ReparsePoint |
                         Attributes.SparseFile)) == 0)
                    .Select(static node => node.FullName);

                allPaths.AddRange(files);
            });

            return allPaths;
        }

        /// <summary>
        /// Gets a path with a wildcard, resolves it, and returns a list of paths.
        /// </summary>
        /// <remarks>
        /// The conventional <c>%SYSTEMDRIVE%\Users\*\Downloads</c> expression is resolved
        /// against profile configuration as well as physical profile directories. This retains
        /// coverage when a user has redirected Downloads away from the profile root.
        /// </remarks>
        public static List<string> ResolveWildcardPath(string path)
        {
            var resolvedPaths = new List<string>();

            if (string.IsNullOrWhiteSpace(path))
            {
                return resolvedPaths;
            }

            if (path.EndsWith('*'))
            {
                var directory = Path.GetDirectoryName(path);
                if (Directory.Exists(directory))
                {
                    resolvedPaths.AddRange(Directory.GetFiles(directory));
                }
            }
            else if (path.Contains('*'))
            {
                var wildcardIndex = path.IndexOf('*');
                var prefix = path[..wildcardIndex].TrimEnd(Path.DirectorySeparatorChar);
                var suffix = path[(wildcardIndex + 1)..].TrimStart(Path.DirectorySeparatorChar);

                if (IsPerUserDownloadsWildcard(prefix, suffix))
                {
                    resolvedPaths.AddRange(ResolveConfiguredUserDownloads());
                }

                if (Directory.Exists(prefix))
                {
                    try
                    {
                        foreach (var subfolder in Directory.GetDirectories(prefix))
                        {
                            var finalPath = Path.Combine(subfolder, suffix);
                            if (Directory.Exists(finalPath) || File.Exists(finalPath))
                            {
                                resolvedPaths.Add(finalPath);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Retain results from accessible profiles and registry redirection.
                    }
                    catch (IOException)
                    {
                        // A profile can be removed while scopes are being resolved.
                    }
                }
            }
            else if (!path.StartsWith('*'))
            {
                resolvedPaths.Add(path);
            }

            return resolvedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static IReadOnlyList<string> ResolveUserDownloads(
            IEnumerable<(string ProfilePath, string? ConfiguredDownloads)> profiles,
            Func<string, bool> directoryExists)
        {
            ArgumentNullException.ThrowIfNull(profiles);
            ArgumentNullException.ThrowIfNull(directoryExists);

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (profilePath, configuredDownloads) in profiles)
            {
                if (string.IsNullOrWhiteSpace(profilePath))
                {
                    continue;
                }

                var candidate = ResolveUserDownloadsPath(profilePath, configuredDownloads);
                if (directoryExists(candidate))
                {
                    paths.Add(candidate);
                }
            }

            return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool IsPerUserDownloadsWildcard(string prefix, string suffix) =>
            suffix.Equals("Downloads", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(Path.TrimEndingDirectorySeparator(prefix))
                .Equals("Users", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<string> ResolveConfiguredUserDownloads()
        {
            if (!OperatingSystem.IsWindows())
            {
                return [];
            }

            try
            {
                using var profileList = MicrosoftRegistry.LocalMachine.OpenSubKey(ProfileListPath, writable: false);
                if (profileList is null)
                {
                    return [];
                }

                var profiles = new List<(string ProfilePath, string? ConfiguredDownloads)>();
                foreach (var sid in profileList.GetSubKeyNames())
                {
                    using var profile = profileList.OpenSubKey(sid, writable: false);
                    var profilePath = profile?.GetValue("ProfileImagePath") as string;
                    if (string.IsNullOrWhiteSpace(profilePath))
                    {
                        continue;
                    }

                    using var userShellFolders = MicrosoftRegistry.Users.OpenSubKey(sid + "\\" + UserShellFoldersPath, writable: false);
                    string? configuredDownloads = userShellFolders?.GetValue(DownloadsFolderId, defaultValue: null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    configuredDownloads ??= userShellFolders?.GetValue("Downloads", defaultValue: null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    profiles.Add((Environment.ExpandEnvironmentVariables(profilePath), configuredDownloads));
                }

                return ResolveUserDownloads(profiles, Directory.Exists);
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }
            catch (IOException)
            {
                return [];
            }
        }

        private static string ResolveUserDownloadsPath(string profilePath, string? configuredDownloads)
        {
            if (string.IsNullOrWhiteSpace(configuredDownloads))
            {
                return IsFullyQualifiedWindowsPath(profilePath)
                    ? profilePath.TrimEnd('\\', '/') + "\\Downloads"
                    : Path.Combine(profilePath, "Downloads");
            }

            var expanded = configuredDownloads.Replace("%USERPROFILE%", profilePath,
                StringComparison.OrdinalIgnoreCase);
            expanded = Environment.ExpandEnvironmentVariables(expanded);
            return IsFullyQualifiedWindowsPath(expanded)
                ? expanded
                : IsFullyQualifiedWindowsPath(profilePath)
                    ? profilePath.TrimEnd('\\', '/') + "\\" + expanded
                    : Path.Combine(profilePath, expanded);
        }

        private static bool IsFullyQualifiedWindowsPath(string path) =>
            Path.IsPathFullyQualified(path) ||
            (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
             (path[2] == '\\' || path[2] == '/')) ||
            path.StartsWith("\\\\", StringComparison.Ordinal);
    }
}
