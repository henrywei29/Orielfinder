using OrielFolder.Services;

namespace OrielFolder.Tests.Services;

[TestClass]
public class OcrRequestStateTests
{
    [TestMethod]
    public void ChangingSourceOrPageCancelsWorkAndRejectsLateCompletion()
    {
        using var state = new OcrRequestState();
        var old = state.Begin();
        state.Invalidate();
        Assert.IsTrue(old.CancellationToken.IsCancellationRequested);
        Assert.IsFalse(state.IsCurrent(old));
    }

    [TestMethod]
    public void StartingAgainOnlyAcceptsTheLatestRequest()
    {
        using var state = new OcrRequestState();
        var first = state.Begin();
        var second = state.Begin();
        Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
        Assert.IsFalse(state.IsCurrent(first));
        Assert.IsTrue(state.IsCurrent(second));
    }

    [TestMethod]
    public void UnloadingCancelsTheCurrentRequest()
    {
        var state = new OcrRequestState();
        var current = state.Begin();
        state.Dispose();
        Assert.IsTrue(current.CancellationToken.IsCancellationRequested);
        Assert.IsFalse(state.IsCurrent(current));
    }
}
