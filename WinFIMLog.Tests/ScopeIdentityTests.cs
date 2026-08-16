using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Configuration;

namespace WinFIMLog.Tests
{
    [TestClass]
    public sealed class ScopeIdentityTests
    {
        [TestMethod]
        public void Compute_IsIndependentOfOrderingCaseAndDuplicates()
        {
            var first = ScopeIdentity.Compute([@"C:\Windows", @"C:\Data"], [@"C:\Temp"], [".log"],
                [ScopeIdentity.PolicyKey, ScopeIdentity.PreferenceKey], []);
            var second = ScopeIdentity.Compute([@"c:\data\", @"C:\WINDOWS", @"C:\Windows"], [@"c:\temp\"], [".LOG"],
                [ScopeIdentity.PreferenceKey.ToLowerInvariant(), ScopeIdentity.PolicyKey], []);
            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public void EnsureConfigurationKeysMonitored_AddsBothLocationsOnce()
        {
            ICollection<string> keys = new List<string> { @"HKEY_LOCAL_MACHINE\Software\Example" };
            ScopeIdentity.EnsureConfigurationKeysMonitored(keys);
            ScopeIdentity.EnsureConfigurationKeysMonitored(keys);
            CollectionAssert.Contains((System.Collections.ICollection)keys, ScopeIdentity.PolicyKey);
            Assert.AreEqual(3, keys.Count);
        }

        [DataTestMethod]
        [DataRow(@"HKEY_LOCAL_MACHINE")]
        [DataRow(@"HKEY_LOCAL_MACHINE\SOFTWARE")]
        [DataRow(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\WinFIMLog")]
        [DataRow(@"HKEY_LOCAL_MACHINE\SOFTWARE\WinFIMLog")]
        public void RejectProtectedExclusions_RejectsCoveringKey(string exclusion) =>
            Assert.ThrowsException<ConfigurationValidationException>(() => ScopeIdentity.RejectProtectedExclusions([exclusion]));
    }
}
