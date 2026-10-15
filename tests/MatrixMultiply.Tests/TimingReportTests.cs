using MatrixMultiply.Core.Orchestration;

namespace MatrixMultiply.Tests;

public class TimingReportTests
{
    [Fact]
    public void FormatsTheStepsFromTheInstructions()
    {
        var report = new RunReport(
            "Initialized matrices A & B with size 1000 x 1000",
            FetchMilliseconds: 157,
            MultiplyMilliseconds: 425,
            ValidateResult: "Success",
            ValidateMilliseconds: 1001,
            TotalMilliseconds: 1583,
            Hash: "abc",
            Succeeded: true);

        var text = TimingReport.Format(report).Replace("\r\n", "\n", StringComparison.Ordinal);
        var expected = """
            Init Message: Initialized matrices A & B with size 1000 x 1000

            Get A & B took
            157

            Multiply took
            425

            Validate Result
            Success

            Validate took
            1001

            Total took
            1583
            """.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(expected + "\n", text);
    }
}
