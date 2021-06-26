using Austin.ThumbWriter.DiskImages;
using Austin.ThumbWriter.VirtualDiskService;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Austin.ThumbWriter
{
    public static class DiskImageWriter
    {
        const int SECTOR_SIZE = Program.SECTOR_SIZE;
        const int MAX_CHUNK_SHIFT = 20;
        const int MAX_CHUNK = 1 << MAX_CHUNK_SHIFT;
        const int MAX_CHUNK_LBAS = MAX_CHUNK / SECTOR_SIZE;

        /// <summary>
        /// Copies the contents of the disk image to the disk.
        /// </summary>
        /// <param name="disk"></param>
        /// <param name="image"></param>
        /// <param name="progress">Recievies the percent complete progress.</param>
        /// <returns></returns>
        public static async Task WriteImageToDisk(Disk disk, DiskImage image, IProgress<int> progress)
        {
            if (image.Length > disk.Capacity)
            {
                throw new InvalidOperationException("Disk image is larger than the size of the selected disk.");
            }
            if (disk.Capacity % Program.SECTOR_SIZE != 0)
            {
                throw new InvalidOperationException("Disk capacity is not a multiple of sector size.");
            }

            IPartitioningInfo partInfo;
            bool imageLengthOk = image.Length % Program.SECTOR_SIZE == 0;
            bool hasPartitions = getParitionInfo(image, out partInfo);

            if (!imageLengthOk)
            {
                if (hasPartitions)
                {
                    throw new InvalidOperationException($"Found partition info, but image file length is not a multiple of sector size ({SECTOR_SIZE} bytes). Maybe the file got partially truncated?");
                }
                else
                {
                    throw new PartitionInformationMissingException();
                }
            }
            else if (!hasPartitions)
            {
                throw new PartitionInformationMissingException();
            }

            var copyPlan = CreateCopyPlan(image, partInfo);

            await disk.Clean().ConfigureAwait(false);

            int access = Interop.Kernel32.GenericOperations.GENERIC_WRITE;
            using (var hDisk = Interop.Kernel32.CreateFile(disk.DiskPath, access, FileShare.ReadWrite, FileMode.Open, 0))
            {
                if (hDisk.IsInvalid)
                    throw new Exception("Failed to open disk handle.");

                using (var fs = new FileStream(hDisk, FileAccess.Write))
                {
                    var buffer = new byte[MAX_CHUNK];

                    for (int i = 0; i < copyPlan.Count; i++)
                    {
                        var chunk = copyPlan[i];
                        image.ReadBytes(new Span<byte>(buffer, 0, (int)chunk.Length), chunk.Offset);
                        fs.Seek(chunk.Offset, SeekOrigin.Begin);
                        await fs.WriteAsync(buffer, 0, (int)chunk.Length);
                        progress.Report(i * 100 / copyPlan.Count);
                    }

                    if (partInfo != null)
                    {
                        await partInfo.WriteParitionTableAsync(fs, disk.Capacity);
                    }

                    await fs.FlushAsync();
                    fs.Flush(flushToDisk: true);
                }

                progress.Report(100);
            }

            disk.Eject();
        }

        //TODO: align copy plan with file map in disk image
        private static List<FileExtent> CreateCopyPlan(DiskImage image, IPartitioningInfo partInfo)
        {
            //TODO: figure out if it possible to quickly zero the whole disk so we can skip writing zero blocks.
            //Using allocation bitmap from the disk image will speed thing up if the disk is sparse.
            //However not writing zeros can cause data corruption.
            var diskImageBitmap = new BitArray((int)(image.Length / SECTOR_SIZE), true);

            if (partInfo != null)
            {
                var partBitmap = GetAllocationBitmap(image.Length, partInfo.Partitions);
                diskImageBitmap = diskImageBitmap.And(partBitmap);
            }

            var ret = new List<FileExtent>();
            long startLba = -1;
            long currentLba;
            for (currentLba = 0; currentLba < diskImageBitmap.Length; currentLba++)
            {
                if (startLba != -1 && (currentLba - startLba) >= MAX_CHUNK_LBAS)
                {
                    ret.Add(new FileExtent(startLba * SECTOR_SIZE, (currentLba - startLba) * SECTOR_SIZE));
                    startLba = -1;
                }

                if (diskImageBitmap[checked((int)currentLba)])
                {
                    if (startLba == -1)
                        startLba = currentLba;
                }
                else if (startLba != -1)
                {
                    ret.Add(new FileExtent(startLba * SECTOR_SIZE, (currentLba - startLba) * SECTOR_SIZE));
                    startLba = -1;
                }
            }

            if (startLba != -1)
            {
                long offset = startLba * SECTOR_SIZE;
                ret.Add(new FileExtent(offset, image.Length - offset));
            }

            return ret;
        }

        private static BitArray GetAllocationBitmap(long diskLength, List<FileExtent> fileExtents)
        {
            var diskImageBitmap = new BitArray(checked((int)(diskLength / Program.SECTOR_SIZE)));
            foreach (var chunk in fileExtents)
            {
                for (long i = 0; i < chunk.Length; i += Program.SECTOR_SIZE)
                {
                    long offset = i + chunk.Offset;
                    int lba = (int)(offset / Program.SECTOR_SIZE);
                    diskImageBitmap[lba] = true;
                }
            }
            return diskImageBitmap;
        }

        static bool getParitionInfo(DiskImage image, out IPartitioningInfo info)
        {
            GptPartitioningInfo gpt;
            if (GptPartitioningInfo.TryGetGptInfo(image, out gpt))
            {
                info = gpt;
                return true;
            }
            MbrPartitioningInfo mbr;
            if (MbrPartitioningInfo.TryGetMbrInfo(image, out mbr))
            {
                info = mbr;
                return true;
            }
            info = null;
            return false;
        }
    }
}
