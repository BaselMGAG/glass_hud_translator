namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// Per-provider requests-per-minute limiter.
///
/// <para>
/// RPM, not RPD, is the real constraint: cutscene dialogue advances every 3-5 seconds, so 12-20
/// lines a minute, which sits at or above Gemini's free ceiling on its own. Failing over on the
/// bucket rather than only on daily exhaustion is what makes Groq a second lane instead of a
/// reserve tank, and the two lanes together clear the densest cutscene (brief 5).
/// </para>
///
/// <para>
/// Configured limits sit deliberately under the published ones - the published numbers are not
/// guaranteed and are no longer even documented per model.
/// </para>
/// </summary>
public sealed class TokenBucket
{
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();

    private double _tokens;
    private long _lastRefillTicks;

    public TokenBucket(int perMinute, TimeProvider? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(perMinute, 1);

        _capacity = perMinute;
        _refillPerSecond = perMinute / 60.0;
        _clock = clock ?? TimeProvider.System;
        _tokens = perMinute;
        _lastRefillTicks = _clock.GetTimestamp();
    }

    public int PerMinute => (int)_capacity;

    public double Available
    {
        get
        {
            lock (_gate)
            {
                Refill();
                return _tokens;
            }
        }
    }

    public bool TryTake()
    {
        lock (_gate)
        {
            Refill();
            if (_tokens < 1) return false;

            _tokens -= 1;
            return true;
        }
    }

    private void Refill()
    {
        var now = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedTime(_lastRefillTicks, now).TotalSeconds;
        if (elapsed <= 0) return;

        _lastRefillTicks = now;
        _tokens = Math.Min(_capacity, _tokens + elapsed * _refillPerSecond);
    }
}
