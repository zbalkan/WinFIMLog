using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Configuration;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class SettingsAtomicPublicationTests
{
    [TestMethod]
    public void Concurrent_readers_observe_one_complete_generation()
    {
        var first = Generation("A", @"C:\A");
        var second = Generation("B", @"C:\B");
        var settings = new Settings(first);
        var mixed = new ConcurrentQueue<string>();

        Parallel.For(0, 20_000, index =>
        {
            settings.PublishForTest((index & 1) == 0 ? first : second);
            var observed = settings.Capture();
            var validA = observed.ScopeHash == "A" && observed.MonitoredPaths[0] == @"C:\A" &&
                observed.IsMonitoredPath(@"C:\A\evidence.txt") && !observed.IsMonitoredPath(@"C:\B\evidence.txt");
            var validB = observed.ScopeHash == "B" && observed.MonitoredPaths[0] == @"C:\B" &&
                observed.IsMonitoredPath(@"C:\B\evidence.txt") && !observed.IsMonitoredPath(@"C:\A\evidence.txt");
            if (!validA && !validB) mixed.Enqueue($"{observed.ScopeHash}:{observed.MonitoredPaths[0]}");
        });

        Assert.IsEmpty(mixed);
    }

    internal static EffectiveSettings GenerationForTest(string hash, string path) => Generation(hash, path);

    private static EffectiveSettings Generation(string hash, string path)
    {
        var escaped = Regex.Escape(path);
        return new EffectiveSettings
        {
            ScopeHash = hash,
            MonitoredPaths = [path],
            MonitoredKeys = [@"HKEY_LOCAL_MACHINE\SOFTWARE\WinFIMLog"],
            ExcludedPaths = [],
            ExcludedExtensions = [],
            ExcludedKeys = [],
            MonitoredPathsPattern = new Regex($@"(?:^({escaped})\\?.*$)", RegexOptions.IgnoreCase),
            MonitoredKeysPattern = new Regex(".*"),
            RegistryScopeMatcher = new RegistryScopeMatcher(
                [@"HKEY_LOCAL_MACHINE\SOFTWARE\WinFIMLog"], [])
        };
    }
}
