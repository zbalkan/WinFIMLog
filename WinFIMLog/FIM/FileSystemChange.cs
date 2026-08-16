using System;
using System.Diagnostics;
using System.IO;
using WinFIMLog.Data;
using WinFIMLog.IO.Security;
using NUlid;
using static WinFIMLog.IO.FileSystem;

namespace WinFIMLog.FIM
{
    public class FileSystemChange : Change
    {
        public string CurrentHash { get; set; }

        public string FullPath { get; set; }

        public string PreviousHash { get; set; }

        public ObjectType ObjectType { get; set; }

        public static string RetrievePreviousHash(string path, ILiteDbContext ctx)
            => RetrievePreviousChange(path, ctx)?.CurrentHash ?? string.Empty;

        public static FileSystemChange? RetrievePreviousChange(string path, ILiteDbContext ctx)
        {
            return ctx.FileSystemChanges.Query()
                      .Where(x => x.Entity == path)
                      .OrderByDescending(c => c.DateTime)
                      .FirstOrDefault();
        }

        /// <summary> Generates new file system change record from parameters </summary>
        /// <param name="path">The path to filekey</param>
        /// <param name="category"><see cref="ChangeCategory"></param>
        /// <param name="hashLimitMb">The maximum file size in megabytes for hash calculation.</param>
        /// <param name="fileSystemChange">The change object</param>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="System.Security.SecurityException"></exception>
        /// <exception cref="System.Reflection.TargetInvocationException"></exception>
        /// <exception cref="PathTooLongException"></exception> <exception cref="UnauthorizedAccessException"></exception>
        public static FileSystemChange? FromPath(string path, ChangeCategory category, int hashLimitMb)
        {
            var objectType = GetObjectType(path);
            if (objectType == ObjectType.Unknown && category != ChangeCategory.Deleted)
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
                PreviousHash = string.Empty,
                ACLs = GetACL(path, category)
            };
        }

        private static string GetACL(string path, ChangeCategory category)
        {
            if (category == ChangeCategory.Deleted)
            {
                return string.Empty;
            }

            try
            {
                return path.GetACL();
            }
            catch (Exception ex)
            {
                // ACL collection must not prevent the filesystem event from being recorded.
                Debug.WriteLine(ex);
                return string.Empty;
            }
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
    }
}
