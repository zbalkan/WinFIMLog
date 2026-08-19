using System;
using System.Security.AccessControl;

namespace WinFIMLog.IO.Security
{
    /// <summary>Renders a typed ACL as a human-readable key-value record without JSON serialization.</summary>
    internal static class AccessControlListFormatter
    {
        private static readonly AclTextCache AclTexts = new();

        public static string Format(AccessControlList accessControlList)
        {
            ArgumentNullException.ThrowIfNull(accessControlList);

            if (AclTexts.TryGet(accessControlList, out var cachedText))
            {
                return cachedText;
            }

            Span<char> initialBuffer = stackalloc char[256];
            var buffer = new PooledCharBuffer(initialBuffer);
            try
            {
                buffer.Append("Owner: ");
                AppendIdentity(ref buffer, accessControlList.Owner);
                buffer.Append("; PrimaryGroup: ");
                AppendIdentity(ref buffer, accessControlList.PrimaryGroupOfOwner);
                buffer.Append("; AceCount: ");
                buffer.Append(accessControlList.Count);

                foreach (var entry in accessControlList.Entries.Span)
                {
                    buffer.Append("; ACE: [Identity: ");
                    AppendIdentity(ref buffer, entry.Identity);
                    buffer.Append("; Rights: 0x");
                    buffer.AppendHex(entry.Rights);
                    buffer.Append("; Type: ");
                    AppendAccessControlType(ref buffer, entry.Type);
                    buffer.Append("; Inherited: ");
                    buffer.Append(entry.IsInherited);
                    buffer.Append("; Inheritance: ");
                    AppendInheritanceFlags(ref buffer, entry.InheritanceFlags);
                    buffer.Append("; Propagation: ");
                    AppendPropagationFlags(ref buffer, entry.PropagationFlags);
                    buffer.Append("]");
                }

                return AclTexts.Add(accessControlList, buffer.ToString());
            }
            finally
            {
                buffer.Dispose();
            }
        }

        private static void AppendIdentity(ref PooledCharBuffer buffer, System.Security.Principal.SecurityIdentifier? identity) =>
            buffer.Append(identity is null ? "None" : ExtensionMethods.AccountNameOrSid(identity));

        private static void AppendAccessControlType(ref PooledCharBuffer buffer, AccessControlType type) =>
            buffer.Append(type == AccessControlType.Allow ? "Allow" : "Deny");

        private static void AppendInheritanceFlags(ref PooledCharBuffer buffer, InheritanceFlags flags)
        {
            if (flags == InheritanceFlags.None)
            {
                buffer.Append("None");
                return;
            }

            var hasValue = false;
            AppendFlag(ref buffer, flags, InheritanceFlags.ObjectInherit, "ObjectInherit", ref hasValue);
            AppendFlag(ref buffer, flags, InheritanceFlags.ContainerInherit, "ContainerInherit", ref hasValue);
        }

        private static void AppendPropagationFlags(ref PooledCharBuffer buffer, PropagationFlags flags)
        {
            if (flags == PropagationFlags.None)
            {
                buffer.Append("None");
                return;
            }

            var hasValue = false;
            AppendFlag(ref buffer, flags, PropagationFlags.NoPropagateInherit, "NoPropagateInherit", ref hasValue);
            AppendFlag(ref buffer, flags, PropagationFlags.InheritOnly, "InheritOnly", ref hasValue);
        }

        private static void AppendFlag<T>(ref PooledCharBuffer buffer, T flags, T flag, ReadOnlySpan<char> name, ref bool hasValue)
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
