using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Utils;

namespace WinFIMLog.Tests;

/// <summary>
/// Win32's FILE_ID_DESCRIPTOR is 24 bytes: a 4-byte dwSize, a 4-byte Type discriminator, then a
/// 16-byte union of LARGE_INTEGER/GUID/FILE_ID_128. An earlier version of this struct modeled 16
/// bytes with no discriminator at all, which OpenFileById would not recognize as any layout it
/// reads (ADR-0021). These offsets are asserted directly, the same way
/// UsnRecordParserTests pins USN_RECORD_V2, so a future edit can't silently reintroduce a mismatch
/// that only a real Windows run would catch.
/// </summary>
[TestClass]
public sealed class FileIdDescriptorTests
{
    [TestMethod]
    public void Struct_size_matches_the_win32_layout()
    {
        Assert.AreEqual(24, Marshal.SizeOf<NativeMethods.FileIdDescriptor>());
    }

    [TestMethod]
    public void Field_offsets_match_the_win32_layout()
    {
        Assert.AreEqual(0, (int)Marshal.OffsetOf<NativeMethods.FileIdDescriptor>(nameof(NativeMethods.FileIdDescriptor.Size)));
        Assert.AreEqual(4, (int)Marshal.OffsetOf<NativeMethods.FileIdDescriptor>(nameof(NativeMethods.FileIdDescriptor.Type)));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<NativeMethods.FileIdDescriptor>(nameof(NativeMethods.FileIdDescriptor.FileIdValue)));
    }

    [TestMethod]
    public void A_64_bit_file_reference_round_trips_through_the_signed_field_unchanged()
    {
        // FileReferenceNumber (ulong) is reinterpreted into a signed LARGE_INTEGER-shaped field.
        // The bit pattern must survive that, including references whose top bit is set.
        var fileRef = 0xFFFF_8000_0000_1234UL;

        var descriptor = new NativeMethods.FileIdDescriptor
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.FileIdDescriptor>(),
            Type = NativeMethods.FileId,
            FileIdValue = unchecked((long)fileRef)
        };

        Assert.AreEqual(fileRef, unchecked((ulong)descriptor.FileIdValue));
    }

    [TestMethod]
    public void FileId_type_constant_selects_the_plain_64_bit_union_member()
    {
        // Type = 0 is FileIdType in FILE_ID_TYPE. This codebase never sets ObjectIdType (1) or
        // ExtendedFileIdType (2); ADR-0021 records why Object IDs are not used for identity.
        Assert.AreEqual(0u, NativeMethods.FileId);
    }
}
