using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class ViewModeTests
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
    public void Shortcut_Ctrl1_SwitchesToMiller()
    {
        OrielFolderAppFixture.EnsureExplorerMode(_window!);
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(800));
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // WinUI 3 Grid containers don't create UIA peers — verify via ListItem presence
        var listItems = _window!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        Assert.IsTrue(listItems.Length > 0, "Miller mode should show ListItems after Ctrl+1");
    }

    [TestMethod]
    public void Shortcut_Ctrl2_SwitchesToDetails()
    {
        OrielFolderAppFixture.EnsureExplorerMode(_window!);
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // WinUI 3 Grid containers don't create UIA peers — verify via Details filter buttons
        var filterBtn = OrielFolderAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        Assert.IsNotNull(filterBtn, "Details mode should show filter buttons after Ctrl+2");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void ViewModeButton_Exists()
    {
        var viewModeBtn = OrielFolderAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "View mode button should exist");
        Assert.IsTrue(viewModeBtn.IsEnabled, "View mode button should be enabled");
    }
}
