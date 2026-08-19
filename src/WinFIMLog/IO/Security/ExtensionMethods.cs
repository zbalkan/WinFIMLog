using System;
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using WinFIMLog.Utils;

namespace WinFIMLog.IO.Security
{
    /// <summary>ACE and ACL related extension methods.</summary>
    public static class ExtensionMethods
    {
        /// <summary>Gets a human-readable key-value ACL for a file-system path.</summary>
        public static string GetACL(this string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            var accessControlList = OfFileSystem(new FileInfo(path));
            if (accessControlList is null)
            {
                return string.Empty;
            }

            using (accessControlList)
            {
                return AccessControlListFormatter.Format(accessControlList);
            }
        }

        /// <summary>Gets a human-readable key-value ACL for a registry key.</summary>
        public static string GetACL(this RegistryKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            using var accessControlList = OfRegistryKey(key);
            return AccessControlListFormatter.Format(accessControlList);
        }

        internal static string AccountNameOrSid(
            string identityValue,
            Func<IdentityReference> translate,
            Func<string, string?>? localLookup = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(identityValue);
            ArgumentNullException.ThrowIfNull(translate);

            var localAccount = localLookup?.Invoke(identityValue);
            if (!string.IsNullOrWhiteSpace(localAccount))
            {
                return localAccount;
            }

            try
            {
                return translate() is NTAccount account ? account.Value : identityValue;
            }
            catch (IdentityNotMappedException)
            {
                return LocalAccountOrSid(identityValue, localLookup);
            }
            catch (Win32Exception)
            {
                // Domain identities cannot always be resolved (for example, while a laptop is
                // offline or its domain trust is unavailable). Refresh the local resolver before
                // preserving the SID as the final stable evidence.
                return LocalAccountOrSid(identityValue, localLookup);
            }
        }

        private static string LocalAccountOrSid(string identityValue, Func<string, string?>? localLookup)
        {
            var localAccount = localLookup?.Invoke(identityValue);
            return string.IsNullOrWhiteSpace(localAccount) ? identityValue : localAccount;
        }

        /// <summary>
        /// Resolves an identity through the local logon-session cache first, then Windows account
        /// translation, and finally preserves the SID when no account name is available.
        /// </summary>
        internal static string AccountNameOrSid(IdentityReference identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            return AccountNameOrSid(
                identity.Value,
                () => identity.Translate(typeof(NTAccount)),
                LocalSidAccountResolver.Resolve);
        }

        private static AccessControlList? OfFileSystem(FileInfo fileInfo)
        {
            FileSecurity security;
            try
            {
                security = fileInfo.GetAccessControl();
            }
            catch (FileNotFoundException)
            {
                return null;
            }

            return Capture(security);
        }

        private static AccessControlList OfRegistryKey(RegistryKey key)
        {
            try
            {
                return Capture(key.GetAccessControl(AccessControlSections.All));
            }
            catch (Exception)
            {
                return new AccessControlList(0);
            }
        }

        private static AccessControlList Capture(FileSecurity security)
        {
            ArgumentNullException.ThrowIfNull(security);
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            var accessControlList = new AccessControlList(rules.Count)
            {
                Owner = GetSecurityIdentifierOrNull(() => security.GetOwner(typeof(SecurityIdentifier))),
                PrimaryGroupOfOwner = GetSecurityIdentifierOrNull(() => security.GetGroup(typeof(SecurityIdentifier)))
            };

            try
            {
                foreach (FileSystemAccessRule rule in rules)
                {
                    accessControlList.Add(rule.ToAce());
                }

                return accessControlList;
            }
            catch
            {
                accessControlList.Dispose();
                throw;
            }
        }

        private static AccessControlList Capture(RegistrySecurity security)
        {
            ArgumentNullException.ThrowIfNull(security);
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            var accessControlList = new AccessControlList(rules.Count)
            {
                Owner = GetSecurityIdentifierOrNull(() => security.GetOwner(typeof(SecurityIdentifier))),
                PrimaryGroupOfOwner = GetSecurityIdentifierOrNull(() => security.GetGroup(typeof(SecurityIdentifier)))
            };

            try
            {
                foreach (RegistryAccessRule rule in rules)
                {
                    accessControlList.Add(rule.ToAce());
                }

                return accessControlList;
            }
            catch
            {
                accessControlList.Dispose();
                throw;
            }
        }

        private static SecurityIdentifier? GetSecurityIdentifierOrNull(Func<IdentityReference?> getIdentity)
        {
            try
            {
                return getIdentity() as SecurityIdentifier;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static AccessControlEntry ToAce(this RegistryAccessRule rule) => new(
            ToSecurityIdentifier(rule.IdentityReference),
            unchecked((uint)rule.RegistryRights),
            rule.AccessControlType,
            rule.IsInherited,
            rule.InheritanceFlags,
            rule.PropagationFlags);

        private static AccessControlEntry ToAce(this FileSystemAccessRule rule) => new(
            ToSecurityIdentifier(rule.IdentityReference),
            unchecked((uint)rule.FileSystemRights),
            rule.AccessControlType,
            rule.IsInherited,
            rule.InheritanceFlags,
            rule.PropagationFlags);

        private static SecurityIdentifier ToSecurityIdentifier(IdentityReference identity) =>
            identity as SecurityIdentifier ?? new SecurityIdentifier(identity.Value);
    }
}
