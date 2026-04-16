namespace ValiBlob.Core.Pipeline.Middlewares;

/// <summary>
/// A Stream wrapper that buffers the first N bytes read from a non-seekable stream,
/// then serves those bytes again on subsequent reads before forwarding to the underlying stream.
/// This allows magic-byte detection on streams that do not support seeking.
/// </summary>
internal sealed class LeadingBytesStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _buffer;
    private readonly int _bufferedCount;
    private int _bufferPosition;

    public LeadingBytesStream(Stream inner, byte[] leadingBytes, int count)
    {
        _inner = inner;
        _buffer = leadingBytes;
        _bufferedCount = count;
        _bufferPosition = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var fromBuffer = 0;
        if (_bufferPosition < _bufferedCount)
        {
            fromBuffer = Math.Min(count, _bufferedCount - _bufferPosition);
            Array.Copy(_buffer, _bufferPosition, buffer, offset, fromBuffer);
            _bufferPosition += fromBuffer;
            if (fromBuffer == count)
                return fromBuffer;
        }

        var fromInner = _inner.Read(buffer, offset + fromBuffer, count - fromBuffer);
        return fromBuffer + fromInner;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var fromBuffer = 0;
        if (_bufferPosition < _bufferedCount)
        {
            fromBuffer = Math.Min(count, _bufferedCount - _bufferPosition);
            Array.Copy(_buffer, _bufferPosition, buffer, offset, fromBuffer);
            _bufferPosition += fromBuffer;
            if (fromBuffer == count)
                return fromBuffer;
        }

        var fromInner = await _inner.ReadAsync(buffer, offset + fromBuffer, count - fromBuffer, cancellationToken).ConfigureAwait(false);
        return fromBuffer + fromInner;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
