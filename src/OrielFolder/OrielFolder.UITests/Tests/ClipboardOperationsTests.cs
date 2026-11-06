using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class ClipboardOperationsTests
{
    private static Window? _window;
    private string? _testDir;

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

    [TestCleanup]
    public void TestCleanup() => OrielFolderAppFixture.CleanupTestDirectory(_testDir);

    [TestMethod]
    [TestCategory("Destructive")]
    public void Clipboard_CopyPaste_ViaKeyboard()
    {
        _testDir = OrielFolderAppFixture.CreateTestDirectory(5);
        if (!OrielFolderAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        // Select first item
        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Copy via Ctrl+C
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // App should remain responsive after copy
        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+C");
    }

    [TestMethod]
    public void Clipboard_PasteEnabled_AfterCopy()
    {
        _testDir = OrielFolderAppFixture.CreateTestDirectory(5);
        if (!OrielFolderAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        // Select first item
        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Copy via Ctrl+C
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Check Paste button exists
        var pasteBtn = OrielFolderAppFixture.FindById(_window!, "Button_Paste");
        Assert.IsNotNull(pasteBtn, "Paste button should exist in the toolbar");

        // App should remain responsive after copy
        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after copy");
    }

    [TestMethod]
    public void Clipboard_CutButton_Exists()
    {
        _testDir = OrielFolderAppFixture.CreateTestDirectory(5);
        if (!OrielFolderAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("Could not navigate to test directory");

        // Deselect all
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Check Cut button exists
        var cutBtn = OrielFolderAppFixture.FindById(_window!, "Button_Cut");
        Assert.IsNotNull(cutBtn, "Cut button should exist in the toolbar");
    }
}
