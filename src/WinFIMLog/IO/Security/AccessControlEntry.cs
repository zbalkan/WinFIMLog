using System.Security.AccessControl;
using System.Security.Principal;

namespace WinFIMLog.IO.Security
{
    /// <summary>
    /// A compact typed view of a file-system or registry access control entry.
    /// The SID and rights remain typed until the final text record is rendered.
    /// </summary>
    public readonly record struct AccessControlEntry(
        SecurityIdentifier Identity,
        uint Rights,
        AccessControlType Type,
        bool IsInherited,
        InheritanceFlags InheritanceFlags,
        PropagationFlags PropagationFlags);
}
