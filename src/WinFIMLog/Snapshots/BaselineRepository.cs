using System;
using System.Collections.Generic;
using System.Linq;
using WinFIMLog.Data;

namespace WinFIMLog.Snapshots
{
    /// <summary>Owns the baseline lifecycle and its atomic completion boundary.</summary>
    public sealed class BaselineRepository
    {
        private readonly ILiteDbContext context;

        public BaselineRepository(ILiteDbContext context) => this.context = context;

        public BaselineMetadata Begin(BaselineSource source, string scopeHash, string sourceIdentity,
            int schemaVersion = 1, string algorithmVersion = "sha256-v1", string? startCursor = null)
        {
            InvalidateInapplicable(source, scopeHash, sourceIdentity, schemaVersion, algorithmVersion);
            var baseline = new BaselineMetadata
            {
                Source = source,
                ScopeHash = scopeHash,
                SourceIdentity = sourceIdentity,
                SchemaVersion = schemaVersion,
                AlgorithmVersion = algorithmVersion,
                StartedAt = DateTimeOffset.UtcNow,
                Status = BaselineStatus.Building,
                StartCursor = startCursor
            };
            context.Baselines.Insert(baseline);
            return baseline;
        }

        public BaselineMetadata? LatestComplete(BaselineSource source, string scopeHash, string sourceIdentity,
            int schemaVersion = 1, string algorithmVersion = "sha256-v1") =>
            context.Baselines.Query()
                .Where(x => x.Source == source && x.ScopeHash == scopeHash &&
                    x.SourceIdentity == sourceIdentity && x.SchemaVersion == schemaVersion &&
                    x.AlgorithmVersion == algorithmVersion && x.Status == BaselineStatus.Complete)
                .OrderByDescending(x => x.CompletedAt).FirstOrDefault();

        public IReadOnlyList<BaselineMember> Members(string baselineId) =>
            context.BaselineMembers.Find(x => x.BaselineId == baselineId).ToList();

        public BaselineMetadata? Find(string baselineId) => context.Baselines.FindById(baselineId);

        public IReadOnlyList<ReconciliationResult> PendingResults(int limit = 500) =>
            context.ReconciliationResults.Query().Where(x => x.DeliveredAt == null).Limit(limit).ToList();

        public void RecordDeliveryAttempt(ReconciliationResult result, bool delivered)
        {
            result.DeliveryAttempts++;
            if (delivered) result.DeliveredAt = DateTimeOffset.UtcNow;
            context.ReconciliationResults.Update(result);
        }

        public IReadOnlyList<ReconciliationResult> ReconcileAndComplete(BaselineMetadata baseline, IEnumerable<BaselineMember> members,
            string? endCursor = null)
        {
            if (baseline.Status != BaselineStatus.Building)
                throw new InvalidOperationException("Only a building baseline can be completed.");

            var materialised = members.ToList();
            if (materialised.GroupBy(x => x.Identity, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                throw new InvalidOperationException("A baseline cannot contain duplicate identities.");

            var previous = LatestComplete(baseline.Source, baseline.ScopeHash, baseline.SourceIdentity,
                baseline.SchemaVersion, baseline.AlgorithmVersion);
            var results = Diff(baseline.Id, previous, materialised);
            baseline.Status = BaselineStatus.Reconciling;
            context.Baselines.Update(baseline);

            if (!context.ExecuteTransaction(() =>
            {
                foreach (var member in materialised)
                {
                    member.BaselineId = baseline.Id;
                    context.BaselineMembers.Insert(member);
                }
                foreach (var result in results) context.ReconciliationResults.Insert(result);
                baseline.ItemCount = materialised.Count;
                baseline.EndCursor = endCursor;
                baseline.CompletedAt = DateTimeOffset.UtcNow;
                baseline.Status = BaselineStatus.Complete;
                context.Baselines.Update(baseline);
            })) throw new InvalidOperationException("The baseline completion transaction did not commit.");
            return results;
        }

        /// <summary>Completes a cursorless scan using the second observation for every identity.</summary>
        public IReadOnlyList<ReconciliationResult> ReconcileAndCompleteAfterSecondPass(BaselineMetadata baseline,
            IEnumerable<BaselineMember> firstPass, IEnumerable<BaselineMember> secondPass)
        {
            if (baseline.Status != BaselineStatus.Building && baseline.Status != BaselineStatus.Reconciling)
                throw new InvalidOperationException("The baseline is not being built.");
            // An item seen in pass one but absent in pass two was transient and cannot represent
            // persistent state. Pass-two evidence wins when an item changed during the scan.
            baseline.Status = BaselineStatus.Building;
            return ReconcileAndComplete(baseline, secondPass);
        }

        public void MarkInvalid(BaselineMetadata baseline, string reason)
        {
            baseline.Status = BaselineStatus.Invalid;
            baseline.InvalidReason = reason;
            context.Baselines.Update(baseline);
        }

        private List<ReconciliationResult> Diff(string baselineId, BaselineMetadata? previous, List<BaselineMember> current)
        {
            if (previous is null) return new List<ReconciliationResult>();
            var before = Members(previous.Id).ToDictionary(x => x.Identity, StringComparer.OrdinalIgnoreCase);
            var after = current.ToDictionary(x => x.Identity, StringComparer.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow;
            var output = new List<ReconciliationResult>();
            foreach (var pair in after)
            {
                if (!before.TryGetValue(pair.Key, out var old))
                    output.Add(Result(baselineId, pair.Key, ReconciliationChange.Created, null, pair.Value.Path, previous.Id, now));
                else if (!string.Equals(old.Fingerprint, pair.Value.Fingerprint, StringComparison.Ordinal))
                    output.Add(Result(baselineId, pair.Key, ReconciliationChange.Changed, old.Path, pair.Value.Path, previous.Id, now));
            }
            foreach (var pair in before.Where(x => !after.ContainsKey(x.Key)))
                output.Add(Result(baselineId, pair.Key, ReconciliationChange.Deleted, pair.Value.Path, null, previous.Id, now));
            return output;
        }

        private static ReconciliationResult Result(string baselineId, string identity, ReconciliationChange change, string? oldPath,
            string? newPath, string previousId, DateTimeOffset when) => new()
        {
            BaselineId = baselineId,
            PreviousBaselineId = previousId, Identity = identity, Change = change,
            OldPath = oldPath, NewPath = newPath, DetectedAt = when
        };

        private void InvalidateInapplicable(BaselineSource source, string scopeHash, string identity,
            int schema, string algorithm)
        {
            foreach (var item in context.Baselines.Find(x => x.Source == source && x.Status == BaselineStatus.Complete).ToList())
            {
                if (item.ScopeHash == scopeHash && item.SourceIdentity == identity &&
                    item.SchemaVersion == schema && item.AlgorithmVersion == algorithm) continue;
                item.Status = BaselineStatus.Invalid;
                item.InvalidReason = "Applicability identity changed";
                context.Baselines.Update(item);
            }
        }
    }
}
