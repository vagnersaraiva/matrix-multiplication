using System.Buffers;
using System.Buffers.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace MatrixMultiply.Core.Hash;

public static class ResultHasher
{
    /// <summary>
    /// Layout accepted by the validate endpoint. Updated after calibration against the live API.
    /// </summary>
    public const HashLayout DefaultLayout = HashLayout.CommaSeparated;

    public static string Hash(ReadOnlySpan<int> rowMajorProduct, int n, HashLayout layout)
    {
        if (n < 1 || rowMajorProduct.Length != n * n)
        {
            throw new ArgumentException("Product length must be n*n.", nameof(rowMajorProduct));
        }

        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        Append(incremental, rowMajorProduct, n, layout);
        Span<byte> hash = stackalloc byte[16];
        var written = incremental.GetHashAndReset(hash);
        return Convert.ToBase64String(hash[..written]);
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<int> rowMajor, int n, HashLayout layout)
    {
        switch (layout)
        {
            case HashLayout.CommaSeparated:
                AppendFlat(hash, rowMajor, (byte)',');
                break;
            case HashLayout.Int32LittleEndianRowMajor:
                AppendBinary(hash, rowMajor, littleEndian: true);
                break;
            case HashLayout.Int32BigEndianRowMajor:
                AppendBinary(hash, rowMajor, littleEndian: false);
                break;
            case HashLayout.Int32LittleEndianColumnMajor:
                AppendColumnMajor(hash, rowMajor, n, littleEndian: true);
                break;
            case HashLayout.Int32BigEndianColumnMajor:
                AppendColumnMajor(hash, rowMajor, n, littleEndian: false);
                break;
            case HashLayout.JsonRows:
                AppendJsonRows(hash, rowMajor, n);
                break;
            case HashLayout.CommaSeparatedRows:
                AppendDelimited(hash, rowMajor, n, (byte)',');
                break;
            case HashLayout.SpaceSeparatedRows:
                AppendDelimited(hash, rowMajor, n, (byte)' ');
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown hash layout.");
        }
    }

    private static void AppendBinary(IncrementalHash hash, ReadOnlySpan<int> values, bool littleEndian)
    {
        if (littleEndian && BitConverter.IsLittleEndian)
        {
            hash.AppendData(MemoryMarshal.AsBytes(values));
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(values.Length * 4);
        try
        {
            var span = rented.AsSpan(0, values.Length * 4);
            for (var i = 0; i < values.Length; i++)
            {
                var slot = span.Slice(i * 4, 4);
                if (littleEndian)
                {
                    BinaryPrimitivesWriteLe(slot, values[i]);
                }
                else
                {
                    BinaryPrimitivesWriteBe(slot, values[i]);
                }
            }

            hash.AppendData(span);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void AppendColumnMajor(IncrementalHash hash, ReadOnlySpan<int> rowMajor, int n, bool littleEndian)
    {
        var rented = ArrayPool<byte>.Shared.Rent(n * 4);
        try
        {
            var bytes = rented.AsSpan(0, n * 4);
            for (var column = 0; column < n; column++)
            {
                for (var row = 0; row < n; row++)
                {
                    var slot = bytes.Slice(row * 4, 4);
                    var value = rowMajor[(row * n) + column];
                    if (littleEndian)
                    {
                        BinaryPrimitivesWriteLe(slot, value);
                    }
                    else
                    {
                        BinaryPrimitivesWriteBe(slot, value);
                    }
                }

                hash.AppendData(bytes);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void AppendJsonRows(IncrementalHash hash, ReadOnlySpan<int> rowMajor, int n)
    {
        using var writer = new HashBufferWriter(hash);
        using var json = new Utf8JsonWriter(writer);
        json.WriteStartArray();
        for (var row = 0; row < n; row++)
        {
            json.WriteStartArray();
            for (var column = 0; column < n; column++)
            {
                json.WriteNumberValue(rowMajor[(row * n) + column]);
            }

            json.WriteEndArray();
        }

        json.WriteEndArray();
        json.Flush();
    }

    private static void AppendFlat(IncrementalHash hash, ReadOnlySpan<int> values, byte separator)
    {
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var buffer = rented.AsSpan();
            var written = 0;
            for (var index = 0; index < values.Length; index++)
            {
                if (written > buffer.Length - 16)
                {
                    hash.AppendData(buffer[..written]);
                    written = 0;
                }

                if (index > 0)
                {
                    buffer[written++] = separator;
                }

                if (!Utf8Formatter.TryFormat(values[index], buffer[written..], out var count))
                {
                    throw new InvalidOperationException("Failed to format an integer.");
                }

                written += count;
            }

            if (written > 0)
            {
                hash.AppendData(buffer[..written]);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void AppendDelimited(IncrementalHash hash, ReadOnlySpan<int> rowMajor, int n, byte separator)
    {
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(32, n * 12));
        try
        {
            for (var row = 0; row < n; row++)
            {
                var written = 0;
                var buffer = rented.AsSpan();
                for (var column = 0; column < n; column++)
                {
                    if (column > 0)
                    {
                        buffer[written++] = separator;
                    }

                    if (!Utf8Formatter.TryFormat(rowMajor[(row * n) + column], buffer[written..], out var count))
                    {
                        throw new InvalidOperationException("Failed to format an integer.");
                    }

                    written += count;
                }

                if (row + 1 < n)
                {
                    buffer[written++] = (byte)'\n';
                }

                hash.AppendData(buffer[..written]);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void BinaryPrimitivesWriteLe(Span<byte> slot, int value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(slot, value);
    }

    private static void BinaryPrimitivesWriteBe(Span<byte> slot, int value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(slot, value);
    }

    private sealed class HashBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private readonly IncrementalHash _hash;
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(4096);

        public HashBufferWriter(IncrementalHash hash)
        {
            _hash = hash;
        }

        public void Advance(int count)
        {
            if ((uint)count > (uint)_buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _hash.AppendData(_buffer.AsSpan(0, count));
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer;
        }

        public void Dispose()
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }

        private void Ensure(int sizeHint)
        {
            var needed = Math.Max(sizeHint, 1);
            if (_buffer.Length >= needed)
            {
                return;
            }

            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(needed);
        }
    }
}
