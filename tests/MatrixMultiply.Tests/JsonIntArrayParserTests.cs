using System.Text;
using MatrixMultiply.Core.Api;

namespace MatrixMultiply.Tests;

public class JsonIntArrayParserTests
{
    [Fact]
    public void FillsDestinationFromRowPayload()
    {
        var destination = new int[3];

        JsonIntArrayParser.Fill("{\"row\":[80,41,-7]}"u8, destination);

        Assert.Equal(new[] { 80, 41, -7 }, destination);
    }

    [Fact]
    public void FillsDestinationFromColumnPayloadWithSpaces()
    {
        var destination = new int[2];

        JsonIntArrayParser.Fill(Encoding.UTF8.GetBytes("{ \"column\" : [ 1, 2 ] }"), destination);

        Assert.Equal(new[] { 1, 2 }, destination);
    }

    [Fact]
    public void RejectsAShortArray()
    {
        var destination = new int[3];

        var error = Assert.Throws<FormatException>(() => JsonIntArrayParser.Fill("{\"row\":[1,2]}"u8, destination));

        Assert.Contains("3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsALongArray()
    {
        var destination = new int[1];

        Assert.Throws<FormatException>(() => JsonIntArrayParser.Fill("{\"row\":[1,2]}"u8, destination));
    }
}
