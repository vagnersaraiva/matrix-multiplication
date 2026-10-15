namespace MatrixMultiply.Core;

public static class ValidateResults
{
    public static bool IsSuccess(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
        {
            return false;
        }

        if (result.Contains("not", StringComparison.OrdinalIgnoreCase)
            || result.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || result.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || result.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return result.Equals("true", StringComparison.OrdinalIgnoreCase)
            || result.Contains("success", StringComparison.OrdinalIgnoreCase)
            || result.Contains("match", StringComparison.OrdinalIgnoreCase)
            || result.Contains("valid", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDiscarded(string result)
    {
        return result.Contains("Matrix not found", StringComparison.OrdinalIgnoreCase);
    }
}
