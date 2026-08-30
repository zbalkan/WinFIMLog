using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.FIM;
using WinFIMLog.USN;
using WinFIMLog.Utils;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class UsnReasonMapperTests
{
    [TestMethod]
    public void Delete_outranks_every_other_reason_on_the_same_record()
    {
        // NTFS accumulates reasons onto one record, so a deleted temporary file arrives carrying its
        // creation and write flags too. Reporting that as a create would invert the finding.
        var reason = (uint)(NativeMethods.UsnReasonFlags.FileCreate |
                            NativeMethods.UsnReasonFlags.DataExtend |
                            NativeMethods.UsnReasonFlags.FileDelete);

        Assert.AreEqual(ChangeCategory.Deleted, UsnReasonMapper.MapReasonToChangeCategory(reason));
    }

    [TestMethod]
    public void Create_outranks_data_and_security_reasons()
    {
        var reason = (uint)(NativeMethods.UsnReasonFlags.FileCreate |
                            NativeMethods.UsnReasonFlags.SecurityChange |
                            NativeMethods.UsnReasonFlags.DataOverwrite);

        Assert.AreEqual(ChangeCategory.Created, UsnReasonMapper.MapReasonToChangeCategory(reason));
    }

    [TestMethod]
    public void Rename_reports_as_a_creation_at_the_destination_name()
    {
        Assert.AreEqual(ChangeCategory.Created,
            UsnReasonMapper.MapReasonToChangeCategory((uint)NativeMethods.UsnReasonFlags.RenameNewName));
        Assert.AreEqual(ChangeCategory.Created,
            UsnReasonMapper.MapReasonToChangeCategory((uint)NativeMethods.UsnReasonFlags.RenameOldName));
    }

    [TestMethod]
    public void Security_change_alone_reports_as_a_change()
    {
        Assert.AreEqual(ChangeCategory.Changed,
            UsnReasonMapper.MapReasonToChangeCategory((uint)NativeMethods.UsnReasonFlags.SecurityChange));
    }

    [TestMethod]
    public void Data_reasons_report_as_a_change()
    {
        foreach (var flag in new[]
                 {
                     NativeMethods.UsnReasonFlags.DataOverwrite,
                     NativeMethods.UsnReasonFlags.DataExtend,
                     NativeMethods.UsnReasonFlags.DataTruncation,
                     NativeMethods.UsnReasonFlags.BasicInfoChange
                 })
        {
            Assert.AreEqual(ChangeCategory.Changed, UsnReasonMapper.MapReasonToChangeCategory((uint)flag),
                $"{flag} should map to Changed");
        }
    }

    [TestMethod]
    public void Unclassified_and_empty_reasons_still_produce_a_category()
    {
        Assert.AreEqual(ChangeCategory.Changed,
            UsnReasonMapper.MapReasonToChangeCategory((uint)NativeMethods.UsnReasonFlags.CompressionChange));
        Assert.AreEqual(ChangeCategory.Changed, UsnReasonMapper.MapReasonToChangeCategory(0));
    }

    [TestMethod]
    public void Formatted_reasons_retain_the_detail_the_category_collapses()
    {
        var reason = (uint)(NativeMethods.UsnReasonFlags.FileCreate |
                            NativeMethods.UsnReasonFlags.DataExtend |
                            NativeMethods.UsnReasonFlags.FileDelete);

        var formatted = UsnReasonMapper.FormatReasonFlags(reason);

        StringAssert.Contains(formatted, "FileCreate");
        StringAssert.Contains(formatted, "DataExtend");
        StringAssert.Contains(formatted, "FileDelete");
    }

    [TestMethod]
    public void Formatted_empty_reason_is_explicit_rather_than_blank()
    {
        Assert.AreEqual("(no flags)", UsnReasonMapper.FormatReasonFlags(0));
    }
}
