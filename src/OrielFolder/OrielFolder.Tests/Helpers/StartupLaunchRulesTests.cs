using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrielFolder.Helpers;
using Windows.ApplicationModel;

namespace OrielFolder.Tests.Helpers;

[TestClass]
public class StartupLaunchRulesTests
{
    [TestMethod]
    public void Enabled_IsOnAndChangeable()
        => Assert.AreEqual((true, true, (string?)null), StartupLaunchRules.Describe(StartupTaskState.Enabled));

    [TestMethod]
    public void Disabled_IsOffAndChangeable()
        => Assert.AreEqual((false, true, (string?)null), StartupLaunchRules.Describe(StartupTaskState.Disabled));

    [TestMethod]
    public void DisabledByUser_CannotBeTurnedOnByTheApp()
        => Assert.AreEqual((false, false, "Settings_StartupDisabledByUser"), StartupLaunchRules.Describe(StartupTaskState.DisabledByUser));

    [TestMethod]
    public void PolicyStates_AreLocked()
    {
        Assert.AreEqual((false, false, "Settings_StartupDisabledByPolicy"), StartupLaunchRules.Describe(StartupTaskState.DisabledByPolicy));
        Assert.AreEqual((true, false, "Settings_StartupEnabledByPolicy"), StartupLaunchRules.Describe(StartupTaskState.EnabledByPolicy));
    }

    [TestMethod]
    public void Unavailable_IsOffLockedWithNote()
        => Assert.AreEqual((false, false, "Settings_StartupUnavailable"), StartupLaunchRules.Describe(null));

    [TestMethod]
    public void Manifest_DeclaresDisabledStartupTaskWithSameId()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "OrielFolder.sln"))) dir = dir.Parent;
        Assert.IsNotNull(dir);
        var manifest = XDocument.Load(System.IO.Path.Combine(dir!.FullName, "src", "OrielFolder", "OrielFolder", "Package.appxmanifest"));
        var task = manifest.Descendants().Single(e => e.Name.LocalName == "StartupTask");
        Assert.AreEqual(StartupLaunchRules.TaskId, (string?)task.Attribute("TaskId"));
        // Off until the user turns it on in Settings.
        Assert.AreEqual("false", (string?)task.Attribute("Enabled"));
        var extension = task.Parent!;
        Assert.AreEqual("windows.startupTask", (string?)extension.Attribute("Category"));
        // $targetnametoken$ is not expanded inside extensions: the exe name is written out and must follow
        // the project's AssemblyName (OrielFolderDev in Dev, OrielFolder in the release identity).
        var project = XDocument.Load(System.IO.Path.Combine(dir.FullName, "src", "OrielFolder", "OrielFolder", "OrielFolder.csproj"));
        Assert.AreEqual(project.Descendants("AssemblyName").Single().Value + ".exe", (string?)extension.Attribute("Executable"));
        // Task Manager shows this name; keep it equal to the app's display name.
        var visual = manifest.Descendants().Single(e => e.Name.LocalName == "VisualElements");
        Assert.AreEqual((string?)visual.Attribute("DisplayName"), (string?)task.Attribute("DisplayName"));
    }
}
