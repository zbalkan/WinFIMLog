using System;
using System.Security.AccessControl;

namespace WinFIMLog.IO.Security
{
    /// <summary>Renders a typed ACL as a human-readable key-value record without JSON serialization.</summary>
    internal static class AccessControlListFormatter
    {
        private static readonly AclStringPool AclStrings = new();

        public static string Format(AccessControlList accessControlList)
        {
            ArgumentNullException.ThrowIfNull(accessControlList);

            using var buffer = new PooledCharBuffer(256);
            buffer.Append("Owner: ");
            AppendIdentity(buffer, accessControlList.Owner);
            buffer.Append("; PrimaryGroup: ");
            AppendIdentity(buffer, accessControlList.PrimaryGroupOfOwner);
            buffer.Append("; AceCount: ");
            buffer.Append(accessControlList.Count);

            foreach (var entry in accessControlList.Entries.Span)
            {
                buffer.Append("; ACE: [Identity: ");
                AppendIdentity(buffer, entry.Identity);
                buffer.Append("; Rights: 0x");
                buffer.AppendHex(entry.Rights);
                buffer.Append("; Type: ");
                AppendAccessControlType(buffer, entry.Type);
                buffer.Append("; Inherited: ");
                buffer.Append(entry.IsInherited);
                buffer.Append("; Inheritance: ");
                AppendInheritanceFlags(buffer, entry.InheritanceFlags);
                buffer.Append("; Propagation: ");
                AppendPropagationFlags(buffer, entry.PropagationFlags);
                buffer.Append("]");
            }

            return AclStrings.GetOrAdd(buffer.ToString());
        }

        private static void AppendIdentity(PooledCharBuffer buffer, System.Security.Principal.SecurityIdentifier? identity) =>
            buffer.Append(identity is null ? "None" : ExtensionMethods.AccountNameOrSid(identity));

        private static void AppendAccessControlType(PooledCharBuffer buffer, AccessControlType type) =>
            buffer.Append(type == AccessControlType.Allow ? "Allow" : "Deny");

        private static void AppendInheritanceFlags(PooledCharBuffer buffer, InheritanceFlags flags)
        {
            if (flags == InheritanceFlags.None)
            {
                buffer.Append("None");
                return;
            }

            var hasValue = false;
            AppendFlag(buffer, flags, InheritanceFlags.ObjectInherit, "ObjectInherit", ref hasValue);
            AppendFlag(buffer, flags, InheritanceFlags.ContainerInherit, "ContainerInherit", ref hasValue);
        }

        private static void AppendPropagationFlags(PooledCharBuffer buffer, PropagationFlags flags)
        {
            if (flags == PropagationFlags.None)
            {
                buffer.Append("None");
                return;
            }

            var hasValue = false;
            AppendFlag(buffer, flags, PropagationFlags.NoPropagateInherit, "NoPropagateInherit", ref hasValue);
            AppendFlag(buffer, flags, PropagationFlags.InheritOnly, "InheritOnly", ref hasValue);
        }

        private static void AppendFlag<T>(PooledCharBuffer buffer, T flags, T flag, ReadOnlySpan<char> name, ref bool hasValue)
            where T : struct, Enum
        {
            if (!flags.HasFlag(flag))
            {
                return;
            }

            if (hasValue)
            {
                buffer.Append("|");
            }

            buffer.Append(name);
            hasValue = true;
        }
    }
}
