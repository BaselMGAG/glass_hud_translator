using GlassHudTranslator.Core.Capture;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Two threads genuinely reach these objects, and until now nothing said so.
///
/// <para>
/// The poll thread sits inside <c>Offer</c> and <c>Confirm</c> for minutes at a time. The UI thread
/// arrives at <c>Reset</c> and <c>Retune</c> through a mode change, at <c>NowShowing</c> through a
/// key press, at <c>ForgetWhatIsOnScreen</c> through a snip, and at <c>Cadence</c> and
/// <c>Requests</c> through the Diagnostics tab. That last one is not merely a torn read:
/// <c>Cadence</c> enumerates a <see cref="Queue{T}"/> that the poll thread is enqueuing into, which
/// throws — on the UI thread, from a tab somebody opened to find out why the app was misbehaving.
/// </para>
///
/// <para>
/// <b>These are regression tests, not proofs.</b> A green run does not establish that the locking is
/// correct; it establishes that the specific collisions which are reachable today do not throw. What
/// makes them worth having is that they fail reliably against unsynchronised code, because the
/// collections involved detect concurrent mutation and say so.
/// </para>
/// </summary>
public class GateThreadSafetyTests
{
    private static readonly TimeSpan Long = TimeSpan.FromMilliseconds(250);

    private static FrameSignature Signature(int n)
    {
        var b = new FrameBuilder(200, 80, Rgb.BoxDark);
        for (var i = 0; i < n % 7 + 1; i++) b.Rect(10 + i * 22, 20, 18, 14, Rgb.TextWhite);
        return FrameSignature.Compute(b.Build());
    }

    [Fact]
    public void EverythingPublicOnTheGateIsSafeToCallFromAnotherThread()
    {
        var gate = new FrameSettleGate();
        using var stop = new CancellationTokenSource(Long);
        Exception? failure = null;

        var poller = new Thread(() =>
        {
            try
            {
                var n = 0;
                while (!stop.IsCancellationRequested)
                {
                    if (gate.Offer(Signature(n++)) == FrameVerdict.Read)
                        gate.Confirm($"a line that reads about the same {n / 3}", false);
                }
            }
            catch (Exception e) { failure ??= e; }
        });

        var meddler = new Thread(() =>
        {
            try
            {
                var n = 0;
                while (!stop.IsCancellationRequested)
                {
                    // Everything the UI thread can actually do to a running gate.
                    gate.NowShowing(Signature(n++));
                    gate.Retune(new SettleOptions { PollsPerSecond = 4 });
                    _ = gate.SceneMovement;
                    _ = gate.GaveUp;
                    _ = gate.StillTicks;
                    gate.Reset();
                }
            }
            catch (Exception e) { failure ??= e; }
        });

        poller.Start();
        meddler.Start();
        poller.Join();
        meddler.Join();

        Assert.Null(failure);
    }

    [Fact]
    public void CadenceAndRequestsAreSafeToReadWhileARunIsGoing()
    {
        // The live crash: SettingsWindow reads WatchStats -> Cadence, which does _gaps.Order(),
        // while the poll thread is inside Translated() enqueuing into that same queue.
        var session = new WatchSession(WatchPacing.For(WatchMode.Dialogue));
        session.Start();

        using var stop = new CancellationTokenSource(Long);
        Exception? failure = null;
        var counted = 0;

        var poller = new Thread(() =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    session.Translated();
                    Interlocked.Increment(ref counted);
                }
            }
            catch (Exception e) { failure ??= e; }
        });

        var diagnostics = new Thread(() =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    _ = session.Cadence;
                    _ = session.Requests;
                    _ = session.OutrunningTheFloor;
                }
            }
            catch (Exception e) { failure ??= e; }
        });

        poller.Start();
        diagnostics.Start();
        poller.Join();
        diagnostics.Join();

        Assert.Null(failure);

        // And not one increment lost, because this number is what the session cap is measured
        // against and what its own message reports to the user.
        Assert.Equal(counted, session.Requests);
    }
}
