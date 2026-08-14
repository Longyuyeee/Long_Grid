using System.Text;

namespace LongGrid.ThumbnailWorker;

internal sealed class BoundedLineReader
{
    private const int BufferSize = 1_024;
    private readonly TextReader _reader;
    private readonly int _maximumCharacters;
    private readonly char[] _buffer = new char[BufferSize];
    private int _bufferIndex;
    private int _bufferCount;

    internal BoundedLineReader(TextReader reader, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 1);
        _reader = reader;
        _maximumCharacters = maximumCharacters;
    }

    internal async Task<string?> ReadLineAsync()
    {
        var line = new StringBuilder();

        while (true)
        {
            if (_bufferIndex >= _bufferCount)
            {
                _bufferCount = await _reader.ReadAsync(_buffer.AsMemory());
                _bufferIndex = 0;
                if (_bufferCount == 0)
                {
                    return line.Length == 0 ? null : line.ToString();
                }
            }

            char value = _buffer[_bufferIndex++];
            if (value == '\n')
            {
                if (line.Length > 0 && line[^1] == '\r')
                {
                    line.Length--;
                }

                return line.ToString();
            }

            if (line.Length >= _maximumCharacters)
            {
                throw new InvalidDataException(
                    "The protocol line exceeded its configured limit.");
            }

            line.Append(value);
        }
    }
}
