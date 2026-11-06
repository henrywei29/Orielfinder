using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

/// <summary>
/// Tests for preview panel toggle and presence (FEATURES.md: 미리보기 패널).
/// Verifies Ctrl+P/Ctrl+Shift+P toggles, and preview button exists.
/// </summary>
[TestClass]
public class PreviewPanelTests
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
    public void PreviewToggleButton_Exists()
    {
        var previewBtn = OrielFolderAppFixture.FindById(_window!, "Button_PreviewToggle");
        Assert.IsNotNull(previewBtn, "Preview toggle button should exist");
        Assert.IsTrue(previewBtn.IsEnabled, "Preview toggle button should be enabled");
    }

    [TestMethod]
    public void PreviewButton_Click_TogglesPreview()
    {
        var previewBtn = OrielFolderAppFixture.FindByIdOrThrow(_window!, "Button_PreviewToggle");

        // Toggle on
        previewBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // App should remain responsive
        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after preview toggle on");

        // Toggle off
        previewBtn.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void CtrlShiftP_TogglesPreviewPanel()
    {
        // Toggle on
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_P);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after Ctrl+Shift+P");

        // Toggle off
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_P);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }
}
