namespace MatrixMultiply.Core.Hash;

public enum HashLayout
{
    /// <summary>
    /// Row-major integers joined by commas, UTF-8, with no trailing comma.
    /// This is the layout the validate endpoint accepts.
    /// </summary>
    CommaSeparated = 0,
    Int32LittleEndianRowMajor = 1,
    Int32BigEndianRowMajor = 2,
    Int32LittleEndianColumnMajor = 3,
    Int32BigEndianColumnMajor = 4,
    JsonRows = 5,
    CommaSeparatedRows = 6,
    SpaceSeparatedRows = 7,
}
