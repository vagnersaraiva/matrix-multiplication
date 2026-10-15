using System.Text.Json;

namespace MatrixMultiply.Core.Api;

internal static class JsonIntArrayParser
{
    public static void Fill(ReadOnlySpan<byte> utf8, Span<int> destination)
    {
        var reader = new Utf8JsonReader(utf8, isFinalBlock: true, state: default);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                break;
            }
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new FormatException("JSON array not found.");
        }

        var index = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new FormatException($"Expected a number at index {index}.");
            }

            if ((uint)index >= (uint)destination.Length)
            {
                throw new FormatException("JSON array is longer than the destination.");
            }

            destination[index++] = reader.GetInt32();
        }

        if (index != destination.Length)
        {
            throw new FormatException($"Expected {destination.Length} integers but received {index}.");
        }
    }
}
