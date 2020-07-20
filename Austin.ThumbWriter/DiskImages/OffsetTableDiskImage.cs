using System;
using System.Collections.Generic;

namespace Austin.ThumbWriter.DiskImages
{
    abstract class OffsetTableDiskImage : DiskImage
    {
        protected readonly DiskImage mHdd;
        readonly Program.BlockReader<long> mReadBlock;
        readonly Func<long, long> mGetBlockKey;

        //need to be set by subclass
        protected long[] mBlockOffsets;
        protected int mBlockSize;

        protected OffsetTableDiskImage(DiskImage hdd)
        {
            if (hdd == null)
                throw new ArgumentNullException(nameof(hdd));
            mHdd = hdd;
            mReadBlock = readBlock;
            mGetBlockKey = getBlockKey;
        }

        public override long Length
        {
            get
            {
                return mBlockSize * mBlockOffsets.LongLength;
            }
        }

        public override void Dispose()
        {
            mHdd.Dispose();
        }

        public override void ReadBytes(Span<byte> dest, long offset)
        {
            CheckOffsets(offset, dest.Length);
            Program.MultiBlockCopy<long>(dest, offset, mBlockSize, mGetBlockKey, mReadBlock);
        }

        long getBlockKey(long blockId)
        {
            return mBlockOffsets[blockId];
        }

        void readBlock(Span<byte> array, long blockOffset, int blockStartNdx)
        {
            if (blockOffset == -1)
            {
                array.Fill(0);
            }
            else
            {
                mHdd.ReadBytes(array, blockOffset + blockStartNdx);
            }
        }

        public override List<FileExtent> GetFileMap()
        {
            if (Length % Program.SECTOR_SIZE != 0)
                throw new InvalidOperationException("Disk image length is not a multiple of sector size.");
            if (mBlockSize % Program.SECTOR_SIZE != 0)
                throw new InvalidOperationException("Block size is not a multiple of sector size.");

            //The above two checks ensure that the last block will also be a multiple of the sector size.

            var ret = new List<FileExtent>();
            for (int i = 0; i < mBlockOffsets.Length; i++)
            {
                if (mBlockOffsets[i] != -1)
                {
                    long blockStart = (long)i * mBlockSize;
                    long blockEnd = Math.Min(Length, blockStart + mBlockSize);
                    long blockLength = blockEnd - blockStart;
                    ret.Add(new FileExtent(blockStart, blockLength));
                }
            }
            return ret;
        }
    }
}
