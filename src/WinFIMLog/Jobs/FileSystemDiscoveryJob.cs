using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using WinFIMLog.Data;
using WinFIMLog.FIM;
using WinFIMLog.IO;

namespace WinFIMLog.Jobs
{
    internal class FileSystemDiscoveryJob
    {
        private readonly ILiteDbContext _ctx;

        private readonly ILogger _logger;

        private readonly IBuffer<FileSystemChange> _messageStore;

        private readonly Settings _settings;

        public FileSystemDiscoveryJob(ILogger logger, IBuffer<FileSystemChange> fsStore, ILiteDbContext ctx, Settings settings)
        {
            _logger = logger;
            _messageStore = fsStore;
            _ctx = ctx;
            _settings = settings;
        }

        /// <summary>
        ///     Start file discovery with filtering
        /// </summary>
        /// <exception cref="System.IO.IOException">
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// </exception>
        /// <exception cref="AggregateException">
        /// </exception>
        /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// </exception>
        /// <exception cref="OverflowException">
        /// </exception>
        internal void Start()
        {
            _logger.LogInformation("Starting inventory discovery (path and hash)...");

            var files = RunNtfsDiscovery(out var sw);

            var filtered = FilterByConfig(sw, files);

            filtered = ContinueFromLastScan(sw, filtered);

            var filesCount = files.Count.ToString("N0");
            var filteredAfterLastScanCount = filtered.Count.ToString("N0");
            var diff = (files.Count - filtered.Count).ToString("N0");
            var percentage = files.IsEmpty
                ? 0d.ToString("N2")
                : ((double)(files.Count - filtered.Count) * 100 / files.Count).ToString("N2");

            Debug.WriteLine("Number of all files on the device: {0}\n" +
                "Number of files to be monitored: {1}\n" +
                "Filtered out: {2} (%{3})",
                filesCount, filteredAfterLastScanCount, diff, percentage);
            _logger.LogInformation("Number of all files on the device: {files:l}\n" +
                "Number of files to be monitored: {filteredCount:l}\n" +
                "Filtered out: {diff:l} (%{percentage:l})",
                filesCount, filteredAfterLastScanCount, diff, percentage);

            UpdateDiscoveryDatabase(sw, filtered);
        }

        private void Add(string path)
        {
            var change = FileSystemChange.FromPath(path, ChangeCategory.Discovery, _settings.HashLimitMB, _settings.ScopeHash);
            if (change != null)
            {
                if (change.ObjectType == FileSystem.ObjectType.File)
                {
                    change.PreviousHash = FileSystemChange.RetrievePreviousHash(path, _ctx);
                }

                if (!change.CurrentHash.Equals(change.PreviousHash, StringComparison.InvariantCultureIgnoreCase))
                {
                    _messageStore.Add(change);
                }
            }
        }

        private List<string> ContinueFromLastScan(Stopwatch sw, List<string> filtered)
        {
            Debug.WriteLine("Filtering out the data in the database...");
            sw.Restart();
            var initialCount = filtered.Count;

            var existing = new HashSet<string>(
                _ctx.FileSystemChanges.FindAll().Select(change => change.Entity),
                StringComparer.OrdinalIgnoreCase);
            var filteredOut = filtered.RemoveAll(existing.Contains);
            sw.Stop();
            Debug.WriteLine("Filtering out completed: {0}", sw.Elapsed);
            if (filteredOut > 0)
            {
                Debug.WriteLine("Number of files not in database: {0} (filtered out {1}, %{2})",
                    filtered.Count.ToString("N0"), filteredOut, (double)filteredOut * 100 / initialCount);
            }
            else
            {
                Debug.WriteLine("Nothing to filter out. Discovery database is empty.");
            }

            return filtered;
        }

        /// <summary>
        ///     Runs multiple filterin options to optimize the scan
        /// </summary>
        /// <param name="sw">
        ///     Stopwatch for statistics
        /// </param>
        /// <param name="files">
        ///     List of initial file paths
        /// </param>
        /// <returns>
        ///     List of filtered out file paths
        /// </returns>
        /// <exception cref="RegexMatchTimeoutException">
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// </exception>
        /// <exception cref="AggregateException">
        /// </exception>
        /// <exception cref="OverflowException">
        /// </exception>
        /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">
        /// </exception>
        private List<string> FilterByConfig(Stopwatch sw, ConcurrentBag<string> files)
        {
            Debug.WriteLine("Starting filtering by configuration values...");
            sw.Restart();
            var filtered = _settings.FilterPaths(files);
            sw.Stop();
            Debug.WriteLine("Path filtering completed: {0}", sw.Elapsed);
            return filtered;
        }

        /// <summary>
        ///     Initiates the NTFS scan
        /// </summary>
        /// <param name="sw">
        ///     Stopwatch for statistics
        /// </param>
        /// <returns>
        ///     List of all files in the device
        /// </returns>
        /// <exception cref="System.IO.IOException">
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// </exception>
        /// <exception cref="AggregateException">
        /// </exception>
        private ConcurrentBag<string> RunNtfsDiscovery(out Stopwatch sw)
        {
            Debug.WriteLine("Starting file search...");
            sw = new Stopwatch();
            sw.Start();
            var files = FileSystem.InvokeNtfsSearch();
            sw.Stop();
            Debug.WriteLine("Filesystem search completed: {0}", sw.Elapsed);
            Debug.WriteLine("Number of all files in the device: {filesCount}", files.Count.ToString("N0"));
            return files;
        }

        private void UpdateDiscoveryDatabase(Stopwatch sw, List<string> filtered)
        {
            sw.Restart();
            var concurrency = _settings.DiscoveryConcurrency;
            ProcessPaths(filtered, concurrency, Add);
            sw.Stop();
            Debug.WriteLine("Database update completed in {0} with concurrency {1}", sw.Elapsed, concurrency);
            _logger.LogInformation(
                "Discovery processed {Count} paths in {Elapsed} with concurrency {Concurrency}",
                filtered.Count, sw.Elapsed, concurrency);
        }

        /// <summary>Processes discovery paths without exceeding the configured worker count.</summary>
        /// <remarks>
        /// Explicitly bounded parallelism prevents hashing and metadata reads from oversubscribing
        /// storage. Do not use the unbounded default Parallel.ForEach overload on this I/O path.
        /// </remarks>
        internal static void ProcessPaths(IEnumerable<string> paths, int concurrency, Action<string> process) =>
            Parallel.ForEach(paths, new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency
            }, process);
    }
}
