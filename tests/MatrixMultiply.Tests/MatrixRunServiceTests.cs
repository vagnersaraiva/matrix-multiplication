using MatrixMultiply.Core;
using MatrixMultiply.Core.Hash;
using MatrixMultiply.Core.Orchestration;

namespace MatrixMultiply.Tests;

public class MatrixRunServiceTests
{
    [Fact]
    public async Task FetchesBeforeMultiplyingAndValidatesTheProductHash()
    {
        var api = new FakeApi();
        var service = new MatrixRunService(api, HashLayout.Int32LittleEndianRowMajor);

        var report = await service.ExecuteAsync(2, CancellationToken.None);

        Assert.Equal(["init:2", "fetch", "validate"], api.Calls.Select(call => call.Split(' ')[0]).ToArray());
        Assert.Equal(ResultHasher.Hash([1, 1, 1, 1], 2, HashLayout.Int32LittleEndianRowMajor), report.Hash);
        Assert.True(report.Succeeded);
        Assert.Equal(report.FetchMilliseconds + report.MultiplyMilliseconds + report.ValidateMilliseconds, report.TotalMilliseconds);
        Assert.StartsWith("Initialized matrices", report.InitMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestartsFromInitWhenTheMatrixIsDiscarded()
    {
        var api = new FakeApi { FetchFailures = 1, ValidateDiscards = 1 };
        var service = new MatrixRunService(api, HashLayout.JsonRows);

        var report = await service.ExecuteAsync(2, CancellationToken.None);

        Assert.Equal(
            ["init:2", "fetch", "init:2", "fetch", "validate", "init:2", "fetch", "validate"],
            api.Calls.Select(call => call.Split(' ')[0]).ToArray());
        Assert.True(report.Succeeded);
    }

    [Fact]
    public async Task ReportsARejectedHashWithoutRetrying()
    {
        var api = new FakeApi { ValidateResult = "Hash does not match" };
        var service = new MatrixRunService(api, HashLayout.JsonRows);

        var report = await service.ExecuteAsync(2, CancellationToken.None);

        Assert.False(report.Succeeded);
        Assert.Equal("Hash does not match", report.ValidateResult);
        Assert.Equal(1, api.Calls.Count(call => call.StartsWith("init:", StringComparison.Ordinal)));
    }

    private sealed class FakeApi : IMatrixApi
    {
        public List<string> Calls { get; } = [];

        public int FetchFailures { get; set; }

        public int ValidateDiscards { get; set; }

        public string ValidateResult { get; set; } = "Success";

        public Task<string> InitializeAsync(int n, CancellationToken cancellationToken)
        {
            Calls.Add($"init:{n}");
            return Task.FromResult($"Initialized matrices A & B with size {n} x {n}");
        }

        public Task FetchIntoAsync(int n, int[] aRowMajor, int[] bColumnMajor, CancellationToken cancellationToken)
        {
            Calls.Add("fetch");
            if (FetchFailures > 0)
            {
                FetchFailures--;
                throw new MatrixDiscardedException("Matrix not found");
            }

            Array.Fill(aRowMajor, 1);
            Array.Clear(bColumnMajor);
            for (var index = 0; index < n; index++)
            {
                bColumnMajor[(index * n) + index] = 1;
            }

            return Task.CompletedTask;
        }

        public Task<string> ValidateAsync(string hash, CancellationToken cancellationToken)
        {
            Calls.Add($"validate {hash}");
            if (ValidateDiscards > 0)
            {
                ValidateDiscards--;
                return Task.FromResult("Matrix not found");
            }

            return Task.FromResult(ValidateResult);
        }
    }
}
