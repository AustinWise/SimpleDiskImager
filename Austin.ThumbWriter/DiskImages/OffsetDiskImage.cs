using System;

namespace Austin.ThumbWriter.DiskImages
{
    class OffsetDiskImage : DiskImage
    {
        DiskImage mHdd;
        long mOffset;
        long mSize;

        public static DiskImage Create(DiskImage hdd, long offset, long size)
        {
            while (hdd is OffsetDiskImage)
            {
                var off = (OffsetDiskImage)hdd;
                offset += off.mOffset;
                hdd = off.mHdd;
            }
            return new OffsetDiskImage(hdd, offset, size);
        }

        private OffsetDiskImage(DiskImage hdd, long offset, long size)
        {
            Init(hdd, offset, size);
        }

        protected OffsetDiskImage()
        {
        }

        protected void Init(DiskImage hdd, long offset, long size)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException();
            if (offset + size > hdd.Length)
                throw new ArgumentOutOfRangeException();

            mHdd = hdd;
            mOffset = offset;
            mSize = size;
        }

        public override void ReadBytes(Span<byte> dest, long offset)
        {
            CheckOffsets(offset, dest.Length);
            mHdd.ReadBytes(dest, mOffset + offset);
        }

        public override long Length
        {
            get { return mSize; }
        }

        public override void Dispose()
        {
            mHdd.Dispose();
        }
    }
}
