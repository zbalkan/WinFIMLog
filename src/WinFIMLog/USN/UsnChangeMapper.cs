using System;
using System.IO;
using NUlid;
using WinFIMLog.FIM;
using static WinFIMLog.IO.FileSystem;

namespace WinFIMLog.USN
{
    /// <summary>Turns a parsed journal record into a <see cref="FileSystemChange"/>.</summary>
    /// <remarks>
    /// Where the object still exists this reuses <see cref="FileSystemChange.FromPath"/> so hash, ACL
    /// and object-type evidence is captured identically to every other source. Where it does not —
    /// the transient create-delete case the journal source exists for — only namespace evidence
    /// survives, and the change is constructed directly rather than pretending to richer evidence.
    ///
    /// Attribution is always absent. A USN record identifies what changed, never who changed it, and
    /// the record is marked unattributed explicitly rather than left at a default that a consumer
    /// could mistake for a failed lookup.
    /// </remarks>
    internal static class UsnChangeMapper
    {
        /// <summary>Placeholder segment used when a record's parent directory cannot be resolved.</summary>
        internal const string UnresolvedSegment = "?";

        /// <summary>
        /// Maps one record, or returns null when it falls outside the monitored scope.
        /// </summary>
        /// <param name="record">The parsed journal record.</param>
        /// <param name="pathCache">Volume-scoped parent-reference resolver.</param>
        /// <param name="configuration">Effective settings generation used for scope and hash limits.</param>
        /// <param name="driveLetter">Volume the record came from.</param>
        public static FileSystemChange? Map(ParsedUsnRecord record, DirectoryPathCache pathCache,
            EffectiveSettings configuration, char driveLetter)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(pathCache);
            ArgumentNullException.ThrowIfNull(configuration);

            var parentPath = pathCache.GetPath(record.ParentDirectoryReferenceNumber);
            var unresolved = IsUnresolved(parentPath);
            var fullPath = CombinePath(parentPath, record.Filename);
            var category = UsnReasonMapper.MapReasonToChangeCategory(record.Reason);

            // A resolved path is scoped exactly as every other source is. An unresolved one cannot be
            // matched against MonitoredPaths at all, and dropping it would discard precisely the
            // deleted-parent activity this source was added to catch, so it is kept and marked.
            if (!unresolved && !configuration.IsMonitoredPath(fullPath))
            {
                return null;
            }

            var change = unresolved
                ? BuildUnresolved(fullPath, category, driveLetter)
                : FileSystemChange.FromPath(fullPath, category, configuration.HashLimitMB,
                    configuration.ScopeHash, retainMissing: true);

            if (change is null)
            {
                return null;
            }

            change.ScopeHash = configuration.ScopeHash;
            change.ObservationSource = ObservationSources.UsnJournal;
            change.UsnValue = record.Usn;
            change.UsnReason = UsnReasonMapper.FormatReasonFlags(record.Reason);
            change.PathUnresolved = unresolved;
            change.DateTime = record.GetDateTimeUtc().ToLocalTime();

            change.AttributionStatus = AttributionStatus.Unattributed;
            change.AttributionMethod = "None";
            change.AttributionConfidence = "None";
            change.AttributionMissingReason = "UsnJournalCarriesNoAttribution";

            return change;
        }

        internal static bool IsUnresolved(string parentPath) =>
            string.IsNullOrEmpty(parentPath) ||
            parentPath.EndsWith(UnresolvedSegment, StringComparison.Ordinal);

        /// <summary>Joins a resolved parent path to a record's filename.</summary>
        /// <remarks>
        /// The separator is hard-coded rather than taken from <see cref="Path.DirectorySeparatorChar"/>
        /// because these are NTFS volume paths, which are backslash-delimited irrespective of the
        /// separator the running runtime happens to prefer.
        /// </remarks>
        internal static string CombinePath(string parentPath, string filename)
        {
            const char separator = '\\';

            if (string.IsNullOrEmpty(filename))
            {
                return parentPath;
            }

            if (string.IsNullOrEmpty(parentPath))
            {
                return filename;
            }

            return parentPath.EndsWith(separator) || parentPath.EndsWith('/')
                ? parentPath + filename
                : parentPath + separator + filename;
        }

        /// <summary>Builds the namespace-only change for a record whose path could not be resolved.</summary>
        private static FileSystemChange BuildUnresolved(string fullPath, ChangeCategory category, char driveLetter) =>
            new()
            {
                Id = Ulid.NewUlid().ToString(),
                ChangeCategory = category,
                ConfigChangeType = ConfigChangeType.FileSystem,
                Entity = fullPath,
                FullPath = fullPath,
                DateTime = DateTime.Now,
                ObjectType = ObjectType.Unknown,
                SourceComputer = Environment.MachineName,
                CurrentHash = string.Empty,
                PreviousHash = string.Empty,
                ACLs = string.Empty
            };
    }
}
