using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Attribution;
using WinFIMLog.FIM;
using WinFIMLog.Jobs;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class OptionalAttributionTests
{
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = CreateOpCodeMap();

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
    public void File_attribution_does_not_send_capture_state_to_the_kernel_provider()
    {
        var monitor = typeof(FileSystemEventAttributionMonitor).GetMethod("Monitor",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.IsFalse(CallsMethod(monitor, typeof(TraceEventSession), nameof(TraceEventSession.CaptureState)),
            "CaptureState is a user-mode provider command and fails with E_INVALIDARG for the kernel provider.");
    }

    [TestMethod]
    public void File_attribution_uses_one_stable_etw_session_and_recognizes_legacy_names()
    {
        Assert.AreEqual("WinFIMLog-FileIO", FileSystemEventAttributionMonitor.SessionName);
        Assert.IsTrue(typeof(FileSystemEventAttributionMonitor.Attribution).IsValueType,
            "The ETW hot path must not allocate an attribution wrapper for every event.");
        Assert.IsTrue(FileSystemEventAttributionMonitor.IsLegacySessionName("WinFIMLog-FileIO-1234"));
        Assert.IsFalse(FileSystemEventAttributionMonitor.IsLegacySessionName("WinFIMLog-FileIO"));
        Assert.IsFalse(FileSystemEventAttributionMonitor.IsLegacySessionName("WinFIMLog-FileIO-other"));
        Assert.IsFalse(FileSystemEventAttributionMonitor.IsLegacySessionName("Unrelated-1234"));
    }

    [TestMethod]
    public void Missing_states_are_explicit_contract_values()
    {
        Assert.IsTrue(Enum.IsDefined(AttributionStatus.RundownMissing));
        Assert.IsTrue(Enum.IsDefined(AttributionStatus.ImpersonationAmbiguous));
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

    private static bool CallsMethod(MethodInfo subject, Type declaringType, string methodName)
    {
        var il = subject.GetMethodBody()!.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            short value = il[offset++];
            if (value == 0xfe)
            {
                value = (short)(0xfe00 | il[offset++]);
            }

            var opCode = OpCodesByValue[value];

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var called = subject.Module.ResolveMethod(BitConverter.ToInt32(il, offset));
                if (called?.DeclaringType == declaringType && called.Name == methodName)
                {
                    return true;
                }
            }

            offset += OperandSize(opCode.OperandType, il, offset);
        }

        return false;
    }

    private static IReadOnlyDictionary<short, OpCode> CreateOpCodeMap()
    {
        var result = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opCode)
            {
                result[opCode.Value] = opCode;
            }
        }

        return result;
    }

    private static int OperandSize(OperandType operandType, byte[] il, int offset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or
            OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or
            OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, offset) * 4),
        _ => throw new InvalidOperationException($"Unsupported IL operand type {operandType}.")
    };
}
