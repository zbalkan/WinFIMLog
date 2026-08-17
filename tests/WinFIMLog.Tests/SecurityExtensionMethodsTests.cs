using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.FIM;
using WinFIMLog.IO.Security;

namespace WinFIMLog.Tests
{
    [TestClass]
    public sealed class SecurityExtensionMethodsTests
    {
        [TestMethod]
        public void AccountNameOrSidPrefersLocalLogonSessionWithoutOnlineTranslation()
        {
            const string sid = "S-1-5-21-1000-2000-3000-3999";
            var translationAttempted = false;

            var result = ExtensionMethods.AccountNameOrSid(
                sid,
                () =>
                {
                    translationAttempted = true;
                    throw new InvalidOperationException();
                },
                requestedSid => requestedSid == sid ? @"CONTOSO\Alice" : null);

            Assert.AreEqual(@"CONTOSO\Alice", result);
            Assert.IsFalse(translationAttempted);
        }

        [TestMethod]
        public void AccountNameOrSidReturnsSidWhenDomainResolutionFails()
        {
            const string sid = "S-1-5-21-1000-2000-3000-4000";
            var result = ExtensionMethods.AccountNameOrSid(
                sid,
                () => throw new Win32Exception(1789));

            Assert.AreEqual(sid, result);
        }

        [TestMethod]
        public void AccountNameOrSidReturnsSidWhenIdentityIsNotMapped()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("IdentityNotMappedException requires Windows Principal support.");
                return;
            }
            const string sid = "S-1-5-21-1000-2000-3000-4001";
            var result = ExtensionMethods.AccountNameOrSid(
                sid,
                () => throw new IdentityNotMappedException());

            Assert.AreEqual(sid, result);
        }

        [TestMethod]
        [DataRow(typeof(UnauthorizedAccessException))]
        [DataRow(typeof(SecurityException))]
        [DataRow(typeof(FileNotFoundException))]
        [DataRow(typeof(DirectoryNotFoundException))]
        [DataRow(typeof(IOException))]
        public void FileSystemChangeContinuesWhenAclIsUnavailable(Type exceptionType)
        {
            var exception = (Exception)Activator.CreateInstance(exceptionType)!;

            var result = FileSystemChange.GetAclOrEmpty(() => throw exception);

            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void FileSystemChangeKeepsAvailableAcl()
        {
            const string acl = "{\"Owner\":\"CONTOSO\\\\Alice\"}";

            var result = FileSystemChange.GetAclOrEmpty(() => acl);

            Assert.AreEqual(acl, result);
        }

        [TestMethod]
        public void AclStringPoolReusesEqualPayloads()
        {
            var pool = new AclStringPool();
            var first = new string("{\"Owner\":\"CONTOSO\\\\Alice\"}".ToCharArray());
            var duplicate = new string(first.ToCharArray());

            var canonical = pool.GetOrAdd(first);
            var reused = pool.GetOrAdd(duplicate);

            Assert.AreSame(canonical, reused);
        }

        [TestMethod]
        public void AclStringPoolDoesNotGrowPastCapacity()
        {
            var pool = new AclStringPool(1);
            var first = pool.GetOrAdd(new string('a', 2));
            var uncached = pool.GetOrAdd(new string('b', 2));
            var secondUncached = pool.GetOrAdd(new string('b', 2));

            Assert.AreSame(first, pool.GetOrAdd(new string('a', 2)));
            Assert.AreNotSame(uncached, secondUncached);
        }
    }
}
