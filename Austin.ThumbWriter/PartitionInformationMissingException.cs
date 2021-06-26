using System;

namespace Austin.ThumbWriter
{
    public class PartitionInformationMissingException : Exception
    {
        public PartitionInformationMissingException()
            : base("No partitioning information was recognized in the disk image.")
        {
        }
    }
}
