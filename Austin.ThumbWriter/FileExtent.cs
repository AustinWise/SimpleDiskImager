using System;

namespace Austin.ThumbWriter
{
    public class FileExtent
    {
        public FileExtent(long offset, long length)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be greater than or equal to zero.");
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.");

            this.Offset = offset;
            this.Length = length;
        }

        public long Offset { get; }
        public long Length { get; }
    }
}
