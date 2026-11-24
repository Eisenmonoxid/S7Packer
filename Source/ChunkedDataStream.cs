using System;
using System.Collections.Generic;
using System.IO;

namespace S7Packer.Source
{
    public sealed class ChunkedDataStream : Stream
    {
        private readonly List<MemoryStream> _Chunks = [];
        private readonly int _ChunkSize;

        private long _Position;
        private long _Length;
        private bool _Disposed;

        public override bool CanRead => !_Disposed;
        public override bool CanSeek => !_Disposed;
        public override bool CanWrite => !_Disposed;
        public override long Length => _Length;
        private readonly long MaxStreamLength = UInt32.MaxValue - (BBAHeader.Size + BBADirectoryHeader.Size + BBADirectory.Size);

        public override long Position
        {
            get => _Position;
            set
            {
                ThrowIfDisposed();
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxStreamLength);
                _Position = value;
            }
        }

        public ChunkedDataStream(int Size = 100 * 1024 * 1024) // 100 MB
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Size);
            _ChunkSize = Size;
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_Disposed, this);

        private MemoryStream GetChunk(int Index)
        {
            while (_Chunks.Count <= Index)
            {
                _Chunks.Add(new MemoryStream(_ChunkSize));
            }

            return _Chunks[Index];
        }

        public override long Seek(long Offset, SeekOrigin Origin)
        {
            ThrowIfDisposed();
            long NewPosition = Origin switch
            {
                SeekOrigin.Begin => Offset,
                SeekOrigin.Current => checked(_Position + Offset),
                SeekOrigin.End => checked(_Length + Offset),
                _ => throw new ArgumentException("Invalid seek origin.")
            };

            if (NewPosition < 0)
            {
                throw new IOException("Cannot seek before the beginning of the stream.");
            }

            _Position = NewPosition;
            return _Position;
        }

        public override void Write(ReadOnlySpan<byte> Buffer)
        {
            ThrowIfDisposed();
            if (_Position > MaxStreamLength - Buffer.Length)
            {
                throw new IOException("Maximum stream size exceeded.");
            }

            while (!Buffer.IsEmpty)
            {
                int chunkIndex = (int)(_Position / _ChunkSize);
                int chunkOffset = (int)(_Position % _ChunkSize);

                MemoryStream chunk = GetChunk(chunkIndex);
                for (int i = 0; i < chunkIndex; i++)
                {
                    MemoryStream previous = GetChunk(i);
                    if (previous.Length < _ChunkSize)
                    {
                        previous.SetLength(_ChunkSize);             
                    }
                }

                int Count = Math.Min(_ChunkSize - chunkOffset, Buffer.Length);
                chunk.Position = chunkOffset;
                chunk.Write(Buffer[..Count]);

                _Position += Count;
                Buffer = Buffer[Count..];
                _Length = Math.Max(_Length, _Position);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            Write(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            ThrowIfDisposed();
            int totalRead = 0;
            while (!buffer.IsEmpty && _Position < _Length)
            {
                int chunkIndex = (int)(_Position / _ChunkSize);
                int chunkOffset = (int)(_Position % _ChunkSize);

                MemoryStream Chunk = _Chunks[chunkIndex];

                int Count = (int)Math.Min(Math.Min(_ChunkSize - chunkOffset, buffer.Length), _Length - _Position);
                Chunk.Position = chunkOffset;
                int bytesRead = Chunk.Read(buffer[..Count]);

                if (bytesRead == 0)
                {
                    break;
                }

                _Position += bytesRead;
                totalRead += bytesRead;
                buffer = buffer[bytesRead..];
            }

            return totalRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        public override void SetLength(long value)
        {
            ThrowIfDisposed();
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            if (value > MaxStreamLength)
            {
                throw new IOException("Maximum stream size exceeded.");
            }

            int requiredChunks = value == 0 ? 0 : checked((int)((value - 1) / _ChunkSize + 1));
            while (_Chunks.Count < requiredChunks)
            {
                GetChunk(_Chunks.Count);
            }

            while (_Chunks.Count > requiredChunks)
            {
                int last = _Chunks.Count - 1;
                _Chunks[last].Dispose();
                _Chunks.RemoveAt(last);
            }

            if (requiredChunks > 0)
            {
                int lastChunkSize = (int)(value % _ChunkSize);
                if (lastChunkSize == 0)
                {
                    lastChunkSize = _ChunkSize;
                }

                _Chunks[^1].SetLength(lastChunkSize);
            }

            _Length = value;
        }

        public override void Flush() => _Chunks.ForEach(X => X.Flush());

        protected override void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                foreach (MemoryStream Chunk in _Chunks)
                {
                    Chunk.Dispose();
                }

                _Chunks.Clear();
                _Disposed = true;
            }

            base.Dispose(disposing);
        }

        public bool CopyToAllDataAndDispose(Stream Destination)
        {
            try
            {
                ThrowIfDisposed();
                ArgumentNullException.ThrowIfNull(Destination);
                if (!Destination.CanWrite)
                {
                    throw new ArgumentException("Destination stream is not writable.", nameof(Destination));
                } 
            }
            catch
            {
                return false;
            }
            
            foreach (MemoryStream Stream in _Chunks)
            {
                Stream.Seek(0, SeekOrigin.Begin);
                Stream.CopyTo(Destination);
            }

            Dispose();
            return true;
        }

        public override void CopyTo(Stream Destination, int BufferSize)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(Destination);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BufferSize);

            if (!Destination.CanWrite)
            {
                throw new ArgumentException("Destination stream is not writable.", nameof(Destination));
            }

            byte[] Buffer = new byte[BufferSize];
            while (_Position < _Length)
            {
                int chunkIndex = (int)(_Position / _ChunkSize);
                int chunkOffset = (int)(_Position % _ChunkSize);

                MemoryStream Chunk = _Chunks[chunkIndex];

                int Count = (int)Math.Min(Math.Min(_ChunkSize - chunkOffset, _Length - _Position), BufferSize);
                Chunk.Position = chunkOffset;

                int Read = Chunk.Read(Buffer, 0, Count);
                if (Read == 0)
                {
                    break;
                }

                Destination.Write(Buffer, 0, Read);
                _Position += Read;
            }
        }
    }   
}
