using Austin.ThumbWriter.DiskImages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Austin.ThumbWriter
{
    class MbrPartitioningInfo : IPartitioningInfo
    {
        #region StructStuff
        const int MBR_SIZE = 512;
        enum MbrPartitionType : byte
        {
            Empty = 0,
            Ntfs = 7,
            Solaris = 0xbf,
            GptProtective = 0xee,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct CHS
        {
            short Stuff1;
            byte Stuff2;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct PartitionEntry
        {
            public byte Status;
            public CHS FirstSector;
            public MbrPartitionType Type;
            public CHS LastSector;
            public uint FirstSectorLba;
            public uint NumberOfSectors;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct MbrHeader
        {
            public const ushort MbrMagic = 0xaa55;

            fixed byte BootstrapCode1[218];
            short Zeros1;
            public byte OriginalPhysicalDrive;
            public byte Seconds;
            public byte Minutes;
            public byte Hours;
            fixed byte BootStrapCode2[216];
            public int DiskSig;
            short Zeros2;
            public PartitionEntry Partition1;
            public PartitionEntry Partition2;
            public PartitionEntry Partition3;
            public PartitionEntry Partition4;
            public ushort BootSig;

            public PartitionEntry GetPartition(int index)
            {
                switch (index)
                {
                    case 0:
                        return Partition1;
                    case 1:
                        return Partition2;
                    case 2:
                        return Partition3;
                    case 3:
                        return Partition4;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        #endregion

        public static bool TryGetMbrInfo(DiskImage hdd, out MbrPartitioningInfo info)
        {
            info = default;

            Debug.Assert(Unsafe.SizeOf<MbrHeader>() == MBR_SIZE);

            var headerBytes = hdd.ReadBytes(0, MBR_SIZE);
            MbrHeader header = MemoryMarshal.Cast<byte, MbrHeader>(new Span<byte>(headerBytes))[0];

            if (header.BootSig != MbrHeader.MbrMagic)
            {
                return false;
            }

            bool hasGpt = false;
            for (int i = 0; i < 4; i++)
            {
                var part = header.GetPartition(i);
                hasGpt |= part.Type == MbrPartitionType.GptProtective;
            }

            //MBR paritioned disks often contain boot loaders in non-paritioned space.
            //So to be safe copy all the data.
            List<FileExtent> partitions = new List<FileExtent>()
            {
                new FileExtent(MBR_SIZE, hdd.Length-MBR_SIZE),
            };

            info = new MbrPartitioningInfo(headerBytes, partitions, hasGpt);
            return true;
        }

        private readonly byte[] mHeader;

        public List<FileExtent> Partitions { get; }

        public bool HasGpt { get; }

        private MbrPartitioningInfo(byte[] header, List<FileExtent> partitions, bool hasGpt)
        {
            this.mHeader = header;
            this.Partitions = partitions;
            this.HasGpt = hasGpt;
        }

        static void fixupPartitionEntry(ref PartitionEntry part, long diskLength)
        {
            if (part.Type != MbrPartitionType.GptProtective)
                return;
            long lastSector = (diskLength / Program.SECTOR_SIZE) - 1;
            long numberOfSectors = lastSector - part.FirstSectorLba;
            if (numberOfSectors >= (long)uint.MaxValue)
            {
                part.NumberOfSectors = uint.MaxValue;
            }
            else
            {
                part.NumberOfSectors = (uint)numberOfSectors;
            }
        }

        public async Task WriteParitionTableAsync(Stream diskStream, long diskLength)
        {
            MbrHeader header = MemoryMarshal.Cast<byte, MbrHeader>(new ReadOnlySpan<byte>(mHeader))[0];
            fixupPartitionEntry(ref header.Partition1, diskLength);
            fixupPartitionEntry(ref header.Partition2, diskLength);
            fixupPartitionEntry(ref header.Partition3, diskLength);
            fixupPartitionEntry(ref header.Partition4, diskLength);

            var headerBytes = new byte[MBR_SIZE];
            MemoryMarshal.Write(new Span<byte>(headerBytes), ref header);

            //var headerBytes = MemoryMarshal.Write
            diskStream.Seek(0, SeekOrigin.Begin);
            await diskStream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await diskStream.FlushAsync();
        }
    }
}
