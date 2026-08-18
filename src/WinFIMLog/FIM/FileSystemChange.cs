using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using NUlid;
using WinFIMLog.Data;
using WinFIMLog.IO.Security;
using static WinFIMLog.IO.FileSystem;

namespace WinFIMLog.FIM
{
    public class FileSystemChange : Change
    {
        public string CurrentHash { get; set; }
        public long? CurrentSizeBytes { get; set; }

        public string FullPath { get; set; }

        public string? NewPath { get; set; }
        public ObjectType ObjectType { get; set; }
        public string? OldPath { get; set; }
        public string PreviousHash { get; set; }
        public long? PreviousSizeBytes { get; set; }

        /// <summary> Generates new file system change record from parameters </summary>
        /// <param name="path">The path to filekey</param>
        /// <param name="category"><see cref="ChangeCategory"></param>
        /// <param name="hashLimitMb">The maximum file size in megabytes for hash calculation.</param>
        /// <param name="fileSystemChange">The change object</param>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="System.Reflection.TargetInvocationException"></exception>
        /// <exception cref="PathTooLongException"></exception> <exception cref="UnauthorizedAccessException"></exception>
        public static FileSystemChange? FromPath(string path, ChangeCategory category, int hashLimitMb,
            bool retainMissing = false)
        {
            var objectType = GetObjectType(path);
            if (objectType == ObjectType.Unknown && category != ChangeCategory.Deleted && !retainMissing)
            {
                return null;
            }

            var hash = string.Empty;
            if (objectType == ObjectType.File &&
                category != ChangeCategory.Deleted &&
                IsUnderSizeLimit(path, hashLimitMb))
            {
                hash = CalculateFileHash(path);
            }

            return new FileSystemChange
            {
                Id = Ulid.NewUlid().ToString(),
                ChangeCategory = category,
                ConfigChangeType = ConfigChangeType.FileSystem,
                Entity = path,
                DateTime = DateTime.Now,
                ObjectType = objectType,
                FullPath = path,
                SourceComputer = Environment.MachineName,
                CurrentHash = hash,
                CurrentSizeBytes = FileSizeOrNull(path, objectType, category),
                PreviousHash = string.Empty,
                ACLs = GetACL(path, category)
            };
        }

        private static long? FileSizeOrNull(string path, ObjectType objectType, ChangeCategory category)
        {
            if (objectType != ObjectType.File || category == ChangeCategory.Deleted)
            {
                return null;
            }

            try { return new FileInfo(path).Length; }
            catch (Exception) { return null; }
        }

        public static FileSystemChange? FromPath(string path, ChangeCategory category, int hashLimitMb,
            string scopeHash, bool retainMissing = false)
        {
            var change = FromPath(path, category, hashLimitMb, retainMissing);
            change?.ScopeHash = scopeHash;
            return change;
        }

        public static FileSystemChange? RetrievePreviousChange(string path, ILiteDbContext ctx) => ctx.FileSystemChanges.Query()
                      .Where(x => x.NormalizedEntity == LiteDbContext.NormalizeEntity(path))
                      .OrderByDescending(c => c.DateTime)
                      .FirstOrDefault();

        public static string RetrievePreviousHash(string path, ILiteDbContext ctx)
                                    => RetrievePreviousChange(path, ctx)?.CurrentHash ?? string.Empty;

        internal static string GetAclOrEmpty(Func<string> getAcl)
        {
            ArgumentNullException.ThrowIfNull(getAcl);

            try
            {
                return getAcl();
            }
            catch (UnauthorizedAccessException)
            {
                // Access to security descriptors is independent of access to file contents.
                // A denied ACL is expected on protected paths and must not discard the event.
                return string.Empty;
            }
            catch (SecurityException)
            {
                return string.Empty;
            }
            catch (FileNotFoundException)
            {
                // FileSystemWatcher notifications race with subsequent changes and deletion.
                return string.Empty;
            }
            catch (DirectoryNotFoundException)
            {
                return string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (Exception ex)
            {
                // Unexpected failures remain diagnosable without preventing event capture.
                Debug.WriteLine(ex);
                return string.Empty;
            }
        }

        private static string GetACL(string path, ChangeCategory category)
        {
            if (category == ChangeCategory.Deleted)
            {
                return string.Empty;
            }

            return GetAclOrEmpty(path.GetACL);
        }

        private static ObjectType GetObjectType(string path)
        {
            var objectType = ObjectType.Unknown;
            try
            {
                if (Path.Exists(path))
                {
                    var attr = File.GetAttributes(path);
                    if (IsSymbolicLink(path, attr))
                    {
                        return ObjectType.SymbolicLink;
                    }

                    if (attr.HasFlag(FileAttributes.Directory))
                    {
                        return ObjectType.Directory;
                    }

                    return ObjectType.File;
                }
            }
            catch (Exception)
            {
                // When a file is deleted, this returns null
                objectType = ObjectType.Unknown;
            }

            return objectType;
        }

        private static bool IsSymbolicLink(string path, FileAttributes attributes)
        {
            if (!attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            return attributes.HasFlag(FileAttributes.Directory)
                ? new DirectoryInfo(path).LinkTarget != null
                : new FileInfo(path).LinkTarget != null;
        }

        private static bool IsUnderSizeLimit(string path, int hashLimitMb)
        {
            try
            {
                return new FileInfo(path).Length < hashLimitMb * 1024L * 1024L;
            }
            catch (FileNotFoundException)
            {
                // File is removed during recording.
                // We cannot do anything here.
                // No need to spam debug logs.
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return false;
            }
        }
    }
}
