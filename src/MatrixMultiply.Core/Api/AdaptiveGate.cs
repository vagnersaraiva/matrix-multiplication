namespace MatrixMultiply.Core.Api;

/// <summary>
/// Limits in-flight HTTP calls and adjusts that limit with AIMD:
/// every <see cref="SuccessWindow"/> successes raise it by <see cref="IncreaseStep"/>,
/// and a 429 cuts it in half.
/// </summary>
internal sealed class AdaptiveGate
{
    internal const int SuccessWindow = 16;
    internal const int IncreaseStep = 4;

    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _sync = new();
    private readonly int _max;
    private int _limit;
    private int _inUse;
    private int _waiting;
    private int _successes;

    public AdaptiveGate(int initial, int max)
    {
        if (initial < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(initial), "Initial concurrency must be at least 1.");
        }

        if (max < initial)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Max concurrency must be greater than or equal to the initial value.");
        }

        _limit = initial;
        _max = max;
    }

    public int Limit => Volatile.Read(ref _limit);

    public int Max => _max;

    public async Task EnterAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_sync)
            {
                if (_inUse < _limit)
                {
                    _inUse++;
                    return;
                }

                _waiting++;
            }

            try
            {
                await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_sync)
                {
                    _waiting--;
                }

                throw;
            }

            lock (_sync)
            {
                _waiting--;
            }
        }
    }

    public void ExitSuccess() => Release(tooManyRequests: false, success: true);

    public void ExitTooManyRequests() => Release(tooManyRequests: true, success: false);

    public void ExitFailure() => Release(tooManyRequests: false, success: false);

    private void Release(bool tooManyRequests, bool success)
    {
        lock (_sync)
        {
            if (_inUse == 0)
            {
                throw new InvalidOperationException("Adaptive gate was released more times than it was entered.");
            }

            _inUse--;

            if (tooManyRequests)
            {
                _limit = Math.Max(1, _limit / 2);
                _successes = 0;
            }
            else if (success)
            {
                _successes++;
                if (_successes >= SuccessWindow && _limit < _max)
                {
                    _limit = Math.Min(_max, _limit + IncreaseStep);
                    _successes = 0;
                }
            }

            if (_waiting > 0 && _inUse < _limit)
            {
                _signal.Release(Math.Min(_waiting, _limit - _inUse));
            }
        }
    }
}
