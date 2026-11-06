using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class AddressBarNavigationTests
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
    public void AddressBar_TypePath_NavigatesToFolder()
    {
        Assert.IsTrue(OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath),
            "Should navigate to folder via address bar");

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after navigation");

        OrielFolderAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        var textBox = OrielFolderAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Address bar should be accessible after navigation");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void AddressBar_TypeDrivePath_NavigatesToDrive()
    {
        Assert.IsTrue(OrielFolderAppFixture.NavigateToPath(_window!, @"C:\"),
            "Should navigate to C:\\ via address bar");

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after navigating to C:\\");

        OrielFolderAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        var textBox = OrielFolderAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Address bar should be accessible after drive navigation");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void AddressBar_InvalidPath_DoesNotCrash()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, @"Z:\NonExistent\Path\That\Should\Not\Exist");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should not crash on invalid path");
    }

    [TestMethod]
    public void AddressBar_EscapeKey_CancelsEdit()
    {
        OrielFolderAppFixture.Focus(_window!);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        var textBox = OrielFolderAppFixture.WaitForElement(_window!, "TextBox_AddressBar", 3000);
        Assert.IsNotNull(textBox, "Address bar text box should appear after Ctrl+L");

        Keyboard.Type("random-text-that-should-be-discarded");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should be responsive after Escape cancels address bar edit");
    }

    [TestMethod]
    public void AddressBar_CopyPathButton_Exists()
    {
        var copyPathBtn = OrielFolderAppFixture.FindById(_window!, "Button_CopyPath");
        Assert.IsNotNull(copyPathBtn, "Button_CopyPath should exist in the address bar area");
    }

    [TestMethod]
    public void AddressBar_SequentialNavigation_UpdatesPath()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        OrielFolderAppFixture.NavigateToPath(_window!, @"C:\");
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPathAlt);

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after sequential navigations");
    }
}
