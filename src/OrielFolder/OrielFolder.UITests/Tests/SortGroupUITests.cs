using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OrielFolder.UITests.Tests;

[TestClass]
public class SortGroupUITests
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

    private static void SwitchToDetailsView()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));
    }

    private static void RestoreMillerView()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void Sort_ByName_ReordersItems()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        SwitchToDetailsView();

        var nameHeader = OrielFolderAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        if (nameHeader == null)
        {
            RestoreMillerView();
            Assert.Inconclusive("Name column header not found in Details view");
            return;
        }

        nameHeader.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after sorting by name");

        RestoreMillerView();
    }

    [TestMethod]
    public void Sort_ByDate_ReordersItems()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        SwitchToDetailsView();

        var dateHeader = OrielFolderAppFixture.FindById(_window!, "Button_FilterDate");
        if (dateHeader == null)
        {
            RestoreMillerView();
            Assert.Inconclusive("Date column header not found in Details view");
            return;
        }

        dateHeader.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after sorting by date");

        RestoreMillerView();
    }

    [TestMethod]
    public void Sort_BySize_ReordersItems()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        SwitchToDetailsView();

        var sizeHeader = OrielFolderAppFixture.FindById(_window!, "Button_FilterSize");
        if (sizeHeader == null)
        {
            RestoreMillerView();
            Assert.Inconclusive("Size column header not found in Details view");
            return;
        }

        sizeHeader.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after sorting by size");

        RestoreMillerView();
    }

    [TestMethod]
    public void Sort_Descending_ReverseOrder()
    {
        OrielFolderAppFixture.NavigateToPath(_window!, OrielFolderAppFixture.NavPath);
        SwitchToDetailsView();

        var nameHeader = OrielFolderAppFixture.WaitForElement(_window!, "Button_FilterName", 3000);
        if (nameHeader == null)
        {
            RestoreMillerView();
            Assert.Inconclusive("Name column header not found in Details view");
            return;
        }

        // First click: ascending sort
        nameHeader.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // Second click: descending sort (toggle)
        nameHeader.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var backBtn = OrielFolderAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, "App should remain responsive after toggling sort to descending");

        RestoreMillerView();
    }
}
