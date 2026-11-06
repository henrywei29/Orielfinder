using System.Reflection;
using System.Xml.Linq;
using OrielFolder.Services;

namespace OrielFolder.Tests;

[TestClass]
public class DevelopmentIsolationTests
{
    private static string ProjectRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OrielFolder.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new AssertFailedException("Cannot locate development source tree.");
        }
    }

    private static XDocument Manifest => XDocument.Load(
        Path.Combine(ProjectRoot, "src", "OrielFolder", "OrielFolder", "Package.appxmanifest"));

    [TestMethod]
    public void PackageIdentityCannotReplaceOfficialPackage()
    {
        var identity = Manifest.Root!.Elements().Single(e => e.Name.LocalName == "Identity");
        Assert.AreEqual("LocalDevelopment.OrielFolderDev", (string?)identity.Attribute("Name"));
        Assert.AreEqual("CN=OrielFolder Dev", (string?)identity.Attribute("Publisher"));
        CollectionAssert.AreEqual(new[] { "OrielFolder Dev", "OrielFolder Dev" }, Manifest.Descendants()
            .Where(e => e.Name.LocalName == "DisplayName" || e.Name.LocalName == "VisualElements")
            .Select(e => e.Name.LocalName == "DisplayName" ? e.Value : (string)e.Attribute("DisplayName")!)
            .ToArray());
    }

    [TestMethod]
    public void ExecutionAliasCannotTakeOverOfficialCommand()
    {
        var aliases = Manifest.Descendants().Where(e => e.Name.LocalName == "ExecutionAlias")
            .Select(e => (string)e.Attribute("Alias")!).ToArray();
        CollectionAssert.AreEqual(new[] { "orielfolder-dev.exe" }, aliases);
        Assert.IsFalse(Manifest.Descendants().Any(e => e.Name.LocalName == "FileTypeAssociation"));
    }

    [TestMethod]
    public void MainAndWorkerUseIndependentExecutableNames()
    {
        foreach (var (folder, expected) in new[] { ("OrielFolder", "OrielFolderDev"), ("OrielFolder.Thumbs", "OrielFolderDev.Thumbs") })
        {
            var project = XDocument.Load(Path.Combine(ProjectRoot, "src", "OrielFolder", folder, folder + ".csproj"));
            Assert.AreEqual(expected, project.Descendants("AssemblyName").SingleOrDefault()?.Value);
        }
    }

    [TestMethod]
    public void RuntimeDataAndSingleInstanceIdentityAreIndependent()
    {
        var identity = typeof(ActionLogService).Assembly.GetType("OrielFolder.Helpers.AppIdentity");
        Assert.IsNotNull(identity, "Development runtime identity must be shared by storage consumers.");
        var root = (string)identity.GetProperty("LocalDataRoot", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrielFolderDev"), root);
        Assert.AreEqual("ORIEL_FOLDER_DEV_MAIN", identity.GetField("SingleInstanceKey")!.GetRawConstantValue());
        Assert.AreEqual(false, identity.GetField("AllowsDefaultAssociations")!.GetRawConstantValue());
    }

    [TestMethod]
    public void DevelopmentBuildCannotEnableDefaultAssociationChanges()
    {
        var policy = typeof(DefaultFileManagerService).GetProperty("CanChangeDefaultAssociations");
        Assert.IsNotNull(policy, "Default-manager operations must expose their disabled development policy.");
        Assert.AreEqual(false, policy.GetValue(null));
    }

    [TestMethod]
    public void IntegrationFixturesUseDisposableTemporaryRoot()
    {
        StringAssert.StartsWith(TestFixtureHelper.TestRoot,
            Path.Combine(Path.GetTempPath(), "OrielFolderFixtures_"));
    }

    [TestMethod]
    [DoNotParallelize]
    public void ArchiveCleanupPreservesOfficialVersionsTemporaryFiles()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "OrielFolderIsolation_" + Guid.NewGuid().ToString("N"));
        var official = Path.Combine(fixture, "OrielFolderArchive_disposable_official");
        var development = Path.Combine(fixture, "OrielFolderDevArchive_disposable_development");
        var oldTemp = Environment.GetEnvironmentVariable("TEMP");
        var oldTmp = Environment.GetEnvironmentVariable("TMP");
        try
        {
            Directory.CreateDirectory(official);
            Directory.CreateDirectory(development);
            File.WriteAllText(Path.Combine(official, "sentinel.txt"), "disposable official-version stand-in");
            File.WriteAllText(Path.Combine(development, "sentinel.txt"), "disposable development-version stand-in");
            Directory.SetCreationTimeUtc(official, DateTime.UtcNow.AddHours(-2));
            Directory.SetCreationTimeUtc(development, DateTime.UtcNow.AddHours(-2));
            Environment.SetEnvironmentVariable("TEMP", fixture);
            Environment.SetEnvironmentVariable("TMP", fixture);
            Assert.AreEqual(fixture, Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                "Cleanup may run only inside this disposable fixture.");

            typeof(OrielFolder.Services.Archive.ArchiveEntryStaging)
                .GetMethod("CleanupStaleStagingFolders", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);

            Assert.IsTrue(File.Exists(Path.Combine(official, "sentinel.txt")), "Development cleanup must preserve official-version files.");
            Assert.IsFalse(Directory.Exists(development), "Development cleanup should still clean its own stale files.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMP", oldTemp);
            Environment.SetEnvironmentVariable("TMP", oldTmp);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
        }
    }
}
