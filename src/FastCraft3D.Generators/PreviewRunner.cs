namespace FastCraft3D.Generators;

/// <summary>
/// Builds a preview off the UI thread, once the typing has settled, and only ever shows the
/// newest.
///
/// The panels built before this rebuilt on the UI thread at every keystroke; a sixty-tooth gear
/// frame is a quarter of a second, and holding a key down in its Teeth box froze the window.
/// What this does instead:
/// - waits a moment after the last change before starting;
/// - cancels the build still running when a new change comes in;
/// - builds on the thread pool, and hands the result back on the thread that made the runner;
/// - numbers every request, so a slow old build that finishes late is dropped rather than
///   overwriting the newer one already shown.
/// </summary>
/// <param name="build">The work. Handed a token it should check between steps.</param>
/// <param name="show">Given each result that is still the newest, on the runner's own thread.</param>
/// <param name="settle">How long to wait after a change. Zero starts at once.</param>
/// <param name="post">
/// How a result gets back to the thread that shows it. The panel hands its own dispatcher's; left
/// out, the synchronisation context the runner was made on. That alone was tried first, and a
/// panel opened from a command run outside the dispatcher's loop found none, built its previews
/// and never showed them.
/// </param>
public sealed class PreviewRunner<TIn, TOut>(
    Func<TIn, CancellationToken, TOut> build,
    Action<TIn, TOut> show,
    TimeSpan settle,
    Action<Action>? post = null) : IDisposable
{
    private readonly SynchronizationContext? home = SynchronizationContext.Current;
    private CancellationTokenSource? running;
    private long latest;
    private bool disposed;

    /// <summary>Whether a build has been asked for and its result has not yet been shown.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Raised on the runner's own thread when <see cref="IsBusy"/> changes.</summary>
    public event Action<bool>? BusyChanged;

    /// <summary>Raised on the runner's own thread when a build throws, with what it threw.</summary>
    public event Action<TIn, Exception>? Failed;

    /// <summary>Asks for a build of <paramref name="input"/>, replacing whatever was asked before.</summary>
    public Task Request(TIn input)
    {
        if (disposed) return Task.CompletedTask;

        long ticket = Interlocked.Increment(ref latest);

        running?.Cancel();
        running?.Dispose();
        var cancel = running = new CancellationTokenSource();
        var token = cancel.Token;

        SetBusy(true);

        return Task.Run(async () =>
        {
            try
            {
                if (settle > TimeSpan.Zero) await Task.Delay(settle, token);
                token.ThrowIfCancellationRequested();

                var result = build(input, token);
                Post(() =>
                {
                    if (ticket != Interlocked.Read(ref latest) || disposed) return;
                    show(input, result);
                    SetBusy(false);
                });
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer request, which will say when it is done.
            }
            catch (Exception ex)
            {
                Post(() =>
                {
                    if (ticket != Interlocked.Read(ref latest) || disposed) return;
                    SetBusy(false);
                    Failed?.Invoke(input, ex);
                });
            }
        }, CancellationToken.None);
    }

    /// <summary>Stops whatever is running; nothing more is shown after this.</summary>
    public void Dispose()
    {
        disposed = true;
        Interlocked.Increment(ref latest);
        running?.Cancel();
        running?.Dispose();
        running = null;
        IsBusy = false;
    }

    private void SetBusy(bool busy)
    {
        if (IsBusy == busy) return;
        IsBusy = busy;
        BusyChanged?.Invoke(busy);
    }

    private void Post(Action action)
    {
        if (post is not null) post(action);
        else if (home is null) action();
        else home.Post(_ => action(), null);
    }
}
