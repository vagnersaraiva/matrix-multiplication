using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MatrixMultiply.Core.Api;

public sealed class MatrixApiClient : IMatrixApi
{
    private readonly HttpClient _http;
    private readonly AdaptiveGate _gate;
    private readonly Func<int, TimeSpan> _retryDelay;
    private readonly int _maxAttempts;

    public MatrixApiClient(HttpClient http, int initialConcurrency, int maxConcurrency, Func<int, TimeSpan>? retryDelay = null, int maxAttempts = 8)
        : this(http, new AdaptiveGate(initialConcurrency, maxConcurrency), retryDelay, maxAttempts)
    {
    }

    internal MatrixApiClient(HttpClient http, AdaptiveGate gate, Func<int, TimeSpan>? retryDelay = null, int maxAttempts = 8)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _retryDelay = retryDelay ?? DefaultDelay;
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        _maxAttempts = maxAttempts;
    }

    public async Task<string> InitializeAsync(int n, CancellationToken cancellationToken)
    {
        if (n is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "n must be from 1 to 10000.");
        }

        var payload = Encoding.UTF8.GetBytes($"{{\"n\":{n}}}");
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            byte[] body;
            HttpStatusCode status;
            try
            {
                (status, body) = await PostAsync("api/matrix/init", payload, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < _maxAttempts)
            {
                await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if ((int)status is >= 200 and <= 299)
            {
                return ApiJson.ReadStringProperty(body, "message");
            }

            if (ShouldRetry(status) && attempt < _maxAttempts)
            {
                await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw new HttpRequestException($"Init failed with {(int)status}: {Decode(body)}");
        }

        throw new HttpRequestException("Init failed.");
    }

    public async Task FetchIntoAsync(int n, int[] aRowMajor, int[] bColumnMajor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aRowMajor);
        ArgumentNullException.ThrowIfNull(bColumnMajor);
        if (n < 1 || aRowMajor.Length != n * n || bColumnMajor.Length != n * n)
        {
            throw new ArgumentException("Both buffers must contain n*n integers.");
        }

        var jobs = new FetchJob[n * 2];
        for (var index = 0; index < n; index++)
        {
            jobs[index * 2] = new FetchJob(IsColumn: false, index);
            jobs[(index * 2) + 1] = new FetchJob(IsColumn: true, index);
        }

        var discarded = new DiscardedSlot();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await Parallel.ForEachAsync(
                jobs,
                new ParallelOptions
                {
                    CancellationToken = linked.Token,
                    MaxDegreeOfParallelism = _gate.Max,
                },
                async (job, token) =>
                {
                    var url = job.IsColumn
                        ? $"api/matrix/B/column/{job.Index}"
                        : $"api/matrix/A/row/{job.Index}";
                    var destination = job.IsColumn
                        ? bColumnMajor.AsMemory(job.Index * n, n)
                        : aRowMajor.AsMemory(job.Index * n, n);

                    try
                    {
                        await DownloadVectorAsync(url, destination, token).ConfigureAwait(false);
                    }
                    catch (MatrixDiscardedException ex)
                    {
                        Interlocked.CompareExchange(ref discarded.Error, ex, null);
                        try
                        {
                            linked.Cancel();
                        }
                        catch (ObjectDisposedException)
                        {
                        }

                        throw;
                    }
                }).ConfigureAwait(false);
        }
        catch (Exception) when (Volatile.Read(ref discarded.Error) is { } error)
        {
            throw error;
        }
    }

    public async Task<string> ValidateAsync(string hash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(hash);
        var payload = Encoding.UTF8.GetBytes($"{{\"hash\":\"{hash}\"}}");

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            byte[] body;
            HttpStatusCode status;
            try
            {
                (status, body) = await PostAsync("api/matrix/validate", payload, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < _maxAttempts)
            {
                await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (IsDiscarded(status, body))
            {
                throw new MatrixDiscardedException(Decode(body));
            }

            if ((int)status is >= 200 and <= 299)
            {
                return ApiJson.ReadStringProperty(body, "result");
            }

            if (ShouldRetry(status) && attempt < _maxAttempts)
            {
                await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw new HttpRequestException($"Validate failed with {(int)status}: {Decode(body)}");
        }

        throw new HttpRequestException("Validate failed.");
    }

    private async Task<(HttpStatusCode Status, byte[] Body)> PostAsync(string relativeUrl, byte[] payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = CreateJson(payload),
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    public static HttpClient CreateHttpClient(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 1,
            EnableMultipleHttp2Connections = false,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Timeout = TimeSpan.FromMinutes(3),
        };
    }

    private async Task DownloadVectorAsync(string relativeUrl, Memory<int> destination, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
            FetchDecision decision;
            try
            {
                decision = await TryOnceAsync(relativeUrl, destination, cancellationToken).ConfigureAwait(false);
                switch (decision)
                {
                    case FetchDecision.Success:
                        _gate.ExitSuccess();
                        return;
                    case FetchDecision.TooManyRequests:
                        _gate.ExitTooManyRequests();
                        break;
                    default:
                        _gate.ExitFailure();
                        break;
                }
            }
            catch (MatrixDiscardedException)
            {
                _gate.ExitFailure();
                throw;
            }
            catch (OperationCanceledException)
            {
                _gate.ExitFailure();
                throw;
            }
            catch (HttpRequestException) when (attempt < _maxAttempts)
            {
                _gate.ExitFailure();
            }
            catch
            {
                _gate.ExitFailure();
                throw;
            }

            if (attempt == _maxAttempts)
            {
                break;
            }

            await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
        }

        throw new HttpRequestException($"GET {relativeUrl} failed after {_maxAttempts} attempts.");
    }

    private async Task<FetchDecision> TryOnceAsync(string relativeUrl, Memory<int> destination, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return FetchDecision.TooManyRequests;
        }

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (IsDiscarded(response.StatusCode, payload))
        {
            throw new MatrixDiscardedException(Decode(payload));
        }

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
        {
            return FetchDecision.Retry;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GET {relativeUrl} failed with {(int)response.StatusCode}: {Decode(payload)}");
        }

        JsonIntArrayParser.Fill(payload, destination.Span);
        return FetchDecision.Success;
    }

    private static bool IsDiscarded(HttpStatusCode status, ReadOnlySpan<byte> body)
    {
        return status == HttpStatusCode.NotFound || body.IndexOf("Matrix not found"u8) >= 0;
    }

    private static bool ShouldRetry(HttpStatusCode status)
    {
        return status == HttpStatusCode.TooManyRequests
            || status == HttpStatusCode.RequestTimeout
            || (int)status >= 500;
    }

    private static ByteArrayContent CreateJson(byte[] payload)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static string Decode(ReadOnlySpan<byte> body)
    {
        return Encoding.UTF8.GetString(body);
    }

    private Task DelayAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = _retryDelay(attempt);
        return delay > TimeSpan.Zero
            ? Task.Delay(delay, cancellationToken)
            : Task.CompletedTask;
    }

    private static TimeSpan DefaultDelay(int attempt)
    {
        var upper = Math.Min(400, 50 * attempt);
        return TimeSpan.FromMilliseconds(Random.Shared.Next(20, upper + 1));
    }

    private readonly record struct FetchJob(bool IsColumn, int Index);

    private enum FetchDecision
    {
        Success,
        TooManyRequests,
        Retry,
    }

    private sealed class DiscardedSlot
    {
        public MatrixDiscardedException? Error;
    }
}
