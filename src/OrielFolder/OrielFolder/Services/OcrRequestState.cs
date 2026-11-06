using System;
using System.Threading;

namespace OrielFolder.Services;

internal readonly record struct OcrRequestTicket(long Generation, CancellationToken CancellationToken);

internal sealed class OcrRequestState : IDisposable
{
    private long _generation;
    private CancellationTokenSource? _cancellation;

    public OcrRequestTicket Begin()
    {
        Invalidate();
        _cancellation = new CancellationTokenSource();
        return new OcrRequestTicket(_generation, _cancellation.Token);
    }

    public void Invalidate()
    {
        _generation++;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public bool IsCurrent(OcrRequestTicket ticket) =>
        ticket.Generation == _generation && !ticket.CancellationToken.IsCancellationRequested;

    public void Dispose() => Invalidate();
}
