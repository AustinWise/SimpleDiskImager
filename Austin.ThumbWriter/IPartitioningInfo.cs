using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Austin.ThumbWriter
{
    interface IPartitioningInfo
    {
        /// <summary>
        /// Where data resides, aligned on sector-size bounderies.
        /// </summary>
        List<FileExtent> Partitions { get; }

        Task WriteParitionTableAsync(Stream diskStream, long diskLength);
    }
}
