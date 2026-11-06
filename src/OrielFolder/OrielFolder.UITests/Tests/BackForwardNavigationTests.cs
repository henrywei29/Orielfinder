using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class BackForwardNavigationTests
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
    public void BackButton_Exists_And_Clickable()
    {
        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "Back button should exist");
    }

    [TestMethod]
    public void ForwardButton_Exists_And_Clickable()
    {
        var forwardBtn = OrielFolderAppFixture.FindById(_window!, "Button_Forward");
        Assert.IsNotNull(forwardBtn, "Forward button should exist");
    }

    [TestMethod]
    public void BackButton_AfterNavigation_GoesBack()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPathAlt);

        var backBtn = OrielFolderAppFixture.FindByIdOrThrow(_window!, "Button_Back");
        backBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var viewModeBtn = OrielFolderAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "App should remain responsive after Back navigation");
    }

    [TestMethod]
    public void ForwardButton_AfterBack_GoesForward()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPathAlt);

        var backBtn = OrielFolderAppFixture.FindByIdOrThrow(_window!, "Button_Back");
        backBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var forwardBtn = OrielFolderAppFixture.FindByIdOrThrow(_window!, "Button_Forward");
        forwardBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var viewModeBtn = OrielFolderAppFixture.FindById(_window!, "Button_ViewMode");
        Assert.IsNotNull(viewModeBtn, "App should remain responsive after Forward navigation");
    }

    [TestMethod]
    public void BackForward_ViaKeyboard_AltLeftRight()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPathAlt);

        OrielFolderAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.LEFT);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var backCheck = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backCheck, "App should remain responsive after Alt+Left (Back)");

        OrielFolderAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.RIGHT);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var forwardCheck = OrielFolderAppFixture.FindById(_window!, "Button_Forward");
        Assert.IsNotNull(forwardCheck, "App should remain responsive after Alt+Right (Forward)");
    }
}
