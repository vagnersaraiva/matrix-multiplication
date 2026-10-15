using MatrixMultiply.Core.Multiply;

namespace MatrixMultiply.Tests;

public class MatrixMultiplierTests
{
    [Fact]
    public void MultipliesTwoByTwo()
    {
        var a = new[] { 1, 2, 3, 4 };
        var bColumns = new[] { 5, 7, 6, 8 };

        var product = MatrixMultiplier.Multiply(a, bColumns, 2);

        Assert.Equal(new[] { 19, 22, 43, 50 }, product);
    }

    [Fact]
    public void WrapsInt32LikeUncheckedArithmetic()
    {
        var product = MatrixMultiplier.Multiply([int.MaxValue], [2], 1);

        Assert.Equal(new[] { -2 }, product);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(17)]
    [InlineData(65)]
    public void MatchesScalarReference(int n)
    {
        var random = new Random(n);
        var a = new int[n * n];
        var bColumns = new int[n * n];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = random.Next(int.MinValue, int.MaxValue);
            bColumns[i] = random.Next(int.MinValue, int.MaxValue);
        }

        var actual = MatrixMultiplier.Multiply(a, bColumns, n);
        var expected = Scalar(a, bColumns, n);

        Assert.Equal(expected, actual);
    }

    private static int[] Scalar(int[] a, int[] bColumns, int n)
    {
        var product = new int[n * n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                var sum = 0;
                for (var k = 0; k < n; k++)
                {
                    unchecked
                    {
                        sum += a[(i * n) + k] * bColumns[(j * n) + k];
                    }
                }

                product[(i * n) + j] = sum;
            }
        }

        return product;
    }
}
