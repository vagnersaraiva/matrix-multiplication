using System.Security.Cryptography;
using System.Text;
using MatrixMultiply.Core.Hash;

namespace MatrixMultiply.Tests;

public class ResultHasherTests
{
    private static readonly int[] Product = [1, 2, 3, 4];

    [Fact]
    public void HashesLittleEndianRowMajorBytes()
    {
        byte[] bytes = [1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0];

        Assert.Equal(Base64Md5(bytes), ResultHasher.Hash(Product, 2, HashLayout.Int32LittleEndianRowMajor));
    }

    [Fact]
    public void HashesBigEndianRowMajorBytes()
    {
        byte[] bytes = [0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4];

        Assert.Equal(Base64Md5(bytes), ResultHasher.Hash(Product, 2, HashLayout.Int32BigEndianRowMajor));
    }

    [Fact]
    public void HashesLittleEndianColumnMajorBytes()
    {
        byte[] bytes = [1, 0, 0, 0, 3, 0, 0, 0, 2, 0, 0, 0, 4, 0, 0, 0];

        Assert.Equal(Base64Md5(bytes), ResultHasher.Hash(Product, 2, HashLayout.Int32LittleEndianColumnMajor));
    }

    [Fact]
    public void HashesCompactJsonRows()
    {
        Assert.Equal(
            Base64Md5("[[1,2],[3,4]]"u8),
            ResultHasher.Hash(Product, 2, HashLayout.JsonRows));
    }

    [Fact]
    public void HashesTheLayoutAcceptedByTheApi()
    {
        Assert.Equal(
            Base64Md5(Encoding.UTF8.GetBytes("1,2,3,4")),
            ResultHasher.Hash(Product, 2, HashLayout.CommaSeparated));
        Assert.Equal(
            "rMCX1kfIj0RT/50Fx0CGzw==",
            ResultHasher.Hash([5148, 1460, 9396, 2275], 2, HashLayout.CommaSeparated));
    }

    [Fact]
    public void MatchesStringJoinAcrossHashChunks()
    {
        const int n = 100;
        var values = new int[n * n];
        var random = new Random(7);
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = random.Next(-20_000, 20_000);
        }

        var text = string.Join(',', values);
        Assert.Equal(Base64Md5(Encoding.UTF8.GetBytes(text)), ResultHasher.Hash(values, n, HashLayout.CommaSeparated));
    }

    [Fact]
    public void HashesDelimitedRows()
    {
        Assert.Equal(
            Base64Md5(Encoding.UTF8.GetBytes("1,2\n3,4")),
            ResultHasher.Hash(Product, 2, HashLayout.CommaSeparatedRows));
        Assert.Equal(
            Base64Md5(Encoding.UTF8.GetBytes("1 2\n3 4")),
            ResultHasher.Hash(Product, 2, HashLayout.SpaceSeparatedRows));
    }

    [Fact]
    public void EncodesNegativeLittleEndianValues()
    {
        byte[] bytes = [254, 255, 255, 255];

        Assert.Equal(Base64Md5(bytes), ResultHasher.Hash([-2], 1, HashLayout.Int32LittleEndianRowMajor));
    }

    private static string Base64Md5(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(MD5.HashData(bytes));
    }
}
