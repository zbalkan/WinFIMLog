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

    [TestMethod]
    public void CacheEvictsAMappingAtCapacity()
    {
        var cache = new RegistryKcbCache(2);
        cache.Update(1, "one");
        cache.Update(2, "two");

        cache.Update(3, "three");

        var retainedOldMappings = (cache.TryGet(1, out _) ? 1 : 0) +
                                  (cache.TryGet(2, out _) ? 1 : 0);
        Assert.AreEqual(1, retainedOldMappings);
        Assert.IsTrue(cache.TryGet(3, out _));
    }

    [TestMethod]
    public void UpdatingExistingHandleDoesNotEvictAtCapacity()
    {
        var cache = new RegistryKcbCache(2);
        cache.Update(1, "old");
        cache.Update(2, "two");

        cache.Update(1, "new");

        Assert.IsTrue(cache.TryGet(1, out var path));
        Assert.AreEqual("new", path);
        Assert.IsTrue(cache.TryGet(2, out _));
    }
}
