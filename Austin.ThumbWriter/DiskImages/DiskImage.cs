using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Austin.ThumbWriter.DiskImages
{
    public abstract class DiskImage : IDisposable
    {
        protected void CheckOffsets(long offset, long size)
        {
            if (offset < 0 || size <= 0 || offset + size > Length || offset + size < 0)
                throw new ArgumentOutOfRangeException();
        }

        public void Get<T>(long offset, out T @struct) where T : unmanaged
        {
            int structSize = Unsafe.SizeOf<T>();
            CheckOffsets(offset, structSize);
            var byteArray = ArrayPool<byte>.Shared.Rent(structSize);
            try
            {
                ReadBytes(new Span<byte>(byteArray, 0, structSize), offset);
                @struct = MemoryMarshal.Read<T>(new ReadOnlySpan<byte>(byteArray, 0, structSize));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(byteArray);
            }
        }

        public byte[] ReadBytes(long offset, int count)
        {
            var ret = new byte[count];
            ReadBytes(new Span<byte>(ret), offset);
            return ret;
        }

        public abstract void ReadBytes(Span<byte> dest, long offset);

        public abstract long Length
        {
            get;
        }

        public abstract void Dispose();

        /// <summary>
        /// Gets locations of the disk image that actually contain data.
        /// Offsets and sizes are aligned to sector size.
        /// </summary>
        /// <returns></returns>
        public virtual List<FileExtent> GetFileMap()
        {
            if (Length % Program.SECTOR_SIZE != 0)
                throw new InvalidOperationException("Disk image length is not a multiple of sector size.");
            return new List<FileExtent>()
            {
                new FileExtent(0, Length)
            };
        }
    }
}
