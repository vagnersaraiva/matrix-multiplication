using System.Text;
using System.Text.Json;

namespace MatrixMultiply.Core.Api;

internal static class ApiJson
{
    public static string ReadStringProperty(ReadOnlySpan<byte> utf8, string propertyName)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8, isFinalBlock: true, state: default);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals(propertyName))
                {
                    if (!reader.Read())
                    {
                        break;
                    }

                    return reader.TokenType == JsonTokenType.String
                        ? reader.GetString() ?? string.Empty
                        : Encoding.UTF8.GetString(reader.ValueSpan);
                }
            }
        }
        catch (JsonException)
        {
            // Fall through and return the raw body.
        }

        return Encoding.UTF8.GetString(utf8);
    }
}
