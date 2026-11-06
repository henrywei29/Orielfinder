using System.IO.Compression;
using System.Text;
using OrielFolder.Helpers;
using OrielFolder.Models;
using OrielFolder.Services;
using OrielFolder.Services.Archive;

namespace OrielFolder.Tests.Integration;

[TestClass]
public class ZipLegacyReaderStagingTests
{
    private const string KoreanName = "한글파일.txt";
    private const string Content = "legacy filename, intact content";
    private string _fixtureFolder = null!;

    [TestInitialize]
    public void Setup()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _fixtureFolder = Path.Combine(@"D:\codex\zip-encoding-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixtureFolder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_fixtureFolder)) Directory.Delete(_fixtureFolder, true);
    }

    [TestMethod]
    public async Task Reader_Explicit949_ReadsKoreanListingSummaryAndContent()
    {
        var zip = CreateArchive("korean.zip", KoreanName, 949);
        var settings = new SettingsServiceStub();
        settings.Set("ZipLegacyCodePage", 949);
        var reader = new ArchiveReaderService(settings);

        Assert.AreEqual(KoreanName, (await reader.GetChildrenAsync(zip, "")).Single().Name);
        Assert.AreEqual(KoreanName, (await reader.GetArchiveInfoAsync(zip)).TopEntries.Single().Name);
        using var stream = await reader.OpenEntryAsync(zip, KoreanName);
        using var text = new StreamReader(stream);
        Assert.AreEqual(Content, await text.ReadToEndAsync());
    }

    [TestMethod]
    public async Task Reader_ChangedSetting_ReinterpretsSameNameBytesOnNextRead()
    {
        var chineseName = Encoding.GetEncoding(936).GetString(Encoding.GetEncoding(949).GetBytes(KoreanName));
        Assert.AreNotEqual(KoreanName, chineseName);
        var zip = CreateArchive("chinese.zip", chineseName, 936);
        var settings = new SettingsServiceStub();
        settings.Set("ZipLegacyCodePage", 949);
        var reader = new ArchiveReaderService(settings);

        Assert.AreEqual(KoreanName, (await reader.GetChildrenAsync(zip, "")).Single().Name);
        settings.Set("ZipLegacyCodePage", 936);
        Assert.AreEqual(chineseName, (await reader.GetChildrenAsync(zip, "")).Single().Name);
        Assert.AreEqual(chineseName, (await reader.GetArchiveInfoAsync(zip)).TopEntries.Single().Name);
        using var stream = await reader.OpenEntryAsync(zip, chineseName);
        using var text = new StreamReader(stream);
        Assert.AreEqual(Content, await text.ReadToEndAsync());
    }

    [TestMethod]
    public async Task Materialize_Explicit949And936_KeepChosenNameAndContent()
    {
        var zip = CreateArchive("legacy.zip", KoreanName, 949);
        var chineseName = Encoding.GetEncoding(936).GetString(Encoding.GetEncoding(949).GetBytes(KoreanName));
        foreach (var (codePage, name) in new[] { (949, KoreanName), (936, chineseName) })
        {
            var result = await ArchiveEntryStaging.MaterializeAsync(
                [ArchivePathHelper.Combine(zip, name)], legacyCodePage: codePage);
            Assert.AreEqual(1, result.Count, $"Code page {codePage}");
            Assert.AreEqual(name, Path.GetFileName(result[0]));
            Assert.AreEqual(Content, File.ReadAllText(result[0]));
        }
    }

    [TestMethod]
    public async Task PreviewResolver_Explicit949And936_KeepChosenNameAndContent()
    {
        var zip = CreateArchive("preview.zip", KoreanName, 949);
        var chineseName = Encoding.GetEncoding(936).GetString(Encoding.GetEncoding(949).GetBytes(KoreanName));
        foreach (var (codePage, name) in new[] { (949, KoreanName), (936, chineseName) })
        {
            var staged = await ArchivePreviewResolver.ResolveAsync(PreviewType.Text,
                ArchivePathHelper.Combine(zip, name), Encoding.UTF8.GetByteCount(Content),
                CancellationToken.None, codePage);
            Assert.IsNotNull(staged, $"Code page {codePage}");
            Assert.IsTrue(Path.GetFileName(staged).EndsWith(name, StringComparison.Ordinal));
            Assert.AreEqual(Content, File.ReadAllText(staged));
        }
    }

    [TestMethod]
    public async Task PreviewCache_ChangedEncoding_UsesDifferentIdentityAndReusesWithinChoice()
    {
        var zip = CreateArchive("ascii.zip", "same.txt", 949);
        var entry = ArchivePathHelper.Combine(zip, "same.txt");
        var korean = await ArchiveEntryStaging.StageForPreviewAsync(entry, Content.Length, 1024,
            legacyCodePage: 949);
        var chinese = await ArchiveEntryStaging.StageForPreviewAsync(entry, Content.Length, 1024,
            legacyCodePage: 936);
        var koreanAgain = await ArchiveEntryStaging.StageForPreviewAsync(entry, Content.Length, 1024,
            legacyCodePage: 949);
        Assert.IsNotNull(korean);
        Assert.IsNotNull(chinese);
        Assert.AreNotEqual(korean, chinese, "Encoding choice is part of the preview cache identity.");
        Assert.AreEqual(korean, koreanAgain);
        Assert.AreEqual(Content, File.ReadAllText(chinese));
    }

    [TestMethod]
    public async Task Utf8Entries_ExplicitLegacyChoice_PreserveUnicodeNamesAndContent()
    {
        const string unicodeName = "한글中文日本語.txt";
        var zip = CreateArchive("utf8.zip", unicodeName, 65001);
        foreach (var codePage in new[] { 949, 936 })
        {
            var settings = new SettingsServiceStub();
            settings.Set("ZipLegacyCodePage", codePage);
            var reader = new ArchiveReaderService(settings);
            Assert.AreEqual(unicodeName, (await reader.GetChildrenAsync(zip, "")).Single().Name);
            var result = await ArchiveEntryStaging.MaterializeAsync(
                [ArchivePathHelper.Combine(zip, unicodeName)], legacyCodePage: codePage);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(unicodeName, Path.GetFileName(result[0]));
            Assert.AreEqual(Content, File.ReadAllText(result[0]));
        }
    }

    private string CreateArchive(string fileName, string entryName, int codePage)
    {
        var path = Path.Combine(_fixtureFolder, fileName);
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, false, Encoding.GetEncoding(codePage));
        using var writer = new StreamWriter(archive.CreateEntry(entryName).Open(), new UTF8Encoding(false));
        writer.Write(Content);
        return path;
    }

    [TestMethod]
    public async Task Reader_MixedEfsWithIdenticalRawNames_ListsAndOpensDistinctEntries()
    {
        var zip = CreateRawArchive("mixed.zip", "", mixedEfs: true);
        var settings = new SettingsServiceStub();
        settings.Set("ZipLegacyCodePage", 949);
        var reader = new ArchiveReaderService(settings);
        var children = await reader.GetChildrenAsync(zip, "");
        CollectionAssert.AreEquivalent(new[] { "징.txt", "¡.txt" }, children.Select(e => e.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "징.txt", "¡.txt" },
            (await reader.GetArchiveInfoAsync(zip)).TopEntries.Select(e => e.FullPath).ToArray());
        foreach (var (name, expected) in new[] { ("징.txt", "legacy content"), ("¡.txt", "unicode content") })
        {
            using var stream = await reader.OpenEntryAsync(zip, name);
            using var text = new StreamReader(stream);
            Assert.AreEqual(expected, await text.ReadToEndAsync(), name);
        }
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("docs/")]
    public async Task Staging_MixedEfsWithIdenticalRawNames_SelectsCorrectContent(string prefix)
    {
        var zip = CreateRawArchive("mixed-staging.zip", prefix, mixedEfs: true);
        var entries = new[] { ("징.txt", "legacy content"), ("¡.txt", "unicode content") };
        foreach (var (name, expected) in entries)
        {
            var entryPath = ArchivePathHelper.Combine(zip, prefix + name);
            var materialized = await ArchiveEntryStaging.MaterializeAsync([entryPath], legacyCodePage: 949);
            Assert.AreEqual(1, materialized.Count, name);
            Assert.AreEqual(name, Path.GetFileName(materialized[0]));
            Assert.AreEqual(expected, File.ReadAllText(materialized[0]), name);
            var preview = await ArchivePreviewResolver.ResolveAsync(PreviewType.Text, entryPath,
                expected.Length, CancellationToken.None, 949);
            Assert.IsNotNull(preview, name);
            Assert.AreEqual(expected, File.ReadAllText(preview), name);
        }
        if (prefix.Length > 0)
        {
            var folder = await ArchiveEntryStaging.MaterializeAsync(
                [ArchivePathHelper.Combine(zip, prefix.TrimEnd('/'))], legacyCodePage: 949);
            Assert.AreEqual(1, folder.Count);
            foreach (var (name, expected) in entries)
                Assert.AreEqual(expected, File.ReadAllText(Path.Combine(folder[0], name)), name);
        }
    }

    [DataTestMethod]
    [DataRow(0, "¡.txt")]
    [DataRow(949, "징.txt")]
    public async Task ReaderAndStaging_UnflaggedValidUtf8Bytes_RespectAutoAndForcedChoice(int codePage, string name)
    {
        var zip = CreateRawArchive("unflagged.zip", "", mixedEfs: false);
        var settings = new SettingsServiceStub();
        settings.Set("ZipLegacyCodePage", codePage);
        var reader = new ArchiveReaderService(settings);
        Assert.AreEqual(name, (await reader.GetChildrenAsync(zip, "")).Single().Name);
        var path = ArchivePathHelper.Combine(zip, name);
        var materialized = await ArchiveEntryStaging.MaterializeAsync([path], legacyCodePage: codePage);
        Assert.AreEqual(1, materialized.Count);
        Assert.AreEqual("legacy content", File.ReadAllText(materialized[0]));
        var preview = await ArchiveEntryStaging.StageForPreviewAsync(path, "legacy content".Length, 1024,
            legacyCodePage: codePage);
        Assert.IsNotNull(preview);
        Assert.AreEqual("legacy content", File.ReadAllText(preview));
    }

    private string CreateRawArchive(string fileName, string prefix, bool mixedEfs)
    {
        var path = Path.Combine(_fixtureFolder, fileName);
        byte[] name = Encoding.ASCII.GetBytes(prefix).Concat(new byte[] { 0xC2, 0xA1, 0x2E, 0x74, 0x78, 0x74 }).ToArray();
        var entries = mixedEfs ? new[] { (false, "legacy content"), (true, "unicode content") }
            : new[] { (false, "legacy content") };
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        var offsets = new List<uint>();
        foreach (var (efs, content) in entries)
        {
            var data = Encoding.ASCII.GetBytes(content);
            offsets.Add((uint)file.Position);
            writer.Write(0x04034b50); writer.Write((ushort)20); writer.Write((ushort)(efs ? 0x800 : 0));
            writer.Write((ushort)0); writer.Write((uint)0); writer.Write(System.IO.Hashing.Crc32.HashToUInt32(data));
            writer.Write((uint)data.Length); writer.Write((uint)data.Length);
            writer.Write((ushort)name.Length); writer.Write((ushort)0); writer.Write(name); writer.Write(data);
        }
        uint centralOffset = (uint)file.Position;
        for (int i = 0; i < entries.Length; i++)
        {
            var (efs, content) = entries[i];
            var data = Encoding.ASCII.GetBytes(content);
            writer.Write(0x02014b50); writer.Write((ushort)20); writer.Write((ushort)20);
            writer.Write((ushort)(efs ? 0x800 : 0)); writer.Write((ushort)0); writer.Write((uint)0);
            writer.Write(System.IO.Hashing.Crc32.HashToUInt32(data));
            writer.Write((uint)data.Length); writer.Write((uint)data.Length);
            writer.Write((ushort)name.Length); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((uint)0); writer.Write(offsets[i]); writer.Write(name);
        }
        uint centralSize = (uint)file.Position - centralOffset;
        writer.Write(0x06054b50); writer.Write((ushort)0); writer.Write((ushort)0);
        writer.Write((ushort)entries.Length); writer.Write((ushort)entries.Length);
        writer.Write(centralSize); writer.Write(centralOffset); writer.Write((ushort)0);
        return path;
    }
}
