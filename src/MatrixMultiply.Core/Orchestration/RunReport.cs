namespace MatrixMultiply.Core.Orchestration;

public sealed record RunReport(
    string InitMessage,
    long FetchMilliseconds,
    long MultiplyMilliseconds,
    string ValidateResult,
    long ValidateMilliseconds,
    long TotalMilliseconds,
    string Hash,
    bool Succeeded);
