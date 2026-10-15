using MatrixMultiply.Core;

namespace MatrixMultiply.Tests;

public class ValidateResultsTests
{
    [Theory]
    [InlineData("Success")]
    [InlineData("hash matched")]
    [InlineData("true")]
    [InlineData("valid")]
    public void AcceptsPositiveResults(string result)
    {
        Assert.True(ValidateResults.IsSuccess(result));
    }

    [Theory]
    [InlineData("Hash does not match")]
    [InlineData("failure")]
    [InlineData("invalid")]
    [InlineData("")]
    public void RejectsNegativeResults(string result)
    {
        Assert.False(ValidateResults.IsSuccess(result));
    }

    [Fact]
    public void DetectsADiscardedMatrix()
    {
        Assert.True(ValidateResults.IsDiscarded("Matrix not found"));
        Assert.False(ValidateResults.IsDiscarded("Success"));
    }
}
