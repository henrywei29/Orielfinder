using OrielFolder.Services.DevUninstall;

namespace OrielFolder.Tests.Services;

[TestClass]
public class StaleFolderOpenRuleTests
{
    private const string Alias = @"C:\Users\test\AppData\Local\Microsoft\WindowsApps\spanfinder.exe";
    private static readonly string Command = "\"" + Alias + "\" \"%1\"";
    private static readonly string[] Names = { "", "DelegateExecute" };
    private static bool Missing(string _) => false;
    private static bool Exists(string _) => true;

    [TestMethod]
    public void ExactCommandToMissingExeIsStale() =>
        Assert.IsTrue(StaleFolderOpenRule.IsStale(Command, "", Names, Array.Empty<string>(), Missing));

    [TestMethod]
    public void WithoutDelegateValueIsStillStale() =>
        Assert.IsTrue(StaleFolderOpenRule.IsStale(Command, null, new[] { "" }, Array.Empty<string>(), Missing));

    [TestMethod]
    public void PlaceholderCaseAndPathCaseAreIgnored() =>
        Assert.IsTrue(StaleFolderOpenRule.IsStale(Command.ToUpperInvariant(), "", Names, Array.Empty<string>(), Missing));

    [TestMethod]
    public void ExistingFileManagerIsPreserved() =>
        Assert.IsFalse(StaleFolderOpenRule.IsStale(Command, "", Names, Array.Empty<string>(), Exists));

    [TestMethod]
    public void DelegateHandlerIsPreserved() =>
        Assert.IsFalse(StaleFolderOpenRule.IsStale(Command, "{11dbb47c-a525-400b-9e80-a54615a090c0}", Names, Array.Empty<string>(), Missing));

    [TestMethod]
    public void ExtraValuesArePreserved() =>
        Assert.IsFalse(StaleFolderOpenRule.IsStale(Command, "", new[] { "", "DelegateExecute", "Extra" }, Array.Empty<string>(), Missing));

    [TestMethod]
    public void SubkeysArePreserved() =>
        Assert.IsFalse(StaleFolderOpenRule.IsStale(Command, "", Names, new[] { "Child" }, Missing));

    [TestMethod]
    public void MissingCommandIsNoOp() =>
        Assert.IsFalse(StaleFolderOpenRule.IsStale(null, null, Array.Empty<string>(), Array.Empty<string>(), Missing));

    [TestMethod]
    public void OtherCommandShapesArePreserved()
    {
        Assert.IsFalse(StaleFolderOpenRule.IsStale(Alias + " %1", "", Names, Array.Empty<string>(), Missing));
        Assert.IsFalse(StaleFolderOpenRule.IsStale("\"" + Alias + "\" \"%1\" --extra", "", Names, Array.Empty<string>(), Missing));
        Assert.IsFalse(StaleFolderOpenRule.IsStale("\"relative.exe\" \"%1\"", "", Names, Array.Empty<string>(), Missing));
    }

    [TestMethod]
    public void TargetIsParsedFromQuotedPathWithSpaces() =>
        Assert.AreEqual(@"C:\Program Files\App\app.exe",
            StaleFolderOpenRule.ParseCommandTarget("\"C:\\Program Files\\App\\app.exe\" \"%1\""));

    [TestMethod]
    public void OnlyFolderDirectoryAndDriveAreInspected() =>
        CollectionAssert.AreEqual(new[] { "Folder", "Directory", "Drive" }, StaleFolderOpenRule.Classes.ToArray());
}

[TestClass]
public class DevUninstallPlanTests
{
    private const string Marked = @"D:\codex\OrielFolderDev\marked-files";
    private const string Preview = @"D:\codex\OrielFolderDev\preview-cache";
    private const string LocalData = @"C:\Users\test\AppData\Local\OrielFolderDev";
    private static readonly DateTime Now = new(2026, 11, 6, 12, 30, 45);

    private static DevUninstallPlan Create(string preview = Preview) =>
        DevUninstallPlan.Create(Marked, preview, LocalData, "OrielFolderDev", Now);

    [TestMethod]
    public void BackupLivesNextToMarkedFiles()
    {
        var plan = Create();
        Assert.AreEqual(@"D:\codex\OrielFolderDev\backups", plan.BackupDirectory);
        Assert.AreEqual(@"D:\codex\OrielFolderDev\backups\OrielFolder-config-20261106-123045.json", plan.ConfigurationBackupPath);
        Assert.AreEqual(@"D:\codex\OrielFolderDev\backups\uninstall-20261106-123045", plan.RunDirectory);
        Assert.AreEqual(@"D:\codex\OrielFolderDev\backups\uninstall-20261106-123045\registry", plan.RegistryBackupDirectory);
    }

    [TestMethod]
    public void DeletesCachesButKeepsUserFiles()
    {
        var plan = Create();
        CollectionAssert.AreEqual(new[] { LocalData, Preview }, plan.FoldersToDelete.ToArray());
        CollectionAssert.AreEqual(new[] { Marked, @"D:\codex\OrielFolderDev\backups" }, plan.ProtectedFolders.ToArray());
    }

    [TestMethod]
    public void PreviewCacheInsideLocalDataIsNotListedTwice()
    {
        var plan = Create(LocalData + @"\preview-cache");
        CollectionAssert.AreEqual(new[] { LocalData }, plan.FoldersToDelete.ToArray());
    }

    [TestMethod]
    public void AllowListAcceptsOnlyPlannedFolders()
    {
        var plan = Create();
        Assert.IsTrue(plan.IsAllowedDeletion(LocalData));
        Assert.IsTrue(plan.IsAllowedDeletion(Preview.ToUpperInvariant() + @"\"));
        Assert.IsTrue(plan.IsAllowedDeletion(@"C:\Program Files\Listary\FileAppPlugins\OrielFolderDev"));
        Assert.IsFalse(plan.IsAllowedDeletion(Marked));
        Assert.IsFalse(plan.IsAllowedDeletion(Marked + @"\a.pdf"));
        Assert.IsFalse(plan.IsAllowedDeletion(@"D:\codex\OrielFolderDev\backups"));
        Assert.IsFalse(plan.IsAllowedDeletion(@"D:\codex\OrielFolderDev"));
        Assert.IsFalse(plan.IsAllowedDeletion(@"C:\Users\test\AppData\Local"));
        Assert.IsFalse(plan.IsAllowedDeletion(@"C:\"));
        Assert.IsFalse(plan.IsAllowedDeletion(@"C:\Program Files\Listary\FileAppPlugins"));
        Assert.IsFalse(plan.IsAllowedDeletion(@"C:\Program Files\Listary\FileAppPlugins\OrielFolder"));
        Assert.IsFalse(plan.IsAllowedDeletion(@"relative\OrielFolderDev"));
    }

    [TestMethod]
    public void DocumentsFallbackWhenNoCodexDrive()
    {
        var plan = DevUninstallPlan.Create(@"C:\Users\test\Documents\OrielFolderDev\marked-files",
            LocalData + @"\preview-cache", LocalData, "OrielFolderDev", Now);
        Assert.AreEqual(@"C:\Users\test\Documents\OrielFolderDev\backups", plan.BackupDirectory);
    }

    [TestMethod]
    public void RejectsUnexpectedDataFolderName() =>
        Assert.ThrowsException<ArgumentException>(() =>
            DevUninstallPlan.Create(Marked, Preview, @"C:\Users\test\AppData\Local", "OrielFolderDev", Now));
}
