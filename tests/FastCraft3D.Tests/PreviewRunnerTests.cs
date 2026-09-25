using System.Collections.Concurrent;
using FastCraft3D.Generators;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The preview shows the newest request and only the newest, however the builds finish.</summary>
public class PreviewRunnerTests
{
    /// <summary>
    /// Made with no synchronisation context, so results are shown on the thread that built them
    /// and a request's task is finished only once its result has been shown or dropped.
    /// </summary>
    private static PreviewRunner<int, int> Runner(Func<int, CancellationToken, int> build, ConcurrentQueue<int> shown, TimeSpan settle = default)
    {
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return new PreviewRunner<int, int>(build, (_, result) => shown.Enqueue(result), settle);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    [Fact]
    public async Task ASlowBuildThatFinishesAfterANewerOneIsNeverShown()
    {
        var shown = new ConcurrentQueue<int>();

        // The first ignores its cancellation on purpose, so it is the numbering that has to drop
        // it. Held on a gate rather than a sleep: under a busy test run a sleep did not reliably
        // keep it running until the second had been asked for.
        using var started = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var runner = Runner((n, _) =>
        {
            if (n == 1)
            {
                started.Set();
                gate.Wait();
            }

            return n;
        }, shown);

        var slow = runner.Request(1);
        started.Wait();

        await runner.Request(2);
        gate.Set();
        await slow;

        Assert.Equal([2], shown);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task ChangesInQuickSuccessionAreBuiltOnceTheyHaveSettled()
    {
        var shown = new ConcurrentQueue<int>();
        int builds = 0;
        var runner = Runner((n, _) =>
        {
            Interlocked.Increment(ref builds);
            return n;
        }, shown, TimeSpan.FromMilliseconds(100));

        var requests = Enumerable.Range(1, 5).Select(runner.Request).ToList();
        await Task.WhenAll(requests);

        Assert.Equal(1, builds);
        Assert.Equal([5], shown);
    }

    [Fact]
    public async Task ABuildThatThrowsIsReportedAndNotShown()
    {
        var shown = new ConcurrentQueue<int>();
        var runner = Runner((_, _) => throw new InvalidOperationException("broken"), shown);

        Exception? reported = null;
        runner.Failed += (_, ex) => reported = ex;

        await runner.Request(1);

        Assert.Empty(shown);
        Assert.Equal("broken", reported?.Message);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task NothingIsShownOnceTheRunnerIsStopped()
    {
        var shown = new ConcurrentQueue<int>();
        var runner = Runner((n, _) =>
        {
            Thread.Sleep(100);
            return n;
        }, shown);

        var pending = runner.Request(1);
        runner.Dispose();
        await pending;

        Assert.Empty(shown);
    }
}
