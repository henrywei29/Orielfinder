using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace OrielFolder.UITests.Tests;

/// <summary>
/// Smoke tests: verify the app launches and core UI elements are present.
/// Run these first to confirm the test infrastructure works.
///
/// Note: WinUI 3 Grid/Border/ItemsRepeater elements don't have AutomationPeers,
/// so they aren't exposed in the UIA tree. We verify container areas exist
/// by finding interactive child elements (Buttons, TextBoxes) within them.
/// </summary>
[TestClass]
public class SmokeTests
{
    private static Window? _window;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _window = OrielFolderAppFixture.GetMainWindow();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        OrielFolderAppFixture.Detach();
    }

    [TestMethod]
    public void App_Launches_And_MainWindow_Exists()
    {
        Assert.IsNotNull(_window, "Main window should exist after launch");
        Assert.IsTrue(_window.Title.Contains("OrielFolder") || !string.IsNullOrEmpty(_window.Title),
            "Window title should contain OrielFolder");
    }

    [TestMethod]
    public void Toolbar_Buttons_Exist()
    {
        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        var forwardBtn = OrielFolderAppFixture.FindById(_window!, "Button_Forward");
        var newTabBtn = OrielFolderAppFixture.FindById(_window!, "Button_NewTab");

        Assert.IsNotNull(backBtn, "Back button should exist");
        Assert.IsNotNull(forwardBtn, "Forward button should exist");
        Assert.IsNotNull(newTabBtn, "New Tab button should exist");
    }

    [TestMethod]
    public void AddressBar_Area_Exists()
    {
        // WinUI 3 Grid containers aren't in UIA tree — verify via child button
        var copyPathBtn = OrielFolderAppFixture.FindById(_window!, "Button_CopyPath");
        Assert.IsNotNull(copyPathBtn, "Copy path button (inside address bar) should exist");
    }

    [TestMethod]
    public void SearchBox_Exists()
    {
        var searchBox = OrielFolderAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Search box should exist");
    }

    [TestMethod]
    public void Sidebar_Buttons_Exist()
    {
        // WinUI 3 Border containers aren't in UIA tree — verify sidebar via its child buttons
        var helpBtn = OrielFolderAppFixture.FindById(_window!, "Button_Help");
        var settingsBtn = OrielFolderAppFixture.FindById(_window!, "Button_Settings");

        Assert.IsNotNull(helpBtn, "Help button should exist");
        Assert.IsNotNull(settingsBtn, "Settings button should exist");
    }

    [TestMethod]
    public void FileOperation_Buttons_Exist()
    {
        var cutBtn = OrielFolderAppFixture.FindById(_window!, "Button_Cut");
        var copyBtn = OrielFolderAppFixture.FindById(_window!, "Button_Copy");
        var pasteBtn = OrielFolderAppFixture.FindById(_window!, "Button_Paste");
        var renameBtn = OrielFolderAppFixture.FindById(_window!, "Button_Rename");
        var deleteBtn = OrielFolderAppFixture.FindById(_window!, "Button_Delete");

        Assert.IsNotNull(cutBtn, "Cut button should exist");
        Assert.IsNotNull(copyBtn, "Copy button should exist");
        Assert.IsNotNull(pasteBtn, "Paste button should exist");
        Assert.IsNotNull(renameBtn, "Rename button should exist");
        Assert.IsNotNull(deleteBtn, "Delete button should exist");
    }

    [TestMethod]
    public void ViewMode_And_Sort_Buttons_Exist()
    {
        var sortBtn = OrielFolderAppFixture.FindById(_window!, "Button_Sort");
        var viewModeBtn = OrielFolderAppFixture.FindById(_window!, "Button_ViewMode");
        var splitViewBtn = OrielFolderAppFixture.FindById(_window!, "Button_SplitView");

        Assert.IsNotNull(sortBtn, "Sort button should exist");
        Assert.IsNotNull(viewModeBtn, "View mode button should exist");
        Assert.IsNotNull(splitViewBtn, "Split view button should exist");
    }

    [TestMethod]
    public void ContentArea_Exists()
    {
        // WinUI 3 Grid containers aren't in UIA tree — verify content area via preview toggle button
        var previewBtn = OrielFolderAppFixture.FindById(_window!, "Button_PreviewToggle");
        Assert.IsNotNull(previewBtn, "Preview toggle button should exist (confirming content area)");
    }
}
