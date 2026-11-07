using System.Collections.Generic;
using OrielFolder.Helpers;

namespace OrielFolder.Tests.Helpers;

/// <summary>
/// 启动时侧栏收藏夹先用上次 Quick Access 结果（快照）显示，再在后台读取 Quick Access。
/// 快照只在内容变化时才替换列表，以免启动后侧栏无意义地重建。
/// </summary>
[TestClass]
public class FavoritesSnapshotTests
{
    private static List<FavoriteSnapshotEntry> Sample() => new()
    {
        new FavoriteSnapshotEntry("Desktop", @"C:\Users\me\Desktop"),
        new FavoriteSnapshotEntry("项目", @"D:\project"),
    };

    [TestMethod]
    public void RoundTrip_PreservesNamesPathsAndOrder()
    {
        var json = FavoritesSnapshot.Serialize(Sample());
        var back = FavoritesSnapshot.Deserialize(json);
        Assert.IsNotNull(back);
        Assert.IsTrue(FavoritesSnapshot.SameEntries(Sample(), back));
    }

    [TestMethod]
    public void Deserialize_MissingOrBrokenOrEmpty_ReturnsNull()
    {
        Assert.IsNull(FavoritesSnapshot.Deserialize(null));
        Assert.IsNull(FavoritesSnapshot.Deserialize(""));
        Assert.IsNull(FavoritesSnapshot.Deserialize("{not json"));
        Assert.IsNull(FavoritesSnapshot.Deserialize("[]"));
        Assert.IsNull(FavoritesSnapshot.Deserialize("null"));
    }

    [TestMethod]
    public void Deserialize_SkipsEntriesWithoutPath()
    {
        // JSON: [{"Name":"a","Path":""},{"Name":null,"Path":"D:\\x"}]  (escaped backslash)
        const string json = @"[{""Name"":""a"",""Path"":""""},{""Name"":null,""Path"":""D:\\x""}]";
        var back = FavoritesSnapshot.Deserialize(json);
        Assert.IsNotNull(back);
        Assert.AreEqual(1, back.Count);
        Assert.AreEqual(@"D:\x", back[0].Path);
        Assert.AreEqual("", back[0].Name);
    }

    [TestMethod]
    public void SameEntries_PathCaseInsensitive_NameAndOrderSensitive()
    {
        var a = Sample();
        var upper = new List<FavoriteSnapshotEntry>
        {
            new("Desktop", @"C:\USERS\ME\DESKTOP"),
            new("项目", @"d:\PROJECT"),
        };
        Assert.IsTrue(FavoritesSnapshot.SameEntries(a, upper));

        var renamed = new List<FavoriteSnapshotEntry> { new("桌面", @"C:\Users\me\Desktop"), new("项目", @"D:\project") };
        Assert.IsFalse(FavoritesSnapshot.SameEntries(a, renamed));

        var reordered = new List<FavoriteSnapshotEntry> { a[1], a[0] };
        Assert.IsFalse(FavoritesSnapshot.SameEntries(a, reordered));

        var shorter = new List<FavoriteSnapshotEntry> { a[0] };
        Assert.IsFalse(FavoritesSnapshot.SameEntries(a, shorter));
    }

    [TestMethod]
    public void SameEntries_NoSnapshotShown_IsDifferentFromLoadedList()
    {
        // 没有快照（首次启动）时必须用 Quick Access 结果填充侧栏。
        Assert.IsFalse(FavoritesSnapshot.SameEntries(null, Sample()));
        Assert.IsTrue(FavoritesSnapshot.SameEntries(null, null));
    }
}
