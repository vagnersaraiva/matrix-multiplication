namespace MatrixMultiply.Core;

public sealed class MatrixDiscardedException : Exception
{
    public MatrixDiscardedException(string message)
        : base(message)
    {
    }

    public MatrixDiscardedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
