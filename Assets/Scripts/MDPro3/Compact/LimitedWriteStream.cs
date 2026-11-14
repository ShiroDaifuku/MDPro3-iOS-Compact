using System;
using System.IO;

namespace MDPro3.Compact
{
    public sealed class LimitedWriteStream : Stream
    {
        private readonly Stream inner;
        private readonly long maximum;
        private long written;
        public LimitedWriteStream(Stream inner, long maximumBytes)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            this.inner = inner; maximum = maximumBytes;
        }
        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return inner.CanWrite; } }
        public override long Length { get { return written; } }
        public override long Position { get { return written; } set { throw new NotSupportedException(); } }
        public override void Flush() { inner.Flush(); }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
            if (count > maximum - written) throw new IOException("Resource exceeds its reserved storage budget.");
            inner.Write(buffer, offset, count);
            written += count;
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
    }
}
