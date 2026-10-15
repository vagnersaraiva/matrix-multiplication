namespace MatrixMultiply.Core;

public interface IMatrixApi
{
    Task<string> InitializeAsync(int n, CancellationToken cancellationToken);

    Task FetchIntoAsync(int n, int[] aRowMajor, int[] bColumnMajor, CancellationToken cancellationToken);

    Task<string> ValidateAsync(string hash, CancellationToken cancellationToken);
}
