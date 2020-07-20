using System;
using System.Collections.Generic;
using System.Text;

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
