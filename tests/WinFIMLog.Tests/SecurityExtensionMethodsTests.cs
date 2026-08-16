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
    }
}
