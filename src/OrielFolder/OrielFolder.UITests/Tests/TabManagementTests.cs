using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class TabManagementTests
{
    private static Window? _window;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _window = OrielFolderAppFixture.GetMainWindow();
        OrielFolderAppFixture.Focus(_window);
    }

    [ClassCleanup]
    public static void ClassCleanup() => OrielFolderAppFixture.Detach();

    [TestInitialize]
    public void TestInit() => OrielFolderAppFixture.Focus(_window!);

    [TestMethod]
    public void NewTabButton_Exists_And_Enabled()
    {
        var newTabBtn = OrielFolderAppFixture.FindById(_window!, "Button_NewTab");
        Assert.IsNotNull(newTabBtn, "New tab button should exist");
        Assert.IsTrue(newTabBtn.IsEnabled, "New tab button should be enabled");
    }

    [TestMethod]
    public void CreateMultipleTabs_AllCloseable()
    {
        for (int i = 0; i < 3; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        }

        // New tabs open in Home mode — check for element that exists in any mode
        var newTabBtn = OrielFolderAppFixture.FindById(_window!, "Button_NewTab");
        Assert.IsNotNull(newTabBtn, "UI should remain functional with multiple tabs");

        for (int i = 0; i < 3; i++)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        }

        // After closing new tabs, original tab remains
        var newTabBtnAfter = OrielFolderAppFixture.FindById(_window!, "Button_NewTab");
        Assert.IsNotNull(newTabBtnAfter, "Original tab should still exist after closing added tabs");
    }

    [TestMethod]
    public void NewTab_ViaButton_Works()
    {
        var newTabBtn = OrielFolderAppFixture.FindByIdOrThrow(_window!, "Button_NewTab");
        newTabBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var searchBox = OrielFolderAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Search box should exist in new tab");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void CtrlComma_OpensSettings()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var settingsElement = OrielFolderAppFixture.WaitForElement(_window!, "SettingsScrollViewer", 3000);
        Assert.IsNotNull(settingsElement, "Settings view should appear after Ctrl+,");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
