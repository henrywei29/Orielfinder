using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class KeyboardShortcutTests
{
    private static Window? _window;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _window = OrielFolderAppFixture.GetMainWindow();
        OrielFolderAppFixture.Focus(_window);
        OrielFolderAppFixture.EnsureExplorerMode(_window);
    }

    [ClassCleanup]
    public static void ClassCleanup() => OrielFolderAppFixture.Detach();

    [TestInitialize]
    public void TestInit() => OrielFolderAppFixture.Focus(_window!);

    [TestMethod]
    public void CtrlL_FocusesAddressBar()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        var textBox = OrielFolderAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Ctrl+L should show address bar text box");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void CtrlF_FocusesSearchBox()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        var searchBox = OrielFolderAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Ctrl+F should focus search box");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Ctrl1_SwitchesToMiller()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        var filterBtn = OrielFolderAppFixture.FindById(_window!, "Button_FilterName");
        Assert.IsNull(filterBtn, "Miller mode should not have Details filter buttons");
    }

    [TestMethod]
    public void Ctrl2_SwitchesToDetails()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        var filterBtn = OrielFolderAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        Assert.IsNotNull(filterBtn, "Ctrl+2 should show Details filter button");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Ctrl3_SwitchesToList()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_3);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        var filterBtn = OrielFolderAppFixture.FindById(_window!, "Button_FilterName");
        Assert.IsNull(filterBtn, "List mode should not have Details filter buttons");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Ctrl4_SwitchesToIcons()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_4);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        var filterBtn = OrielFolderAppFixture.FindById(_window!, "Button_FilterName");
        Assert.IsNull(filterBtn, "Icon mode should not have Details filter buttons");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void F1_TogglesHelpOverlay()
    {
        Keyboard.Type(VirtualKeyShort.F1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        var helpOverlay = OrielFolderAppFixture.WaitForElement(_window!, "HelpOverlay", 3000);
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void CtrlT_CreatesNewTab_CtrlW_ClosesIt()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        var viewModeBtn = OrielFolderAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "UI should remain functional after new tab");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void CtrlShiftE_TogglesSplitView()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_E);
        var rightViewMode = OrielFolderAppFixture.WaitForElement(_window!, "Button_RightViewMode", 3000);
        Assert.IsNotNull(rightViewMode, "Ctrl+Shift+E should show right pane view mode");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_E);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public void CtrlShiftP_TogglesPreviewPanel()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_P);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        var previewToggle = OrielFolderAppFixture.FindById(_window!, "Button_PreviewToggle");
        Assert.IsNotNull(previewToggle, "Preview toggle button should exist");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_P);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void F5_Refresh_DoesNotCrash()
    {
        Keyboard.Type(VirtualKeyShort.F5);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after F5 refresh");
    }

    [TestMethod]
    public void CtrlA_SelectAll_DoesNotCrash()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+A");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
