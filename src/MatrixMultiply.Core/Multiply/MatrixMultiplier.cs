using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace MatrixMultiply.Core.Multiply;

public static class MatrixMultiplier
{
    private const int RowTile = 64;
    private const int ColumnTile = 64;
    private const int KTile = 256;

    /// <summary>
    /// Computes C = A × B. <paramref name="aRowMajor"/> is row-major and
    /// <paramref name="bColumnMajor"/> stores each column contiguously.
    /// The product is returned row-major. Arithmetic wraps like unchecked int32.
    /// </summary>
    public static int[] Multiply(int[] aRowMajor, int[] bColumnMajor, int n)
    {
        ArgumentNullException.ThrowIfNull(aRowMajor);
        ArgumentNullException.ThrowIfNull(bColumnMajor);
        if (n < 1 || aRowMajor.Length != n * n || bColumnMajor.Length != n * n)
        {
            throw new ArgumentException("Both matrices must contain n*n integers.");
        }

        var product = new int[n * n];
        var rowTiles = (n + RowTile - 1) / RowTile;
        Parallel.For(0, rowTiles, tile =>
        {
            var i0 = tile * RowTile;
            var i1 = Math.Min(i0 + RowTile, n);
            for (var j0 = 0; j0 < n; j0 += ColumnTile)
            {
                var j1 = Math.Min(j0 + ColumnTile, n);
                for (var k0 = 0; k0 < n; k0 += KTile)
                {
                    var k1 = Math.Min(k0 + KTile, n);
                    MultiplyTile(aRowMajor, bColumnMajor, product, n, i0, i1, j0, j1, k0, k1);
                }
            }
        });

        return product;
    }

    private static void MultiplyTile(
        int[] a,
        int[] b,
        int[] c,
        int n,
        int i0,
        int i1,
        int j0,
        int j1,
        int k0,
        int k1)
    {
        var length = k1 - k0;
        for (var i = i0; i < i1; i++)
        {
            var aOffset = (i * n) + k0;
            var cOffset = i * n;
            for (var j = j0; j < j1; j++)
            {
                var partial = Dot(a, aOffset, b, (j * n) + k0, length);
                unchecked
                {
                    c[cOffset + j] += partial;
                }
            }
        }
    }

    private static int Dot(int[] left, int leftOffset, int[] right, int rightOffset, int length)
    {
        unchecked
        {
            var sum = 0;
            var k = 0;
            if (Avx2.IsSupported && length >= 8)
            {
                var acc = Vector256<int>.Zero;
                ref var leftRef = ref left[leftOffset];
                ref var rightRef = ref right[rightOffset];
                var vectorEnd = length - 7;
                for (; k < vectorEnd; k += 8)
                {
                    var leftVector = Vector256.LoadUnsafe(ref leftRef, (nuint)k);
                    var rightVector = Vector256.LoadUnsafe(ref rightRef, (nuint)k);
                    acc = Vector256.Add(acc, Vector256.Multiply(leftVector, rightVector));
                }

                sum = acc.GetElement(0)
                    + acc.GetElement(1)
                    + acc.GetElement(2)
                    + acc.GetElement(3)
                    + acc.GetElement(4)
                    + acc.GetElement(5)
                    + acc.GetElement(6)
                    + acc.GetElement(7);
            }

            for (; k < length; k++)
            {
                sum += left[leftOffset + k] * right[rightOffset + k];
            }

            return sum;
        }
    }
}
