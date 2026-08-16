using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Jobs;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class RegistryKcbCacheTests
{
    [TestMethod]
    public void DeletedKcbHandleIsNoLongerResolvable()
    {
        var cache = new RegistryKcbCache();

        cache.Update(42, @"\REGISTRY\MACHINE\Software\WinFIMLog");
        Assert.IsTrue(cache.TryGet(42, out var path));
        Assert.AreEqual(@"\REGISTRY\MACHINE\Software\WinFIMLog", path);

        cache.Remove(42);

        Assert.IsFalse(cache.TryGet(42, out _));
    }

    [TestMethod]
    public void InvalidMappingsAreIgnored()
    {
        var cache = new RegistryKcbCache();

        cache.Update(0, @"\REGISTRY\MACHINE");
        cache.Update(42, string.Empty);

        Assert.IsFalse(cache.TryGet(0, out _));
        Assert.IsFalse(cache.TryGet(42, out _));
    }
}
