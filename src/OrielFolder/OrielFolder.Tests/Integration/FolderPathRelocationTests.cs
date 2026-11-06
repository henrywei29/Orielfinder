using OrielFolder.Services.FileOperations;

namespace OrielFolder.Tests.Integration;

[TestClass]
[DoNotParallelize]
public class FolderPathRelocationTests
{
    private string _root = null!;
    private readonly List<(string OldPath, string NewPath)> _moves = new();
    private Action<string, string> _listener = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderPathRelocation_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _listener = (oldPath, newPath) =>
        {
            Assert.IsFalse(Directory.Exists(oldPath), "Notification must follow the physical move.");
            Assert.IsTrue(Directory.Exists(newPath));
            _moves.Add((oldPath, newPath));
        };
        FileOperationHelpers.PathRelocated += _listener;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_listener != null)
            FileOperationHelpers.PathRelocated -= _listener;
        // Keep disposable evidence under the development TEMP directory; no recursive cleanup.
    }

    [TestMethod]
    public async Task RenameFolder_ReportsExactlyOncePerExecuteAndUndo()
    {
        var source = NewFolder("before");
        var target = Path.Combine(_root, "after");
        var op = new RenameFileOperation(source, "after");
        Assert.IsTrue((await op.ExecuteAsync()).Success);
        CollectionAssert.AreEqual(new[] { (source, target) }, _moves);
        Assert.IsTrue((await op.UndoAsync()).Success);
        CollectionAssert.AreEqual(new[] { (source, target), (target, source) }, _moves);
    }

    [TestMethod]
    public async Task RenameFolder_ConflictAndCancelledOperation_ReportNothing()
    {
        var source = NewFolder("before");
        NewFolder("taken");
        Assert.IsFalse((await new RenameFileOperation(source, "taken").ExecuteAsync()).Success);
        Assert.IsFalse((await new RenameFileOperation(source, "after").ExecuteAsync(cancellationToken: new CancellationToken(true))).Success);
        Assert.AreEqual(0, _moves.Count);
        Assert.IsTrue(Directory.Exists(source));
    }

    [TestMethod]
    public async Task MoveFolder_KeepBoth_ReportsActualDestinationAndUndo()
    {
        var source = NewFolder("folder");
        var destination = NewFolder("destination");
        Directory.CreateDirectory(Path.Combine(destination, "folder"));
        var target = Path.Combine(destination, "folder (1)");
        var op = new MoveFileOperation(new List<string> { source }, destination);
        op.SetConflictResolution(ConflictResolution.KeepBoth, true);
        Assert.IsTrue((await op.ExecuteAsync()).Success);
        CollectionAssert.AreEqual(new[] { (source, target) }, _moves);
        Assert.IsTrue((await op.UndoAsync()).Success);
        CollectionAssert.AreEqual(new[] { (source, target), (target, source) }, _moves);
    }

    [TestMethod]
    public async Task MoveFolder_SkippedOrMissing_ReportsNothing()
    {
        var source = NewFolder("folder");
        var destination = NewFolder("destination");
        Directory.CreateDirectory(Path.Combine(destination, "folder"));
        var op = new MoveFileOperation(new List<string> { source }, destination);
        op.SetConflictResolution(ConflictResolution.Skip, true);
        Assert.IsTrue((await op.ExecuteAsync()).Success);
        Assert.IsFalse((await new MoveFileOperation(new List<string> { Path.Combine(_root, "missing") }, destination).ExecuteAsync()).Success);
        Assert.AreEqual(0, _moves.Count);
    }

    [TestMethod]
    public async Task BatchRename_PartialFailure_ReportsOnlySuccessfulFolderAndUndo()
    {
        var source = NewFolder("folder");
        var target = Path.Combine(_root, "renamed");
        var blocked = NewFolder("blocked");
        NewFolder("taken");
        var op = new BatchRenameOperation(new() { (source, "renamed"), (blocked, "taken") });
        Assert.IsFalse((await op.ExecuteAsync()).Success);
        CollectionAssert.AreEqual(new[] { (source, target) }, _moves);
        Assert.IsTrue((await op.UndoAsync()).Success);
        CollectionAssert.AreEqual(new[] { (source, target), (target, source) }, _moves);
    }

    [TestMethod]
    public async Task FilesAndDirectoryCopies_ReportNothing()
    {
        var file = Path.Combine(_root, "file.txt");
        File.WriteAllText(file, "file content");
        var rename = new RenameFileOperation(file, "renamed.txt");
        Assert.IsTrue((await rename.ExecuteAsync()).Success);
        Assert.IsTrue((await rename.UndoAsync()).Success);
        var destination = NewFolder("destination");
        var move = new MoveFileOperation(new List<string> { file }, destination);
        Assert.IsTrue((await move.ExecuteAsync()).Success);
        Assert.IsTrue((await move.UndoAsync()).Success);
        var folder = NewFolder("folder");
        Assert.IsTrue((await new CopyFileOperation(new List<string> { folder }, destination).ExecuteAsync()).Success);
        Assert.AreEqual(0, _moves.Count);
    }

    [TestMethod]
    public async Task FailedFolderUndo_ReportsNothingAfterCompletedMove()
    {
        var source = NewFolder("folder");
        var destination = NewFolder("destination");
        var target = Path.Combine(destination, "folder");
        var op = new MoveFileOperation(new List<string> { source }, destination);
        Assert.IsTrue((await op.ExecuteAsync()).Success);
        CollectionAssert.AreEqual(new[] { (source, target) }, _moves);
        NewFolder("folder"); // Original name has been occupied by another folder.
        Assert.IsFalse((await op.UndoAsync()).Success);
        CollectionAssert.AreEqual(new[] { (source, target) }, _moves);
        Assert.IsTrue(Directory.Exists(source));
        Assert.IsTrue(Directory.Exists(target));
    }

    [TestMethod]
    public async Task SubscriberFailure_DoesNotFailPhysicalRenameOrOtherSubscribers()
    {
        Action<string, string> throwing = (_, _) => throw new InvalidOperationException("subscriber failure");
        FileOperationHelpers.PathRelocated -= _listener;
        FileOperationHelpers.PathRelocated += throwing;
        FileOperationHelpers.PathRelocated += _listener;
        try
        {
            var source = NewFolder("before");
            var target = Path.Combine(_root, "after");
            Assert.IsTrue((await new RenameFileOperation(source, "after").ExecuteAsync()).Success);
            CollectionAssert.AreEqual(new[] { (source, target) }, _moves);
        }
        finally
        {
            FileOperationHelpers.PathRelocated -= throwing;
        }
    }

    private string NewFolder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(path, "nested"));
        File.WriteAllText(Path.Combine(path, "nested", "evidence.txt"), "folder content");
        return path;
    }
}
