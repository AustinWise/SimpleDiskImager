using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Austin.ThumbWriter.DiskImages
{
    unsafe class RawDiskImage : DiskImage
    {
        private MemoryMappedFile mFile;
        private MemoryMappedViewAccessor mViewAcessor;
        private SafeBuffer mBuffer;
        private long mSize;

        public RawDiskImage(string path)
        {
            mFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
            mViewAcessor = mFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            long fileSize = new FileInfo(path).Length;
            //Limit the range of data we read to the Capacity of the ViewAccessor
            //in the unlikly case that it is smaller than the file size we read.
            //We can't just use the Capacity though, as it is round up to the page size.
            mSize = Math.Min(mViewAcessor.Capacity, fileSize);
            mBuffer = mViewAcessor.SafeMemoryMappedViewHandle;
        }

        public override void ReadBytes(Span<byte> dest, long offset)
        {
            CheckOffsets(offset, dest.Length);
            byte* pointer = null;
            mBuffer.AcquirePointer(ref pointer);
            try
            {
                new ReadOnlySpan<byte>(pointer + offset, dest.Length).CopyTo(dest);
            }
            finally
            {
                mBuffer.ReleasePointer();
            }
        }

        public override long Length
        {
            get { return mSize; }
        }

        public override void Dispose()
        {
            mBuffer.Dispose();
            mViewAcessor.Dispose();
            mFile.Dispose();
        }

        public override List<FileExtent> GetFileMap()
        {
            //TODO: add support for sparse files?
            return base.GetFileMap();
        }
    }
}
