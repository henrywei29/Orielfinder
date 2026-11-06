using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrielFolder.Helpers;

namespace OrielFolder.Tests.Helpers;

[TestClass]
public class VisibleRowsTests
{
    private sealed record Row(string Path);

    [TestMethod]
    public void ExpandingAndCollapsingOnlyChangesBranchRows()
    {
        var roots = Enumerable.Range(0, 10000).Select(i => new Row($"root/{i}")).ToArray();
        var rows = new ObservableCollection<Row>(roots);
        var changes = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => changes.Add(e.Action);
        var expanded = roots.Take(4).Concat(new[] { new Row("child/a"), new Row("child/b") }).Concat(roots.Skip(4)).ToArray();
        int keyReads = 0;
        VisibleRows.Update(rows, expanded, r => { keyReads++; return r.Path; });
        Assert.IsTrue(keyReads <= roots.Length * 3, $"Unexpected repeated whole-list scans: {keyReads}");
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add }, changes);
        Assert.AreSame(roots[9999], rows[^1]);
        changes.Clear();
        VisibleRows.Update(rows, roots, r => r.Path);
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove }, changes);
        changes.Clear();
        VisibleRows.Update(rows, roots, r => r.Path);
        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public void ExpandingGroupedRowsKeepsGroupAndOtherBranchObjects()
    {
        var a = new Row("A"); var b = new Row("B");
        var group = new VisibleRowGroup<Row>("Folders (2)", new[] { a, b });
        var groups = new ObservableCollection<VisibleRowGroup<Row>> { group };
        int groupChanges = 0; groups.CollectionChanged += (_, _) => groupChanges++;
        VisibleRows.UpdateGroups(groups, new[] { new VisibleRowGroup<Row>("Folders (2)", new[] { a, new Row("A/child"), b }) }, r => r.Path);
        Assert.AreEqual(0, groupChanges);
        Assert.AreSame(group, groups[0]); Assert.AreSame(b, groups[0][2]);
    }

    [TestMethod]
    public void ReorderedAndRefreshedRowsUseCurrentObjects()
    {
        var rows = new ObservableCollection<Row> { new("A"), new("B"), new("C") };
        var next = new[] { new Row("b"), new Row("A"), new Row("C") };
        VisibleRows.Update(rows, next, r => r.Path);
        for (int i = 0; i < next.Length; i++) Assert.AreSame(next[i], rows[i]);
        VisibleRows.Update(rows, Array.Empty<Row>(), r => r.Path);
        Assert.AreEqual(0, rows.Count);
    }
}
