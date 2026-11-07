using System.Collections.Generic;
using System.IO;
using OrielFolder.Helpers;

namespace OrielFolder.Tests.Helpers;

/// <summary>
/// 详细信息视图打开 zip / 工具栏解压按钮用到的纯逻辑：
/// - IsExtractableArchiveFile：工具栏“解压缩”按钮何时可用
/// - GetUniqueExtractDestination：解压到同名文件夹时的重名处理
/// - GetParentPath：在压缩包内按“向上”时的目标位置（旧实现在压缩包里“向上”没有反应）
/// 路径都用不存在的路径，不触碰真实文件系统。
/// </summary>
[TestClass]
public class ArchiveExtractAndNavigationTests
{
    private const string Dir = @"Z:\__oriel_test_nonexistent__\docs";

    // ── IsExtractableArchiveFile ────────────────────────────────

    [DataTestMethod]
    [DataRow(Dir + @"\a.zip")]
    [DataRow(Dir + @"\A.ZIP")]
    [DataRow(Dir + @"\a.7z")]
    [DataRow(Dir + @"\backup.tar.gz")]
    [DataRow(@"\\server\share\a.zip")]
    public void IsExtractableArchiveFile_LocalArchive_True(string path)
    {
        Assert.IsTrue(ArchivePathHelper.IsExtractableArchiveFile(path), path);
    }

    [DataTestMethod]
    [DataRow(Dir + @"\a.txt")]
    [DataRow(Dir + @"\zip")]
    [DataRow(Dir + @"\a.zip.txt")]
    [DataRow(@"archive://" + Dir + @"\outer.zip/inner.zip")]   // entry inside an opened archive
    [DataRow(@"ftp://host/a.zip")]                               // remote
    [DataRow("")]
    [DataRow(null)]
    public void IsExtractableArchiveFile_NotExtractable_False(string? path)
    {
        Assert.IsFalse(ArchivePathHelper.IsExtractableArchiveFile(path!), path ?? "<null>");
    }

    // ── GetUniqueExtractDestination ─────────────────────────────

    [TestMethod]
    public void GetUniqueExtractDestination_NoConflict_UsesArchiveBaseName()
    {
        var dest = ArchivePathHelper.GetUniqueExtractDestination(Dir, Path.Combine(Dir, "photos.zip"), _ => false);
        Assert.AreEqual(Path.Combine(Dir, "photos"), dest);
    }

    [TestMethod]
    public void GetUniqueExtractDestination_CompoundExtension_StripsWholeExtension()
    {
        var dest = ArchivePathHelper.GetUniqueExtractDestination(Dir, Path.Combine(Dir, "backup.tar.gz"), _ => false);
        Assert.AreEqual(Path.Combine(Dir, "backup"), dest);
    }

    [TestMethod]
    public void GetUniqueExtractDestination_NameTaken_AppendsCounter()
    {
        var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Dir, "photos"),
            Path.Combine(Dir, "photos (1)"),
        };
        var dest = ArchivePathHelper.GetUniqueExtractDestination(Dir, Path.Combine(Dir, "photos.zip"), taken.Contains);
        Assert.AreEqual(Path.Combine(Dir, "photos (2)"), dest);
    }

    [TestMethod]
    public void GetUniqueExtractDestination_DefaultExistsCheck_NonexistentParent_NoCounter()
    {
        var dest = ArchivePathHelper.GetUniqueExtractDestination(Dir, Path.Combine(Dir, "photos.zip"));
        Assert.AreEqual(Path.Combine(Dir, "photos"), dest);
    }

    // ── GetParentPath ───────────────────────────────────────────

    [TestMethod]
    public void GetParentPath_ArchiveRoot_ReturnsFolderHoldingArchive()
    {
        var root = ArchivePathHelper.Combine(Path.Combine(Dir, "a.zip"), "");
        Assert.AreEqual(Dir, ArchivePathHelper.GetParentPath(root));
    }

    [TestMethod]
    public void GetParentPath_TopLevelFolderInArchive_ReturnsArchiveRoot()
    {
        var zip = Path.Combine(Dir, "a.zip");
        var inner = ArchivePathHelper.Combine(zip, "src");
        Assert.AreEqual(ArchivePathHelper.Combine(zip, ""), ArchivePathHelper.GetParentPath(inner));
    }

    [TestMethod]
    public void GetParentPath_NestedFolderInArchive_ReturnsParentFolderInArchive()
    {
        var zip = Path.Combine(Dir, "a.zip");
        var inner = ArchivePathHelper.Combine(zip, "src/main/");
        Assert.AreEqual(ArchivePathHelper.Combine(zip, "src"), ArchivePathHelper.GetParentPath(inner));
    }

    [TestMethod]
    public void GetParentPath_NotArchivePath_ReturnsNull()
    {
        Assert.IsNull(ArchivePathHelper.GetParentPath(Dir));
    }
}
