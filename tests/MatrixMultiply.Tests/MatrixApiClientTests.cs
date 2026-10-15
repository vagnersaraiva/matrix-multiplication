using System.Net;
using System.Text;
using MatrixMultiply.Core;
using MatrixMultiply.Core.Api;

namespace MatrixMultiply.Tests;

public class MatrixApiClientTests
{
    [Fact]
    public async Task PostsInitWithoutACharsetOnContentType()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{"message":"Initialized matrices A & B with size 2 x 2"}"""));
        var client = CreateClient(handler, initial: 1, max: 1);

        var message = await client.InitializeAsync(2, CancellationToken.None);

        Assert.Equal("Initialized matrices A & B with size 2 x 2", message);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/api/matrix/init", request.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("application/json", request.ContentType);
        Assert.Null(request.CharSet);
        Assert.Equal("""{"n":2}""", request.Body);
    }

    [Fact]
    public async Task DoesNotSendABodyOnGetAndRetries429()
    {
        var calls = 0;
        var handler = new ScriptedHandler(request =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }

            var json = request.RequestUri!.AbsolutePath.Contains("column", StringComparison.Ordinal)
                ? """{"column":[9]}"""
                : """{"row":[4]}""";
            return Json(HttpStatusCode.OK, json);
        });
        var gate = new AdaptiveGate(4, 8);
        var client = CreateClient(handler, gate);

        var a = new int[1];
        var b = new int[1];
        await client.FetchIntoAsync(1, a, b, CancellationToken.None);

        Assert.Equal(4, a[0]);
        Assert.Equal(9, b[0]);
        Assert.Equal(2, gate.Limit);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.ContentType);
            Assert.Equal(string.Empty, request.Body);
        });
    }

    [Fact]
    public async Task ThrowsWhenTheMatrixIsDiscardedAndReleasesTheGate()
    {
        var handler = new ScriptedHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Matrix not found", Encoding.UTF8),
            };
            return response;
        });
        var gate = new AdaptiveGate(1, 1);
        var client = CreateClient(handler, gate);

        var error = await Assert.ThrowsAsync<MatrixDiscardedException>(() =>
            client.FetchIntoAsync(1, new int[1], new int[1], CancellationToken.None));

        Assert.Contains("Matrix not found", error.Message, StringComparison.Ordinal);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await gate.EnterAsync(timeout.Token);
        gate.ExitSuccess();
    }

    [Fact]
    public async Task DoesNotRetryABadInitRequest()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.UnsupportedMediaType, """{"message":"Unsupported"}"""));
        var client = CreateClient(handler, initial: 1, max: 1, maxAttempts: 4);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.InitializeAsync(2, CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    private static MatrixApiClient CreateClient(ScriptedHandler handler, int initial, int max, int maxAttempts = 4)
    {
        return CreateClient(handler, new AdaptiveGate(initial, max), maxAttempts);
    }

    private static MatrixApiClient CreateClient(ScriptedHandler handler, AdaptiveGate gate, int maxAttempts = 4)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/"),
        };
        return new MatrixApiClient(http, gate, _ => TimeSpan.Zero, maxAttempts);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _next;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> next)
        {
            _next = next;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests)
            {
                Requests.Add(new RecordedRequest(
                    request.Method,
                    request.RequestUri ?? new Uri("https://example.test/"),
                    request.Content?.Headers.ContentType?.MediaType,
                    request.Content?.Headers.ContentType?.CharSet,
                    body));
            }

            return _next(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ContentType, string? CharSet, string Body);
}
