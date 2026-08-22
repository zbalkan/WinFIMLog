using System;
using System.Collections.Generic;
using System.Linq;
using WinFIMLog.Data;
using WinFIMLog.Integrity;

namespace WinFIMLog.Snapshots
{
    /// <summary>Owns the baseline lifecycle and its atomic completion boundary.</summary>
    public sealed class BaselineRepository
    {
        private readonly ILiteDbContext context;
        private readonly ITpmBaselineIntegrity? integrity;

        public BaselineRepository(ILiteDbContext context, ITpmBaselineIntegrity? integrity = null)
        {
            this.context = context;
            this.integrity = integrity;
        }

        public BaselineMetadata Begin(BaselineSource source, string scopeHash, string sourceIdentity,
            int schemaVersion = 1, BaselineAlgorithm algorithm = BaselineAlgorithm.Sha256, string? startCursor = null)
        {
            var baseline = new BaselineMetadata
            {
                Source = source,
                ScopeHash = scopeHash,
                SourceIdentity = sourceIdentity,
                SchemaVersion = schemaVersion,
                Algorithm = algorithm,
                StartedAt = DateTimeOffset.UtcNow,
                Status = BaselineStatus.Building,
                StartCursor = startCursor
            };
            if (!context.ExecuteTransaction(() =>
            {
                SupersedeInapplicable(source, scopeHash, sourceIdentity, schemaVersion, algorithm);
                context.Baselines.Insert(baseline);
            }))
            {
                throw new InvalidOperationException("Could not commit the baseline start transaction.");
            }

            return baseline;
        }

        /// <summary>Retains two complete generations and removes abandoned staging data.</summary>
        public void CompactAfterCompletion(BaselineMetadata completed, int generationsToKeep = 2)
        {
            // Retention is per comparable lineage. TPM and source-native baselines must not evict
            // one another because the native lineage is required when TPM signing later fails.
            var keep = context.Baselines.Find(x => x.Source == completed.Source &&
                    x.Status == BaselineStatus.Complete).ToList()
                .GroupBy(x => (x.ScopeHash, x.SourceIdentity, x.SchemaVersion, x.Algorithm))
                .SelectMany(group => group.OrderByDescending(x => x.CompletedAt)
                    .Take(Math.Max(1, generationsToKeep)))
                .Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var baseline in context.Baselines.Find(x => x.Source == completed.Source).ToList())
            {
                if (keep.Contains(baseline.Id))
                {
                    continue;
                }

                if (!context.ExecuteTransaction(() =>
                    context.BaselineMembers.DeleteMany(x => x.BaselineId == baseline.Id)))
                {
                    throw new InvalidOperationException("Could not compact baseline members.");
                }

                if (context.ReconciliationResults.Exists(x => x.BaselineId == baseline.Id && x.DeliveredAt == null))
                {
                    continue;
                }

                if (!context.ExecuteTransaction(() =>
                {
                    context.ReconciliationResults.DeleteMany(x => x.BaselineId == baseline.Id);
                    context.Baselines.Delete(baseline.Id);
                }))
                {
                    throw new InvalidOperationException("Could not compact baseline metadata.");
                }
            }
        }

        public BaselineMetadata? Find(string baselineId) => context.Baselines.FindById(baselineId);

        public BaselineMetadata? LatestComplete(BaselineSource source, string scopeHash, string sourceIdentity,
                            int schemaVersion = 1, BaselineAlgorithm algorithm = BaselineAlgorithm.Sha256) =>
            context.Baselines.Query()
                .Where(x => x.Source == source && x.ScopeHash == scopeHash &&
                    x.SourceIdentity == sourceIdentity && x.SchemaVersion == schemaVersion &&
                    x.Algorithm == algorithm && x.Status == BaselineStatus.Complete &&
                    x.Applicability == BaselineApplicability.Current)
                .OrderByDescending(x => x.CompletedAt).FirstOrDefault();

        /// <summary>
        ///     Reinstates the latest complete baseline for a fallback algorithm after a new TPM
        ///     baseline was started but could not be sealed. Begin supersedes incompatible
        ///     lineages eagerly, so this restores the previous comparable baseline before diffing.
        /// </summary>
        public void RestoreFallbackApplicability(BaselineMetadata fallback)
        {
            var previous = context.Baselines.Query()
                .Where(x => x.Id != fallback.Id && x.Source == fallback.Source && x.ScopeHash == fallback.ScopeHash &&
                    x.SourceIdentity == fallback.SourceIdentity && x.SchemaVersion == fallback.SchemaVersion &&
                    x.Algorithm == fallback.Algorithm && x.Status == BaselineStatus.Complete)
                .OrderByDescending(x => x.CompletedAt).FirstOrDefault();
            if (previous is null || previous.Applicability == BaselineApplicability.Current)
            {
                return;
            }

            previous.Applicability = BaselineApplicability.Current;
            if (!context.ExecuteTransaction(() => context.Baselines.Update(previous)))
            {
                throw new InvalidOperationException("Could not restore the fallback baseline applicability.");
            }
        }

        public void MarkInvalid(BaselineMetadata baseline, string reason)
        {
            baseline.Status = BaselineStatus.Invalid;
            baseline.InvalidReason = reason;
            if (!context.ExecuteTransaction(() => context.Baselines.Update(baseline)))
            {
                throw new InvalidOperationException("Could not commit invalid baseline state.");
            }
        }

        public IReadOnlyList<BaselineMember> Members(string baselineId) =>
                    context.BaselineMembers.Find(x => x.BaselineId == baselineId).ToList();

        public IReadOnlyList<ReconciliationResult> PendingResults(int limit = 500) =>
            context.ReconciliationResults.Query().Where(x => x.DeliveredAt == null).Limit(limit).ToList();

        public IReadOnlyList<ReconciliationResult> ReconcileAndComplete(BaselineMetadata baseline, IEnumerable<BaselineMember> members,
            string? endCursor = null)
        {
            if (baseline.Status != BaselineStatus.Building)
            {
                throw new InvalidOperationException("Only a building baseline can be completed.");
            }

            var materialised = members.ToList();
            if (materialised.GroupBy(x => x.Identity, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            {
                throw new InvalidOperationException("A baseline cannot contain duplicate identities.");
            }

            var previous = LatestComplete(baseline.Source, baseline.ScopeHash, baseline.SourceIdentity,
                baseline.SchemaVersion, baseline.Algorithm);
            var results = Diff(baseline.Id, previous, materialised);
            baseline.Status = BaselineStatus.Reconciling;
            if (!context.ExecuteTransaction(() => context.Baselines.Update(baseline)))
            {
                throw new InvalidOperationException("Could not commit baseline reconciliation state.");
            }

            // Stage members in bounded transactions so a large scan cannot monopolise
            // the single embedded writer. Only the final promotion makes them authoritative.
            foreach (var chunk in materialised.Chunk(500))
            {
                if (!context.ExecuteTransaction(() =>
                {
                    foreach (var member in chunk)
                    {
                        member.BaselineId = baseline.Id;
                        context.BaselineMembers.Insert(member);
                    }
                }))
                {
                    throw new InvalidOperationException("A baseline staging transaction did not commit.");
                }
            }

            if (!context.ExecuteTransaction(() =>
            {
                foreach (var result in results)
                {
                    context.ReconciliationResults.Insert(result);
                }

                baseline.ItemCount = materialised.Count;
                baseline.EndCursor = endCursor;
                baseline.CompletedAt = DateTimeOffset.UtcNow;
                baseline.Status = BaselineStatus.Complete;
                context.Baselines.Update(baseline);
            }))
            {
                throw new InvalidOperationException("The baseline completion transaction did not commit.");
            }

            return results;
        }

        /// <summary>Completes a cursorless scan after the coordinator establishes convergence.</summary>
        public IReadOnlyList<ReconciliationResult> ReconcileAndCompleteAfterConvergence(BaselineMetadata baseline,
            IEnumerable<BaselineMember> convergedMembers)
        {
            if (baseline.Status != BaselineStatus.Building && baseline.Status != BaselineStatus.Reconciling)
            {
                throw new InvalidOperationException("The baseline is not being built.");
            }

            baseline.Status = BaselineStatus.Building;
            return ReconcileAndComplete(baseline, convergedMembers);
        }

        public void RecordDeliveryAttempt(ReconciliationResult result, bool delivered)
        {
            result.DeliveryAttempts++;
            if (delivered)
            {
                result.DeliveredAt = DateTimeOffset.UtcNow;
            }

            if (!context.ExecuteTransaction(() => context.ReconciliationResults.Update(result)))
            {
                throw new InvalidOperationException("Could not commit reconciliation delivery state.");
            }
        }

        private static ReconciliationResult Result(string baselineId, string identity, ReconciliationChange change, string? oldPath,
            string? newPath, string previousId, DateTimeOffset when) => new()
            {
                BaselineId = baselineId,
                PreviousBaselineId = previousId,
                Identity = identity,
                Change = change,
                OldPath = oldPath,
                NewPath = newPath,
                DetectedAt = when
            };

        private List<ReconciliationResult> Diff(string baselineId, BaselineMetadata? previous, List<BaselineMember> current)
        {
            if (previous is null)
            {
                return [];
            }

            var priorMembers = Members(previous.Id);
            if (integrity is not null && !integrity.TryVerify(previous, priorMembers, out var integrityReason))
            {
                throw new InvalidOperationException($"The prior baseline {previous.Id} failed TPM integrity verification: {integrityReason}");
            }

            var before = priorMembers.ToDictionary(x => x.Identity, StringComparer.OrdinalIgnoreCase);
            var after = current.ToDictionary(x => x.Identity, StringComparer.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow;
            var output = new List<ReconciliationResult>();
            foreach (var pair in after)
            {
                if (!before.TryGetValue(pair.Key, out var old))
                {
                    output.Add(Result(baselineId, pair.Key, ReconciliationChange.Created, null, pair.Value.Path, previous.Id, now));
                }
                else if (!string.Equals(old.Fingerprint, pair.Value.Fingerprint, StringComparison.Ordinal))
                {
                    output.Add(Result(baselineId, pair.Key, ReconciliationChange.Changed, old.Path, pair.Value.Path, previous.Id, now));
                }
            }
            foreach (var pair in before.Where(x => !after.ContainsKey(x.Key)))
            {
                output.Add(Result(baselineId, pair.Key, ReconciliationChange.Deleted, pair.Value.Path, null, previous.Id, now));
            }

            return output;
        }

        private void SupersedeInapplicable(BaselineSource source, string scopeHash, string identity,
            int schema, BaselineAlgorithm algorithm)
        {
            foreach (var item in context.Baselines.Find(x => x.Source == source && x.Status == BaselineStatus.Complete).ToList())
            {
                if (item.ScopeHash == scopeHash && item.SourceIdentity == identity &&
                    item.SchemaVersion == schema && item.Algorithm == algorithm)
                {
                    continue;
                }

                item.Applicability = BaselineApplicability.Superseded;
                context.Baselines.Update(item);
            }
        }
    }
}
