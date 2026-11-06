using OrielFolder.Services;

namespace OrielFolder.Tests.Services;

[TestClass]
public class FolderChildCountServiceTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        // Keep inspectable disposable fixtures on D:, including after a failed test.
        _root = Path.Combine(@"D:\codex\Orielfolder-development\test-temp", "child-count-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestMethod]
    public async Task CountsDirectFilesAndFoldersWithoutCountingDescendants()
    {
        File.WriteAllText(Path.Combine(_root, "one.txt"), "one");
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllText(Path.Combine(nested.FullName, "two.txt"), "two");

        var result = await FolderChildCountService.ReadAsync(_root, false, null, default);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public async Task EmptyDirectoryIsConfirmedZeroButMissingDirectoryIsUnknown()
    {
        var empty = await FolderChildCountService.ReadAsync(_root, false, null, default);
        var missing = await FolderChildCountService.ReadAsync(Path.Combine(_root, "missing"), false, null, default);

        Assert.IsNotNull(empty);
        Assert.AreEqual(0, empty.Count);
        Assert.IsNull(missing);
    }

    [TestMethod]
    public async Task HiddenToggleIncludesHiddenEntriesAndKeepsSystemOnlyEntriesVisible()
    {
        var hidden = Path.Combine(_root, "hidden.txt");
        var system = Path.Combine(_root, "system.txt");
        File.WriteAllText(hidden, "hidden");
        File.WriteAllText(system, "system");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(system, FileAttributes.System);
        var hiddenDirectory = Directory.CreateDirectory(Path.Combine(_root, "hidden-folder"));
        hiddenDirectory.Attributes |= FileAttributes.Hidden;

        var filtered = await FolderChildCountService.ReadAsync(_root, false, null, default);
        var all = await FolderChildCountService.ReadAsync(_root, true, filtered, default);

        Assert.AreEqual(1, filtered?.Count);
        Assert.AreEqual(3, all?.Count);
    }

    [TestMethod]
    public async Task DirectoryChangeInvalidatesPreviousCount()
    {
        var first = await FolderChildCountService.ReadAsync(_root, false, null, default);
        Assert.IsNotNull(first);
        var unchanged = await FolderChildCountService.ReadAsync(_root, false, first, default);
        Assert.AreSame(first, unchanged);
        File.WriteAllText(Path.Combine(_root, "new.txt"), "new");
        Directory.SetLastWriteTimeUtc(_root, first!.LastWriteTimeUtc.AddSeconds(2));

        var changed = await FolderChildCountService.ReadAsync(_root, false, first, default);

        Assert.AreEqual(1, changed?.Count);
    }

    [TestMethod]
    public async Task ExplicitRefreshIgnoresPreviousTimestamp()
    {
        var first = await FolderChildCountService.ReadAsync(_root, false, null, default);
        Assert.IsNotNull(first);
        File.WriteAllText(Path.Combine(_root, "new.txt"), "new");
        Directory.SetLastWriteTimeUtc(_root, first!.LastWriteTimeUtc);

        var refreshed = await FolderChildCountService.ReadAsync(_root, false, null, default);

        Assert.AreEqual(1, refreshed?.Count);
    }

    [TestMethod]
    public async Task CanceledRequestDoesNotReturnAnEmptyCount()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await FolderChildCountService.ReadAsync(_root, false, null, cancellation.Token);
            Assert.Fail("A canceled request must not produce a count.");
        }
        catch (OperationCanceledException) { }
    }
}
