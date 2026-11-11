using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.CatSystem
{
    internal class BinEntryV1 : Entry
    {
        public long Size64 { get; set; }
    }

    internal class BinStreamV1 : Stream
    {
        private Stream mBaseStream;
        private readonly long mOffset;
        private readonly long mLength;
        private long mPosition = 0L;
        private bool mDisposed = false;

        public BinStreamV1(Stream stream, long offset, long length)
        {
            this.mBaseStream = stream;
            this.mOffset = offset;
            this.mLength = length;
        }

        public override bool CanRead => !this.mDisposed;
        public override bool CanSeek => !this.mDisposed;
        public override bool CanWrite => false;
        public override long Length => this.mLength;
        public override long Position
        {
            get 
            {
                return this.mPosition;
            }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException();
                }
                if (value > this.mLength)
                {
                    throw new ArgumentOutOfRangeException();
                }
                this.mPosition = value;
            }
        }

        public override void Flush()
        {
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Stream stream = this.mBaseStream;

            stream.Position = this.mOffset + this.mPosition;
            int bytesRead = stream.Read(buffer, offset, (int)Math.Min(this.mLength - this.mPosition, count));

            this.Decrypt(buffer, offset, bytesRead, this.mOffset, this.mPosition);
            this.mPosition += bytesRead;

            return bytesRead;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long pos = 0L;
            switch (origin)
            {
                case SeekOrigin.Begin:
                {
                    pos = offset;
                    break;
                }
                case SeekOrigin.Current:
                {
                    pos = this.mPosition + offset;
                    break;
                }
                case SeekOrigin.End:
                {
                    pos = this.mLength + offset;
                    break;
                }
            }

            if (pos < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (pos > this.mLength)
            {
                throw new ArgumentOutOfRangeException();
            }

            this.mPosition = pos;
            return pos;
        }
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
        protected override void Dispose(bool disposing)
        {
            if (!this.mDisposed)
            {
                if (disposing)
                {
                    this.mBaseStream.Dispose();
                    this.mBaseStream = Stream.Null;
                }
                this.mDisposed = true;
                base.Dispose(disposing);
            }
        }

        protected virtual void Decrypt(byte[] buffer, long offset, int count, long fileOffset, long arcOffset)
        {
            for(int i = 0; i < count; ++i)
            {
                byte key = (byte)((fileOffset + arcOffset + i) * 0x9D + (arcOffset + i) * 0x773);
                buffer[offset + i] -= key;
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class BinOpenerV1 : ArchiveFormat
    {
        public override string Tag => "BinV1/CSPACK";
        public override string Description => "CatSystem2 resource archive";
        public override uint Signature => 0x40674461u;
        public override bool IsHierarchic => true;
        public override bool CanWrite => false;

        public override ArcFile TryOpen(ArcView file)
        {
            using (ArcViewStream stream = file.CreateStream())
            {
                using (BinaryReader br = new BinaryReader(stream, Encoding.Unicode, true))
                {
                    stream.Position = 8L;

                    List<BinEntryV1> entries = new List<BinEntryV1>();
                    {
                        string fn = br.ReadString();
                        while (!string.IsNullOrEmpty(fn))
                        {
                            BinEntryV1 e = Create<BinEntryV1>(fn);
                            e.Offset = br.ReadUInt32();
                            e.Size64 = 0L;

                            entries.Add(e);

                            fn = br.ReadString();
                        }
                    }

                    if (entries.Any())
                    {
                        {
                            BinEntryV1 last = entries.Last();
                            last.Size64 = stream.Length - last.Offset;
                            last.Size = (uint)last.Size64;
                        }
                        for (int i = 0; i < entries.Count - 1; ++i)
                        {
                            BinEntryV1 curr = entries[i + 0];
                            BinEntryV1 next = entries[i + 1];
                            curr.Size64 = next.Offset - curr.Offset;
                            curr.Size = (uint)curr.Size64;
                        }
                    }

                    return new ArcFile(file, this, entries.Cast<Entry>().ToList());
                }
            }
        }
        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            if(!(entry is BinEntryV1 e))
            {
                return base.OpenEntry(arc, entry);
            }
            return new BinStreamV1(arc.File.CreateStream(), e.Offset, e.Size64);
        }
    }
}
