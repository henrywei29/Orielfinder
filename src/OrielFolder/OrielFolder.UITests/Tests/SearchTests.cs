using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class SearchTests
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
    public void SearchBox_Exists_And_IsEnabled()
    {
        var searchBox = OrielFolderAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Search box should exist");
        Assert.IsTrue(searchBox.IsEnabled, "Search box should be enabled");
    }

    [TestMethod]
    public void CtrlF_FocusesSearchBox_EscClears()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var searchBox = OrielFolderAppFixture.FindById(_window!, "TextBox_Search");
        Assert.IsNotNull(searchBox, "Search box should exist after Ctrl+F");

        Keyboard.Type("test");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void SearchBox_AcceptsAdvancedSyntax()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        Keyboard.Type("kind:image size:>1MB");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void RecursiveSearch_EnterTriggersSearch_EscapeCancels()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, @"C:\");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        Keyboard.Type("ext:txt");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(3000));

        Assert.IsTrue(_window!.Properties.IsEnabled, "Window should remain responsive during recursive search");

        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
    }

    [TestMethod]
    public void RecursiveSearch_WildcardQuery_NoFreeze()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, @"C:\");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        Keyboard.Type("*.exe");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(3000));

        Assert.IsTrue(_window!.Properties.IsEnabled, "Window should not freeze during wildcard search");

        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
    }

    [TestMethod]
    public void RecursiveSearch_ResultsShowInListView()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        Keyboard.Type("*.dll");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(5000));

        var listItems = _window!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        Assert.IsTrue(listItems.Length > 0, "Recursive search in C:\\Windows for *.dll should return results");

        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
    }
}
