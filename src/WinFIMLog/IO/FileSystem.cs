using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Filesystem.Ntfs;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using WinFIMLog.Utils;

namespace WinFIMLog.IO
{
    public static partial class FileSystem
    {
        /// <summary>
        ///     Calculate <see cref="SHA256" /> digest of a file
        /// </summary>
        /// <param name="path">
        ///     Full pathof the file
        /// </param>
        /// <returns>
        ///     <see cref="SHA256" /> digest converted into <see cref="string" />
        /// </returns>
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
                    digest = BitConverter
                        .ToString(sha.ComputeHash(bufferedStream))
                        .Replace("-", string.Empty);
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Access denied
                    Debug.WriteLine(ex);
                }
                catch (IOException ex)
                {
                    // File is locked by another process
                    Debug.WriteLine(ex);
                }
                catch (Exception ex)
                {
                    // Any other exception
                    // Here for creating breakpoints for separate cases.
                    Debug.WriteLine(ex);
                }
            }
            return digest;
        }

        /// <summary>
        ///     Reads file list fron NTFS indexes
        /// </summary>
        /// <returns>
        ///     List of all files
        /// </returns>
        /// <exception cref="IOException">
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// </exception>
        /// <exception cref="AggregateException">
        /// </exception>
        public static ConcurrentBag<string> InvokeNtfsSearch()
        {
            var ntfsDrives = DriveInfo.GetDrives()
                .Where(d => d.DriveFormat == "NTFS");

            var allPaths = new ConcurrentBag<string>();

            Parallel.ForEach(ntfsDrives, driveToAnalyze =>
            {
                var ntfsReader =
                    new NtfsReader(driveToAnalyze, RetrieveMode.All);
                var files =
                    ntfsReader.GetNodesParallel(driveToAnalyze.Name)
                        .Where(n => (n.Attributes &
                                     (Attributes.Temporary |
                                      Attributes.System |
                                      Attributes.Device |
                                      Attributes.Directory |
                                      Attributes.Offline |
                                      Attributes.ReparsePoint |
                                      Attributes.SparseFile)) == 0)
                        .Select(n => n.FullName);

                allPaths.AddRange(files);
            });

            return allPaths;
        }

        /// <summary>
        ///     Gets a path with wildcard, resolves it and returns a list of paths
        /// </summary>
        /// <param name="path">a path with or without wildcard.</param>
        /// <returns>List of paths returned after resolving wildcard path.</returns>
        /// <remarks>
        /// Position of wildcard can be;
        /// 1. At the end of the path: all files in the directory such as "%WINDIR%\\*", remove and continue.
        /// 2. In the middle of the path: All subfolders in a folder such as "%SYSTEMDRIVE%\\Users\\*\\Downloads". Get the path before wildcard. Enumerate all subfolders, append the part after wildcard and append the final path to the list of paths.
        /// 3. At the beginning, such as "*\\Downloads", which is too broad. Ignore invalid.
        /// 4. Nowhere. Return the same path as a single-item list.
        /// </remarks>
        public static List<string> ResolveWildcardPath(string path)
        {
            var resolvedPaths = new List<string>();

            if (string.IsNullOrWhiteSpace(path))
            {
                return resolvedPaths; // Return empty list for invalid input
            }

            // Case 1: Wildcard at the end of the path
            if (path.EndsWith('*'))
            {
                var directory = Path.GetDirectoryName(path);

                if (Directory.Exists(directory))
                {
                    // Get all files in the directory
                    resolvedPaths.AddRange(Directory.GetFiles(directory));
                }
            }
            // Case 2: Wildcard in the middle of the path
            else if (path.Contains('*'))
            {
                var wildcardIndex = path.IndexOf('*');
                var prefix = path.Substring(0, wildcardIndex).TrimEnd(Path.DirectorySeparatorChar);
                var suffix = path.Substring(wildcardIndex + 1).TrimStart(Path.DirectorySeparatorChar);

                if (Directory.Exists(prefix))
                {
                    // Get all subfolders in the directory
                    var subfolders = Directory.GetDirectories(prefix);

                    foreach (var subfolder in subfolders)
                    {
                        var finalPath = Path.Combine(subfolder, suffix);

                        // Check if the path exists (file or directory)
                        if (Directory.Exists(finalPath) || File.Exists(finalPath))
                        {
                            resolvedPaths.Add(finalPath);
                        }
                    }
                }
            }
            // Case 3: Wildcard at the beginning
            else if (path.StartsWith('*'))
            {
                // Ignore, as this is too broad
                return resolvedPaths;
            }
            // Case 0: No wildcard
            else
            {
                resolvedPaths.Add(path);
            }

            return resolvedPaths;
        }
    }
}
