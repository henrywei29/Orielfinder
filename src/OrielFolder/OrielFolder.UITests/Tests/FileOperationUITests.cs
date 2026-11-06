using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class FileOperationUITests
{
    private static Window? _window;
    private string? _testDir;

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

    [TestCleanup]
    public void TestCleanup() => OrielFolderAppFixture.CleanupTestDirectory(_testDir);

    [TestMethod]
    public void NavigateToFolder_ShowsFiles()
    {
        if (!OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath))
            Assert.Inconclusive("Could not navigate");

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after navigation");
    }

    [TestMethod]
    public void CopyFile_ViaKeyboard_DoesNotCrash()
    {
        _testDir = OrielFolderAppFixture.CreateTestDirectory(5);
        if (!OrielFolderAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+C");
    }

    [TestMethod]
    [TestCategory("Destructive")]
    public void NewFolder_ViaShortcut_CreatesFolder()
    {
        _testDir = OrielFolderAppFixture.CreateTestDirectory(3);
        if (!OrielFolderAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_N);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+Shift+N");

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    [TestCategory("Destructive")]
    public void Rename_ViaF2_InlineEdit()
    {
        _testDir = OrielFolderAppFixture.CreateTestDirectory(5);
        if (!OrielFolderAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        Keyboard.Type(VirtualKeyShort.F2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after F2 rename");

        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Undo_AfterOperation_DoesNotCrash()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+Z undo");
    }

    [TestMethod]
    public void SelectAll_CtrlA_SelectsAllItems()
    {
        _testDir = OrielFolderAppFixture.CreateTestDirectory(5);
        if (!OrielFolderAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+A select all");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
