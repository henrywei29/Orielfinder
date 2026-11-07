using OrielFolder.Helpers;

namespace OrielFolder.Tests.Helpers;

/// <summary>
/// 大块不写入的数组能抬高 WinUI 内存压力触发 Gen2 的阈值。它只占提交量、不占实际内存，
/// 所以按物理内存决定大小：内存小的机器不用或用小一点。
/// </summary>
[TestClass]
public class GcBallastTests
{
    private const long MB = 1024L * 1024L;
    private const long GB = 1024L * MB;

    [TestMethod]
    [DataRow(64L, 512L)]
    [DataRow(16L, 512L)]
    [DataRow(8L, 512L)]
    [DataRow(6L, 256L)]
    [DataRow(4L, 256L)]
    public void SizeFollowsInstalledMemory(long totalGb, long expectedMb)
        => Assert.AreEqual(expectedMb * MB, GcBallast.ChooseSizeBytes(totalGb * GB));

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(2L * 1024 * 1024 * 1024)]
    [DataRow(4L * 1024 * 1024 * 1024 - 1)]
    public void SmallOrUnknownMemory_NoBallast(long totalBytes)
        => Assert.AreEqual(0, GcBallast.ChooseSizeBytes(totalBytes));

    [TestMethod]
    public void Install_IsIdempotent()
    {
        long first = GcBallast.Install();
        Assert.AreEqual(first, GcBallast.Install());
    }
}
