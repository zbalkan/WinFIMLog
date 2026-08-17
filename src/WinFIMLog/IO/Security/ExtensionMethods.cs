using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using WinFIMLog.Utils;

namespace WinFIMLog.IO.Security
{
    /// <summary>
    ///     ACE and ACL related extension methods
    /// </summary>
    public static class ExtensionMethods
    {
        /// <summary>
        ///     Get custom formatted ACL
        /// </summary>
        /// <param name="path">
        ///     File path
        /// </param>
        /// <returns>
        ///     Custom formatted ACL
        /// </returns>
        /// <exception cref="System.Security.SecurityException">
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// </exception>
        /// <exception cref="PathTooLongException">
        /// </exception>
        public static string GetACL(this string path) => ToJson(new AccessControlList().OfFileSystem(new FileInfo(path)));

        /// <summary>
        ///     Get custom formatted ACL
        /// </summary>
        /// <param name="key">
        ///     Registry key
        /// </param>
        /// <returns>
        ///     Custom formatted ACL
        /// </returns>
        /// <exception cref="System.Security.SecurityException">
        /// </exception>
        /// <exception cref="IdentityNotMappedException">
        /// </exception>
        /// <exception cref="SystemException">
        /// </exception>
        public static string GetACL(this RegistryKey key) => ToJson(new AccessControlList().OfRegistryKey(key));

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
                return translate() is NTAccount account
                    ? account.Value
                    : identityValue;
            }
            catch (IdentityNotMappedException ex)
            {
                Debug.WriteLine(ex);
                return identityValue;
            }
            catch (Win32Exception ex)
            {
                // Domain identities cannot always be resolved (for example, while a laptop is
                // offline or its domain trust is unavailable). The SID still identifies the
                // principal and allows ACL collection to continue without losing the value.
                Debug.WriteLine(ex);
                return identityValue;
            }
        }

        /// <summary>
        ///     Resolve an identity to its account name, falling back to its stable SID when Windows
        ///     cannot contact the account's domain or does not know the identity.
        /// </summary>
        private static string AccountNameOrSid(IdentityReference identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            return AccountNameOrSid(
                identity.Value,
                () => identity.Translate(typeof(NTAccount)),
                LocalSidAccountResolver.Resolve);
        }

        private static IEnumerable<string> ListFlags<T>(this T value) where T : struct, Enum
        {
            // Check that this is really a "flags" enum:
            if (!Attribute.IsDefined(typeof(T), typeof(FlagsAttribute)))
            {
                yield return Enum.GetName(value) ?? string.Empty;
                yield break;
            }

            foreach (var flag in Enum.GetNames<T>())
            {
                yield return flag;
            }
        }

        private static AccessControlList? OfFileSystem(this AccessControlList acl, FileInfo fileInfo)
        {
            FileSecurity fileSystemSecurity;
            try
            {
                fileSystemSecurity = fileInfo.GetAccessControl();
            }
            catch (FileNotFoundException)
            {
                return default;
            }

            acl.Owner = OwnerName(fileSystemSecurity);
            acl.PrimaryGroupOfOwner = PrimaryGroupOfOwnerName(fileSystemSecurity);
            acl.Permissions = fileSystemSecurity
                .GetAccessRules(true, true, typeof(NTAccount))
                .Cast<FileSystemAccessRule>()
                .Select(rule => rule.ToAce())
                .ToList();

            return acl;
        }

        /// <summary>
        ///     Get formatted ACL of a Registry key
        /// </summary>
        /// <param name="acl">
        ///     ACL object
        /// </param>
        /// <param name="key">
        ///     Registry key
        /// </param>
        /// <returns>
        ///     Formatted ACL
        /// </returns>
        /// <exception cref="System.Security.SecurityException">
        /// </exception>
        /// <exception cref="IdentityNotMappedException">
        /// </exception>
        /// <exception cref="SystemException">
        /// </exception>
        private static AccessControlList OfRegistryKey(this AccessControlList acl, RegistryKey key)
        {
            try
            {
                var registryPermissions = key.GetAccessControl(AccessControlSections.All);
                acl.Owner = registryPermissions.GetOwner(typeof(NTAccount))?.Value ?? string.Empty;
                acl.PrimaryGroupOfOwner = registryPermissions.GetGroup(typeof(NTAccount))?.Value ?? string.Empty;
                acl.Permissions = registryPermissions
                    .GetAccessRules(true, true, typeof(NTAccount))
                    .Cast<RegistryAccessRule>()
                    .Select(rule => rule.ToAce())
                    .ToList();
            }
            catch (Exception)
            {
                // return same acl
            }

            return acl;
        }

        /// <summary>
        ///     Translate file owner name from SID
        /// </summary>
        /// <param name="fileSecurity">
        /// </param>
        /// <returns>
        ///     Translated owner name, original SID or empty string.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// </exception>
        private static string OwnerName(FileSecurity fileSecurity)
        {
            ArgumentNullException.ThrowIfNull(fileSecurity);
            try
            {
                var sid = fileSecurity.GetOwner(typeof(SecurityIdentifier));
                if (sid == null)
                {
                    return string.Empty;
                }

                return AccountNameOrSid(sid);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return string.Empty;
            }
        }

        /// <summary>
        ///     Translate primary group name from SID
        /// </summary>
        /// <param name="fileSecurity">
        ///     FileSecurity object to parse
        /// </param>
        /// <returns>
        ///     Transled group name, original SID or empty string.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// </exception>
        private static string PrimaryGroupOfOwnerName(FileSecurity fileSecurity)
        {
            ArgumentNullException.ThrowIfNull(fileSecurity);
            try
            {
                var primaryGroup = fileSecurity.GetGroup(typeof(SecurityIdentifier));
                if (primaryGroup == null)
                {
                    return string.Empty;
                }

                return AccountNameOrSid(primaryGroup);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return string.Empty;
            }
        }

        private static AccessControlEntry ToAce(this RegistryAccessRule rule) => new()
        {
            UserOrGroup = rule.IdentityReference.Value,
            Permissions = rule.RegistryRights.ListFlags().ToList(),
            IsInherited = rule.IsInherited
        };

        private static AccessControlEntry ToAce(this FileSystemAccessRule rule) => new()
        {
            UserOrGroup = rule.IdentityReference.Value,
            Permissions = rule.FileSystemRights.ListFlags().ToList(),
            IsInherited = rule.IsInherited
        };

        private static string ToJson(AccessControlList? ac)
        {
            if (ac is null)
            {
                return string.Empty;
            }

            return JsonSerializer.Serialize(ac, AclJsonSerializerContext.Default.AccessControlList);
        }
    }
}
