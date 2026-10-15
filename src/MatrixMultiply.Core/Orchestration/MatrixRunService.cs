using System.Diagnostics;
using MatrixMultiply.Core.Hash;
using MatrixMultiply.Core.Multiply;

namespace MatrixMultiply.Core.Orchestration;

public sealed class MatrixRunService
{
    private const int MaxRestarts = 3;

    private readonly IMatrixApi _api;
    private readonly HashLayout _layout;

    public MatrixRunService(IMatrixApi api, HashLayout layout)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _layout = layout;
    }

    public async Task<RunReport> ExecuteAsync(int n, CancellationToken cancellationToken)
    {
        MatrixDiscardedException? discarded = null;
        for (var attempt = 1; attempt <= MaxRestarts; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(n, cancellationToken).ConfigureAwait(false);
            }
            catch (MatrixDiscardedException ex) when (attempt < MaxRestarts)
            {
                discarded = ex;
            }
        }

        throw discarded ?? new MatrixDiscardedException("The server discarded the matrices.");
    }

    private async Task<RunReport> ExecuteOnceAsync(int n, CancellationToken cancellationToken)
    {
        var initMessage = await _api.InitializeAsync(n, cancellationToken).ConfigureAwait(false);

        var a = new int[n * n];
        var b = new int[n * n];

        var fetch = Stopwatch.StartNew();
        await _api.FetchIntoAsync(n, a, b, cancellationToken).ConfigureAwait(false);
        fetch.Stop();

        var multiply = Stopwatch.StartNew();
        var product = MatrixMultiplier.Multiply(a, b, n);
        multiply.Stop();

        var hash = ResultHasher.Hash(product, n, _layout);

        var validate = Stopwatch.StartNew();
        var result = await _api.ValidateAsync(hash, cancellationToken).ConfigureAwait(false);
        validate.Stop();

        if (ValidateResults.IsDiscarded(result))
        {
            throw new MatrixDiscardedException(result);
        }

        var fetchMs = fetch.ElapsedMilliseconds;
        var multiplyMs = multiply.ElapsedMilliseconds;
        var validateMs = validate.ElapsedMilliseconds;
        return new RunReport(
            initMessage,
            fetchMs,
            multiplyMs,
            result,
            validateMs,
            fetchMs + multiplyMs + validateMs,
            hash,
            ValidateResults.IsSuccess(result));
    }
}
