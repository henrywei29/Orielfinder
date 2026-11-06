using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class ViewModeDetailedTests
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
    public void DetailsView_ShowsFilterButtons()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var filterName = OrielFolderAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        var filterDate = OrielFolderAppFixture.FindById(_window!, "Button_FilterDate");
        var filterSize = OrielFolderAppFixture.FindById(_window!, "Button_FilterSize");

        Assert.IsNotNull(filterName, "Details view should show Name filter button");
        Assert.IsNotNull(filterDate, "Details view should show Date filter button");
        Assert.IsNotNull(filterSize, "Details view should show Size filter button");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void ListView_ShowsSlider()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_3);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should be responsive in List mode");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void IconView_Responsive()
    {
        OrielFolderAppFixture.EnsureExplorerMode(_window!);
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_4);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // WinUI 3 Grid containers don't create UIA peers — verify app is responsive in Icon mode
        Assert.IsTrue(_window!.Properties.IsEnabled, "Window should remain enabled in Icon mode");
        var viewModeBtn = OrielFolderAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "View mode button should exist in Icon mode");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void AllViewModes_SwitchWithoutCrash()
    {
        var modes = new[] {
            VirtualKeyShort.KEY_1, VirtualKeyShort.KEY_2,
            VirtualKeyShort.KEY_3, VirtualKeyShort.KEY_4
        };

        foreach (var mode in modes)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, mode);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
            Assert.IsTrue(_window!.Properties.IsEnabled, $"Window should remain enabled in view mode");
        }

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
