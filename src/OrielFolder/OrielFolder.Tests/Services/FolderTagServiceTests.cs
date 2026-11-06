using OrielFolder.Models;
using OrielFolder.Services;

namespace OrielFolder.Tests.Services;

[TestClass]
public class FolderTagServiceTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        // Keep disposable evidence on D:; no cleanup deletes user directories.
        _root = Path.Combine(@"D:\codex\Orielfolder-development\folder-tag-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [TestMethod]
    public void OrdinaryFiles_CanBeTaggedReloadedAndCleared_WithoutModifyingFile()
    {
        var file = Path.Combine(Folder("files"), "example.pdf");
        File.WriteAllText(file, "disposable test document");
        var bytes = File.ReadAllBytes(file);
        var modified = File.GetLastWriteTimeUtc(file);
        var service = Service();
        Assert.IsTrue(FolderTagService.IsTaggable(file));
        Assert.IsNull(service.SetTag(file, FolderTagColor.Blue));
        Assert.AreEqual(FolderTagColor.Blue, Service().GetTag(file));
        CollectionAssert.Contains(Service().GetTaggedPaths(FolderTagColor.Blue).ToArray(), file);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
        Assert.AreEqual(modified, File.GetLastWriteTimeUtc(file));
        Assert.IsNull(service.SetTag(file, FolderTagColor.None));
        Assert.AreEqual(FolderTagColor.None, Service().GetTag(file));
        Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(file)!).Length);
    }

    [TestMethod]
    public void SetTag_DoesNotCreateAttachedFilesOrChangeFolderAttributes()
    {
        var folder = Folder("target");
        var before = File.GetAttributes(folder);
        var service = Service();
        Assert.IsNull(service.SetTag(folder, FolderTagColor.Red));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(folder).Length,
            "Folder tags must not create desktop.ini or any attached file.");
        Assert.AreEqual(before, File.GetAttributes(folder));
    }

    private FolderTagService Service() => new(Path.Combine(_root, "app-data"));

    [TestMethod]
    public void SetAndClear_PreserveExistingIniBytesAndAttributes_AndSuppressLegacyAfterReload()
    {
        var folder = Folder("legacy");
        var ini = Path.Combine(folder, "desktop.ini");
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(
            System.Text.Encoding.UTF8.GetBytes("[.ShellClassInfo]\r\nIconResource=custom.ico,0\r\n[Orielfolder]\r\nTag=Blue\r\n")).ToArray();
        File.WriteAllBytes(ini, bytes);
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
        var attrs = File.GetAttributes(ini);
        var folderAttrs = File.GetAttributes(folder);
        var service = Service();
        Assert.AreEqual(FolderTagColor.Blue, service.GetTag(folder));
        Assert.IsNull(service.SetTag(folder, FolderTagColor.Green));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(ini));
        Assert.AreEqual(attrs, File.GetAttributes(ini));
        Assert.IsNull(service.SetTag(folder, FolderTagColor.None));
        Assert.AreEqual(FolderTagColor.None, Service().GetTag(folder));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(ini));
        Assert.AreEqual(attrs, File.GetAttributes(ini));
        Assert.AreEqual(folderAttrs, File.GetAttributes(folder));
    }

    [TestMethod]
    public void ReloadAndNormalizedPaths_KeepOneColor_AndCopyDoesNotInherit()
    {
        var folder = Folder("source");
        File.WriteAllText(Path.Combine(folder, "content.txt"), "original");
        var service = Service();
        Assert.IsNull(service.SetTag(folder + Path.DirectorySeparatorChar, FolderTagColor.Red));
        Assert.IsNull(service.SetTag(folder.ToUpperInvariant(), FolderTagColor.Purple));
        var copy = Folder("copied");
        File.Copy(Path.Combine(folder, "content.txt"), Path.Combine(copy, "content.txt"));
        var reloaded = Service();
        Assert.AreEqual(FolderTagColor.Purple, reloaded.GetTag(Path.Combine(folder, ".")));
        Assert.AreEqual(FolderTagColor.None, reloaded.GetTag(copy));
        Assert.AreEqual(1, reloaded.GetTaggedPaths(FolderTagColor.Purple).Count);
        Assert.AreEqual(0, reloaded.GetTaggedPaths(FolderTagColor.Red).Count);
    }

    [TestMethod]
    public void InvalidPathsAndColors_DoNotWriteStore()
    {
        var service = Service();
        Assert.IsNotNull(service.SetTag(Folder("valid"), (FolderTagColor)99));
        Assert.IsNotNull(service.SetTag(Path.Combine(_root, "missing"), FolderTagColor.Red));
        Assert.IsNotNull(service.SetTag("sftp://host/folder", FolderTagColor.Red));
        Assert.IsNotNull(service.SetTag(@"\not-absolute", FolderTagColor.Red));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "app-data", "folder-tags.json")));
    }

    [DataTestMethod]
    [DataRow("{broken")]
    [DataRow("{\"Version\":2,\"Tags\":{},\"Names\":{}}")]
    [DataRow("{\"Version\":1,\"Tags\":{},\"Names\":{},\"Unknown\":true}")]
    [DataRow("{\"Version\":1,\"Tags\":{},\"Names\":{\"Red\":\"bad\\nname\"}}")]
    [DataRow("{\"Version\":1,\"Tags\":{},\"Names\":{\"Unknown\":\"name\"}}")]
    public void CorruptOrUnknownStore_RefusesOverwriteAndPreservesOriginal(string json)
    {
        var data = Folder("app-data");
        var store = Path.Combine(data, "folder-tags.json");
        File.WriteAllText(store, json);
        var before = File.ReadAllBytes(store);
        var service = Service();
        Assert.IsNotNull(service.SetTag(Folder("target"), FolderTagColor.Red));
        Assert.IsNotNull(service.SetName(FolderTagColor.Red, "Work"));
        Assert.AreEqual(FolderTagColor.None, service.GetTag(Path.Combine(_root, "target")));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(store));
    }

    [TestMethod]
    public void SaveFailure_DoesNotChangeCachedTagOrName_OrRaiseChanged()
    {
        var folder = Folder("target");
        var service = Service();
        Assert.IsNull(service.SetTag(folder, FolderTagColor.Red));
        Assert.IsNull(service.SetName(FolderTagColor.Red, "Original"));
        var changed = 0;
        service.Changed += () => changed++;
        var store = Path.Combine(_root, "app-data", "folder-tags.json");
        using var locked = new FileStream(store, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.IsNotNull(service.SetTag(folder, FolderTagColor.Blue));
        Assert.IsNotNull(service.SetName(FolderTagColor.Red, "Changed"));
        Assert.AreEqual(FolderTagColor.Red, service.GetCachedTag(folder));
        Assert.AreEqual("Original", service.GetName(FolderTagColor.Red));
        Assert.AreEqual(0, changed);
    }

    [TestMethod]
    public void MovePath_UpdatesParentAndDescendants_ButNotSimilarPrefixes()
    {
        var old = Folder("old");
        var child = Folder(@"old\child");
        var sibling = Folder("old-other");
        var destination = Path.Combine(_root, "renamed");
        var service = Service();
        Assert.IsNull(service.SetTag(old, FolderTagColor.Red));
        Assert.IsNull(service.SetTag(child, FolderTagColor.Blue));
        Assert.IsNull(service.SetTag(sibling, FolderTagColor.Green));
        Directory.Move(old, destination);
        Assert.IsNull(service.MovePath(old, destination));
        var reloaded = Service();
        Assert.AreEqual(FolderTagColor.Red, reloaded.GetTag(destination));
        Assert.AreEqual(FolderTagColor.Blue, reloaded.GetTag(Path.Combine(destination, "child")));
        Assert.AreEqual(FolderTagColor.None, reloaded.GetTag(old));
        Assert.AreEqual(FolderTagColor.Green, reloaded.GetTag(sibling));
    }

    [TestMethod]
    public void Names_TrimPersistReset_ValidateAndNotify()
    {
        var service = Service();
        var changed = 0;
        service.Changed += () => changed++;
        Assert.AreEqual("", service.GetName(FolderTagColor.Red));
        Assert.IsNull(service.SetName(FolderTagColor.Red, "  工作 Work  "));
        Assert.AreEqual("工作 Work", Service().GetName(FolderTagColor.Red));
        Assert.IsNotNull(service.SetName(FolderTagColor.Red, new string('x', 41)));
        Assert.IsNotNull(service.SetName(FolderTagColor.Red, "a\nb"));
        Assert.IsNotNull(service.SetName(FolderTagColor.None, "None"));
        Assert.IsNotNull(service.SetName((FolderTagColor)99, "Unknown"));
        Assert.IsNull(service.SetName(FolderTagColor.Red, "  "));
        Assert.AreEqual("", Service().GetName(FolderTagColor.Red));
        Assert.AreEqual(2, changed);
    }

    [TestMethod]
    public void ChangedAndSnapshots_ReflectSuccessfulWritesOnly()
    {
        var folder = Folder("target");
        var service = Service();
        var changes = 0;
        service.Changed += () => changes++;
        Assert.IsNull(service.SetTag(folder, FolderTagColor.Yellow));
        var snapshot = service.GetTaggedPaths(FolderTagColor.Yellow);
        Assert.IsNull(service.SetTag(folder, FolderTagColor.None));
        Assert.AreEqual(1, snapshot.Count);
        Assert.AreEqual(0, service.GetTaggedPaths(FolderTagColor.Yellow).Count);
        Assert.AreEqual(2, changes);
    }

    [TestMethod]
    public void CorruptionAfterLoad_IsPreservedWithoutChangingKnownValues()
    {
        var folder = Folder("target");
        var service = Service();
        Assert.IsNull(service.SetTag(folder, FolderTagColor.Red));
        var store = Path.Combine(_root, "app-data", "folder-tags.json");
        File.WriteAllText(store, "{damaged externally");
        var before = File.ReadAllBytes(store);
        Assert.IsNotNull(service.SetTag(folder, FolderTagColor.Blue));
        Assert.AreEqual(FolderTagColor.Red, service.GetCachedTag(folder));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(store));
    }

    [TestMethod]
    public void ParallelWrites_PersistAllIndependentFolders()
    {
        var service = Service();
        var folders = Enumerable.Range(0, 12).Select(i => Folder("folder-" + i)).ToArray();
        Parallel.ForEach(folders, folder => Assert.IsNull(service.SetTag(folder, FolderTagColor.Orange)));
        Assert.AreEqual(12, Service().GetTaggedPaths(FolderTagColor.Orange).Count);
        foreach (var folder in folders) Assert.AreEqual(0, Directory.GetFileSystemEntries(folder).Length);
    }

    [TestMethod]
    public void InvalidStoredColor_IsRejectedWithoutOverwriting()
    {
        var folder = Folder("target");
        var service = Service();
        Assert.IsNull(service.SetTag(folder, FolderTagColor.Red));
        var store = Path.Combine(_root, "app-data", "folder-tags.json");
        File.WriteAllText(store, File.ReadAllText(store).Replace("\"Red\"", "\"99\""));
        var before = File.ReadAllBytes(store);
        Assert.IsNotNull(Service().SetTag(folder, FolderTagColor.Blue));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(store));
    }

    [TestMethod]
    public void ThrowingChangedObserver_DoesNotTurnCommittedWriteIntoFailure_OrBlockOthers()
    {
        var folder = Folder("target");
        var service = Service();
        var observed = 0;
        service.Changed += () => throw new InvalidOperationException("observer failure");
        service.Changed += () => observed++;
        Assert.IsNull(service.SetTag(folder, FolderTagColor.Blue));
        Assert.AreEqual(FolderTagColor.Blue, Service().GetTag(folder));
        Assert.AreEqual(1, observed);
    }

    [TestMethod]
    public void MovePath_WithoutTagsOrWithOnlyCachedNone_DoesNotWriteOrNotify()
    {
        var source = Folder("source");
        var destination = Folder("destination");
        var service = Service();
        var changed = 0;
        service.Changed += () => changed++;
        Assert.IsNull(service.MovePath(source, destination));
        Assert.AreEqual(FolderTagColor.None, service.GetTag(source));
        Assert.IsNull(service.MovePath(source, destination));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "app-data", "folder-tags.json")));
        Assert.AreEqual(0, changed);
    }

    [TestMethod]
    public void MovePath_DriveRootIsRejectedWithoutChangingMappings()
    {
        var root = Path.GetPathRoot(_root)!;
        var destination = Folder("destination");
        var service = Service();
        Assert.IsNull(service.SetTag(root, FolderTagColor.Gray));
        Assert.IsNotNull(service.MovePath(root, destination));
        Assert.AreEqual(FolderTagColor.Gray, service.GetTag(root));
        Assert.AreEqual(FolderTagColor.None, service.GetTag(destination));
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SuccessfulSave_PreservesUnrelatedCachedLegacyColors(bool changeName)
    {
        var legacy = Folder("legacy");
        File.WriteAllText(Path.Combine(legacy, "desktop.ini"), "[Orielfolder]\nTag=Blue\n");
        var service = Service();
        Assert.AreEqual(FolderTagColor.Blue, service.GetTag(legacy));
        Assert.IsNull(changeName
            ? service.SetName(FolderTagColor.Blue, "Work")
            : service.SetTag(Folder("unrelated"), FolderTagColor.Red));
        Assert.AreEqual(FolderTagColor.Blue, service.GetCachedTag(legacy));
    }

    [TestMethod]
    public void MovePath_InvalidatesOnlyMovedLegacyCache_PreservingOtherRealizedRows()
    {
        var source = Folder("source");
        var child = Folder(@"source\child");
        var unrelated = Folder("unrelated");
        foreach (var folder in new[] { source, child, unrelated })
            File.WriteAllText(Path.Combine(folder, "desktop.ini"), "[Orielfolder]\nTag=Purple\n");
        var service = Service();
        foreach (var folder in new[] { source, child, unrelated })
            Assert.AreEqual(FolderTagColor.Purple, service.GetTag(folder));
        var destination = Path.Combine(_root, "destination");
        Directory.Move(source, destination);
        Assert.IsNull(service.MovePath(source, destination));
        Assert.IsNull(service.GetCachedTag(source));
        Assert.IsNull(service.GetCachedTag(child));
        Assert.AreEqual(FolderTagColor.Purple, service.GetCachedTag(unrelated));
        Assert.AreEqual(FolderTagColor.Purple, service.GetCachedTag(destination));
        Assert.AreEqual(FolderTagColor.Purple, service.GetCachedTag(Path.Combine(destination, "child")));
    }
}
