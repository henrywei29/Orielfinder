using OrielFolder.Helpers;

namespace OrielFolder.Tests.Helpers;

[TestClass]
public class InlineTreeStateTests
{
    private sealed record Node(string Path, bool Folder = true)
    {
        public List<Node> Children { get; } = new();
    }

    private static InlineTreeState<Node> Create(Func<Node, Task>? load = null) =>
        new(n => n.Path, n => n.Folder, n => n.Children, load ?? (_ => Task.CompletedTask));

    private static string[] Paths(InlineTreeState<Node> tree, params Node[] roots) =>
        tree.Project(roots).Select(row => row.Item.Path).ToArray();

    [TestMethod]
    public async Task AlreadyLoadedBranchEmitsOnlyOneRefresh()
    {
        var tree = Create(); tree.SetRoot("root"); int changes = 0;
        tree.Changed += () => changes++;
        await tree.ToggleAsync(new Node("A"));
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public async Task ExpandingSibling_KeepsBothBranchesAndSourceChildrenUnchanged()
    {
        var a = new Node("A"); var b = new Node("B");
        a.Children.Add(new("A/file", false)); b.Children.Add(new("B/file", false));
        var roots = new[] { a, b }; var tree = Create(); tree.SetRoot("root");
        await tree.ToggleAsync(a); await tree.ToggleAsync(b);
        CollectionAssert.AreEqual(new[] { "A", "A/file", "B", "B/file" }, Paths(tree, roots));
        Assert.AreEqual(2, roots.Length); Assert.AreEqual(1, a.Children.Count);
        CollectionAssert.AreEqual(new[] { 0, 1, 0, 1 }, tree.Project(roots).Select(r => r.Depth).ToArray());
    }

    [TestMethod]
    public async Task Collapse_HidesOnlyItsDescendantsAndReopenRestoresNestedExpansion()
    {
        var a = new Node("A"); var nested = new Node("A/nested"); var b = new Node("B");
        a.Children.Add(nested); nested.Children.Add(new("A/nested/file", false));
        var tree = Create(); tree.SetRoot("root");
        await tree.ToggleAsync(a); await tree.ToggleAsync(nested); await tree.ToggleAsync(a);
        CollectionAssert.AreEqual(new[] { "A", "B" }, Paths(tree, a, b));
        await tree.ToggleAsync(a);
        CollectionAssert.AreEqual(new[] { "A", "A/nested", "A/nested/file", "B" }, Paths(tree, a, b));
    }

    [TestMethod]
    public async Task SeparatePaneStates_DoNotChangeEachOther()
    {
        var a = new Node("A"); a.Children.Add(new("A/file", false));
        var left = Create(); var right = Create(); left.SetRoot("root"); right.SetRoot("root");
        await left.ToggleAsync(a);
        CollectionAssert.AreEqual(new[] { "A", "A/file" }, Paths(left, a));
        CollectionAssert.AreEqual(new[] { "A" }, Paths(right, a));
        left.SetRoot("ROOT"); Assert.IsTrue(left.IsExpanded(a));
    }

    [TestMethod]
    public async Task CollapseWhileLoading_DoesNotReopenWhenLoadCompletes()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = new Node("A"); var tree = Create(async n => { await ready.Task; n.Children.Add(new("A/file", false)); });
        tree.SetRoot("root"); var expanding = tree.ToggleAsync(a);
        Assert.IsTrue(tree.IsExpanded(a));
        await tree.ToggleAsync(a); ready.SetResult(); await expanding;
        Assert.IsFalse(tree.IsExpanded(a)); CollectionAssert.AreEqual(new[] { "A" }, Paths(tree, a));
    }

    [TestMethod]
    public async Task NavigationDuringLoad_DoesNotRestoreOldBranchOrNotifyNewRoot()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = new Node("A"); var tree = Create(_ => ready.Task); tree.SetRoot("old");
        var pending = tree.ToggleAsync(a); Assert.IsTrue(tree.IsExpanded(a)); tree.SetRoot("new");
        int changes = 0; tree.Changed += () => changes++;
        ready.SetResult(); await pending;
        Assert.AreEqual(0, changes); Assert.IsFalse(tree.IsExpanded(a));
    }

    [TestMethod]
    public async Task ReexpandWhilePending_DeduplicatesLazyLoad()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
        var a = new Node("A"); var tree = Create(_ => { calls++; return ready.Task; }); tree.SetRoot("root");
        var first = tree.ToggleAsync(a); await tree.ToggleAsync(a); var again = tree.ToggleAsync(a);
        Assert.AreEqual(1, calls); ready.SetResult(); await Task.WhenAll(first, again);
        Assert.IsTrue(tree.IsExpanded(a));
    }

    [TestMethod]
    public async Task FailedLoad_CollapsesOnlyFailedBranchAndAllowsRetry()
    {
        int calls = 0; var a = new Node("A"); var b = new Node("B");
        var tree = Create(n => n == a && ++calls == 1 ? Task.FromException(new IOException("test access failure")) : Task.CompletedTask);
        tree.SetRoot("root"); await tree.ToggleAsync(b);
        await Assert.ThrowsExceptionAsync<IOException>(() => tree.ToggleAsync(a));
        Assert.IsFalse(tree.IsExpanded(a)); Assert.IsTrue(tree.IsExpanded(b));
        await tree.ToggleAsync(a); Assert.IsTrue(tree.IsExpanded(a));
    }

    [TestMethod]
    public async Task Projection_SortsSiblingsWithoutSeparatingChildrenFromParent()
    {
        var a = new Node("A"); var b = new Node("B");
        a.Children.AddRange(new[] { new Node("z", false), new Node("c", false) });
        var tree = Create(); tree.SetRoot("root"); await tree.ToggleAsync(a);
        var rows = tree.Project(new[] { b, a }, items => items.OrderBy(n => n.Path));
        CollectionAssert.AreEqual(new[] { "A", "c", "z", "B" }, rows.Select(r => r.Item.Path).ToArray());
        Assert.AreSame(a.Children[1], rows[1].Item);
    }

    [TestMethod]
    public async Task FilesCannotExpand_AndProjectionStopsRepeatedAncestor()
    {
        var a = new Node("A"); a.Children.Add(a); var file = new Node("file", false);
        int calls = 0; var tree = Create(_ => { calls++; return Task.CompletedTask; }); tree.SetRoot("root");
        await tree.ToggleAsync(file); Assert.IsFalse(tree.IsExpanded(file)); Assert.AreEqual(0, calls);
        await tree.ToggleAsync(a); CollectionAssert.AreEqual(new[] { "A" }, Paths(tree, a));
    }

    [TestMethod]
    public async Task UnexpandedDirectories_AreNeverEnumeratedAndExistingBranchesRespectFilter()
    {
        int reads = 0;
        var a = new Node("A"); a.Children.AddRange(new[] { new Node("keep", false), new Node("hide", false) });
        var b = new Node("B");
        var tree = new InlineTreeState<Node>(n => n.Path, n => n.Folder,
            n => { reads++; return n.Children; }, _ => Task.CompletedTask);
        tree.SetRoot("root"); tree.Project(new[] { a, b }); Assert.AreEqual(0, reads);
        await tree.ToggleAsync(a);
        var rows = tree.Project(new[] { a, b }, items => items.Where(n => n.Path != "hide"));
        CollectionAssert.AreEqual(new[] { "A", "keep", "B" }, rows.Select(r => r.Item.Path).ToArray());
        Assert.AreEqual(1, reads); Assert.IsTrue(tree.IsExpanded(a));
    }

    [TestMethod]
    public async Task RefreshAndSeparateGroupedProjections_KeepExpansionByPathAndOriginalItemIdentity()
    {
        var a = new Node("A"); var b = new Node("B"); var tree = Create(); tree.SetRoot("root");
        await tree.ToggleAsync(a); await tree.ToggleAsync(b);
        var refreshed = new Node("a"); var child = new Node("a/new-file", false); refreshed.Children.Add(child);
        var firstGroup = tree.Project(new[] { refreshed }); var secondGroup = tree.Project(new[] { b });
        Assert.AreEqual(2, firstGroup.Count); Assert.AreSame(child, firstGroup[1].Item);
        Assert.IsTrue(firstGroup[0].IsExpanded); Assert.IsTrue(secondGroup[0].IsExpanded);
        tree.SetRoot("new-root"); Assert.IsFalse(tree.IsExpanded(refreshed));
    }
}
