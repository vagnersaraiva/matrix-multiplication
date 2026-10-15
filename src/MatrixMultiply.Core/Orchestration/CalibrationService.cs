using MatrixMultiply.Core.Hash;
using MatrixMultiply.Core.Multiply;

namespace MatrixMultiply.Core.Orchestration;

public sealed class CalibrationService
{
    private readonly IMatrixApi _api;

    public CalibrationService(IMatrixApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<HashLayout?> FindLayoutAsync(int n, Action<string> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);
        var (product, initMessage) = await LoadProductAsync(n, cancellationToken).ConfigureAwait(false);
        log($"Init Message: {initMessage}");

        foreach (var layout in Enum.GetValues<HashLayout>())
        {
            var hash = ResultHasher.Hash(product, n, layout);
            var result = await ValidateOrReloadAsync(n, layout, hash, log, cancellationToken).ConfigureAwait(false);
            if (result.Product is not null)
            {
                product = result.Product;
                hash = ResultHasher.Hash(product, n, layout);
            }

            log($"{layout}: {result.ValidateResult}");
            log($"hash: {hash}");
            if (ValidateResults.IsSuccess(result.ValidateResult))
            {
                return layout;
            }
        }

        return null;
    }

    private async Task<ProbeResult> ValidateOrReloadAsync(
        int n,
        HashLayout layout,
        string hash,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _api.ValidateAsync(hash, cancellationToken).ConfigureAwait(false);
            if (!ValidateResults.IsDiscarded(result))
            {
                return new ProbeResult(result, Product: null);
            }

            log($"{layout}: server discarded the matrices; reloading.");
        }
        catch (MatrixDiscardedException ex)
        {
            log($"{layout}: {ex.Message}; reloading.");
        }

        var (product, _) = await LoadProductAsync(n, cancellationToken).ConfigureAwait(false);
        hash = ResultHasher.Hash(product, n, layout);
        var retried = await _api.ValidateAsync(hash, cancellationToken).ConfigureAwait(false);
        return new ProbeResult(retried, product);
    }

    private async Task<(int[] Product, string InitMessage)> LoadProductAsync(int n, CancellationToken cancellationToken)
    {
        var initMessage = await _api.InitializeAsync(n, cancellationToken).ConfigureAwait(false);
        var a = new int[n * n];
        var b = new int[n * n];
        await _api.FetchIntoAsync(n, a, b, cancellationToken).ConfigureAwait(false);
        return (MatrixMultiplier.Multiply(a, b, n), initMessage);
    }

    private readonly record struct ProbeResult(string ValidateResult, int[]? Product);
}
