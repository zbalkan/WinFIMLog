using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Attribution;
using WinFIMLog.FIM;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class OptionalAttributionTests
{
    [TestMethod]
    public void Process_sequence_prevents_pid_reuse_misattribution()
    {
        var store = new ProcessInstanceStore();
        store.Record(new ProcessInstanceEvidence(321, 100, "first.exe", DateTimeOffset.UtcNow));
        store.Record(new ProcessInstanceEvidence(321, 101, "second.exe", DateTimeOffset.UtcNow));

        Assert.IsTrue(store.TryResolve(321, 100, out var first));
        Assert.IsTrue(store.TryResolve(321, 101, out var second));
        Assert.AreEqual("first.exe", first.ProcessName);
        Assert.AreEqual("second.exe", second.ProcessName);
        Assert.IsFalse(store.TryResolve(321, 99, out _));
    }

    [TestMethod]
    public void Process_exit_removes_only_the_matching_instance()
    {
        var store = new ProcessInstanceStore();
        store.Record(new ProcessInstanceEvidence(321, 100, "first.exe", DateTimeOffset.UtcNow));
        store.Record(new ProcessInstanceEvidence(321, 101, "second.exe", DateTimeOffset.UtcNow));

        store.End(321, 100);

        Assert.IsFalse(store.TryResolve(321, 100, out _));
        Assert.IsTrue(store.TryResolve(321, 101, out _));
    }

    [TestMethod]
    public void Disabled_sacl_tier_has_no_scope_dependency() => new SaclAttributionOptions { Enabled = false }.Validate();

    [TestMethod]
    public void Enabled_sacl_tier_requires_small_explicit_scope()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SaclAttributionOptions { Enabled = true }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new SaclAttributionOptions { Enabled = true, FileScopes = [@"C:\Users\*"] }.Validate());

        new SaclAttributionOptions { Enabled = true, FileScopes = [@"C:\Sensitive"] }.Validate();
    }

    [TestMethod]
    public void Missing_states_are_explicit_contract_values()
    {
        Assert.IsTrue(Enum.IsDefined(AttributionStatus.RundownMissing));
        Assert.IsTrue(Enum.IsDefined(AttributionStatus.ImpersonationAmbiguous));
    }
}
