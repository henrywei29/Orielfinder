using OrielFolder.Helpers;

namespace OrielFolder.Tests.Helpers;

/// <summary>
/// 冷启动时窗口先隐藏（cloak），渲染出第一帧内容后立即显示，而不是等低优先级空闲回调
/// （打开文件夹时那要多等约 1 秒）。空闲回调仍是兜底，且总共只显示一次。
/// </summary>
[TestClass]
public class StartupRevealGateTests
{
    [TestMethod]
    public void RevealsOnSecondFrame_ExactlyOnce()
    {
        var gate = new StartupRevealGate();
        Assert.IsFalse(gate.OnFrame(), "first Rendering callback: the frame is not presented yet");
        Assert.IsTrue(gate.OnFrame(), "second callback: the first frame has been presented");
        Assert.IsTrue(gate.Revealed);
        Assert.IsFalse(gate.OnFrame());
        Assert.IsFalse(gate.TryRevealNow(), "idle fallback must not reveal again");
    }

    [TestMethod]
    public void IdleFallback_RevealsWhenNoFrameCame()
    {
        var gate = new StartupRevealGate();
        Assert.IsTrue(gate.TryRevealNow());
        Assert.IsFalse(gate.OnFrame());
        Assert.IsFalse(gate.OnFrame());
        Assert.IsFalse(gate.TryRevealNow());
    }

    [TestMethod]
    public void IdleFallback_AfterOneFrame_StillReveals()
    {
        var gate = new StartupRevealGate();
        Assert.IsFalse(gate.OnFrame());
        Assert.IsTrue(gate.TryRevealNow());
        Assert.IsFalse(gate.OnFrame());
    }
}
