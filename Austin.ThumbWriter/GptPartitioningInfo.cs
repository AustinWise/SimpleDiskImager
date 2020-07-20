using Austin.ThumbWriter.DiskImages;
using Austin.ThumbWriter.VirtualDiskService;
using Force.Crc32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Austin.ThumbWriter
{
    class GptPartitioningInfo : IPartitioningInfo
    {
        #region Struct Stuff
        readonly static Encoding Utf16Encoding = Encoding.GetEncoding("utf-16");

        const ulong EFI_MAGIC = 0x5452415020494645; // "EFI PART"
        const int CurrentRevision = 0x00010000;
        const int CurrentHeaderSize = 92;
        const int PartitionEntrySize = 128;
        const long SectorSize = Program.SECTOR_SIZE;
        const int MinimumSizeForParitionEntriesArea = 16384;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct GptHeader
        {
            public ulong Signature;
            public int Revision;
            public int HeaderSize;
            public uint Crc;
            int Zero1;
            public long CurrentLba;
            public long BackupLba;
            public long FirstUsableLba;
            public long LastUsableLba;
            public Guid DiskGuid;
            public long StartingLbaOfPartitionEntries;
            public int NumberOfPartitions;
            public int SizeOfPartitionEntry;
            public uint CrcOfPartitionEntry;
        }

        [Flags]
        enum PartitionAttributes : long
        {
            None = 0,
            System = 1,
            Active = 1 << 2,
            ReadOnly = 1 << 60,
            Hidden = 1 << 62,
            DoNotAutomount = 1 << 63,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct PartitionEntry
        {
            const int NameSize = 72;

            public Guid Type;
            public Guid ID;
            public long FirstLba;
            public long LastLba;
            public PartitionAttributes Attributes;
            fixed byte name[NameSize];

            public string Name
            {
                get
                {
                    string ret;
                    fixed (byte* bytes = name)
                    {
                        ret = Utf16Encoding.GetString(bytes, NameSize);
                    }
                    int subStr = ret.IndexOf('\0');
                    if (subStr != -1)
                        ret = ret.Substring(0, subStr);
                    return ret;
                }
            }
        }
        #endregion

        public static bool TryGetGptInfo(DiskImage hdd, out GptPartitioningInfo info)
        {
            Debug.Assert(Unsafe.SizeOf<GptHeader>() == CurrentHeaderSize);
            Debug.Assert(Unsafe.SizeOf<PartitionEntry>() == PartitionEntrySize);

            info = null;

            MbrPartitioningInfo mbr;
            if (!MbrPartitioningInfo.TryGetMbrInfo(hdd, out mbr))
                return false;
            if (!mbr.HasGpt)
                return false;

            var primaryHeader = LoadHeader(hdd, SectorSize);
            var backupHeader = LoadHeader(hdd, hdd.Length - SectorSize);

            //Make sure the headers agree on each other's locations
            if (primaryHeader.BackupLba != (hdd.Length / SectorSize) - 1)
                throw new Exception("Location of backup LBA in primary header is wrong.");
            if (backupHeader.BackupLba != 1)
                throw new Exception("Location of primary LBA in backup header is wrong.");

            //make sure the headers match
            if (primaryHeader.NumberOfPartitions != backupHeader.NumberOfPartitions)
                throw new Exception("Primary and backup GPT headers do not agree on the number of partitions.");
            if (primaryHeader.FirstUsableLba != backupHeader.FirstUsableLba || primaryHeader.LastUsableLba != backupHeader.LastUsableLba)
                throw new Exception("Primary and backup GPT headers do not agree on the usable area of the disk.");
            if (primaryHeader.DiskGuid != backupHeader.DiskGuid)
                throw new Exception("Primary and backup GPT headers do not agree on the disk GUID.");

            //TODO: Make sure the partition entries do not intersect with the usable area of the disk or the GptHeader.

            //at this point we have varified that the header for both the primary and backup
            //GPT are correct. We are only going to load paritions from the primary header.

            //Make sure the minimum space for partition entries was reserved
            if (primaryHeader.FirstUsableLba < (Program.RoundUp(MinimumSizeForParitionEntriesArea, SectorSize) / SectorSize + 2)) //extra two sectors for protective MBR and GPT header
                throw new Exception("Not enough space for primary parition entries was reserved.");
            if (primaryHeader.LastUsableLba >= Program.RoundDown(hdd.Length - MinimumSizeForParitionEntriesArea - SectorSize, SectorSize) / SectorSize)
                throw new Exception("Not enough space for backup parition entries was reserved.");


            byte[] partitionEntriesBytes = new byte[primaryHeader.NumberOfPartitions * PartitionEntrySize];
            hdd.ReadBytes(new Span<byte>(partitionEntriesBytes), primaryHeader.StartingLbaOfPartitionEntries * SectorSize);
            uint actualEntriesCrc = Crc32Algorithm.Compute(partitionEntriesBytes);
            if (primaryHeader.CrcOfPartitionEntry != actualEntriesCrc)
                throw new Exception("CRC of partition entries not correct.");

            var partitions = new List<PartitionEntry>();
            foreach (PartitionEntry part in MemoryMarshal.Cast<byte, PartitionEntry>(new ReadOnlySpan<byte>(partitionEntriesBytes)))
            {
                if (part.Type == Guid.Empty)
                    continue;
                if (part.FirstLba < primaryHeader.FirstUsableLba || part.LastLba > primaryHeader.LastUsableLba)
                    throw new Exception($"Parition '{part.Name}' is outside the usable space.");
                partitions.Add(part);
            }

            partitions.Sort((a, b) => a.FirstLba.CompareTo(b.FirstLba));

            //TODO: make sure the paritions do not intersect with each other

            info = new GptPartitioningInfo(mbr, partitions);
            return true;
        }

        readonly MbrPartitioningInfo mProtectiveMbr;
        readonly List<PartitionEntry> mPartitions;

        public List<FileExtent> Partitions => mPartitions.Select(p => new FileExtent(p.FirstLba * SectorSize, SectorSize * (p.LastLba - p.FirstLba + 1))).ToList();

        private GptPartitioningInfo(MbrPartitioningInfo protectiveMbr, List<PartitionEntry> partitions)
        {
            this.mProtectiveMbr = protectiveMbr;
            this.mPartitions = partitions;
        }

        private static GptHeader LoadHeader(DiskImage hdd, long offset)
        {
            Debug.Assert(offset % SectorSize == 0);
            var headerBytes = hdd.ReadBytes(offset, Unsafe.SizeOf<GptHeader>());

            ref GptHeader ret = ref MemoryMarshal.Cast<byte, GptHeader>(new Span<byte>(headerBytes))[0];

            if (ret.Signature != EFI_MAGIC)
                throw new Exception("Not a GPT.");
            if (ret.Revision != CurrentRevision)
                throw new Exception("Wrong rev.");
            if (ret.HeaderSize < CurrentHeaderSize)
                throw new Exception("Wrong header size.");
            if (ret.CurrentLba != offset / SectorSize)
                throw new Exception("Current LBA does not match location being read at.");
            if (ret.FirstUsableLba > ret.LastUsableLba)
                throw new Exception("Inverted usable space in GPT header.");
            uint actualCrc = ComputeHeaderCrc(ret);
            if (ret.Crc != actualCrc)
                throw new Exception("GPT header CRC not correct.");
            if (ret.SizeOfPartitionEntry != PartitionEntrySize)
                throw new Exception("Wrong ParitionEntrySize.");

            return ret;
        }

        private static uint ComputeHeaderCrc(GptHeader header)
        {
            header.Crc = 0;
            byte[] bytes = new byte[Unsafe.SizeOf<GptHeader>()];
            MemoryMarshal.Write(bytes, ref header);
            return Crc32Algorithm.Compute(bytes);
        }

        public async Task WriteParitionTableAsync(Stream stream, long diskLength)
        {
            long entriesLength = Math.Max(MinimumSizeForParitionEntriesArea, mPartitions.Count * PartitionEntrySize);
            entriesLength = Program.RoundUp(entriesLength, SectorSize);

            var entries = new byte[entriesLength];
            MemoryMarshal.Cast<PartitionEntry, byte>(new ReadOnlySpan<PartitionEntry>(mPartitions.ToArray())).CopyTo(new Span<byte>(entries));
            uint entriesCrc = Crc32Algorithm.Compute(entries, 0, entries.Length);

            long backupLoc = diskLength - SectorSize;
            long backupLba = backupLoc / SectorSize;
            long backupEntriesLoc = backupLoc - entriesLength;
            long backupEntriesLba = backupEntriesLoc / SectorSize;

            long primaryLba = 1;
            long primaryLoc = primaryLba * SectorSize;
            long primaryEntriesLba = 2;
            long primaryEntriesLoc = primaryEntriesLba * SectorSize;

            long firstUsableLba = (primaryEntriesLoc + entriesLength) / SectorSize;
            long lastUsableLba = backupEntriesLba - 1;

            Guid diskGuid = Guid.NewGuid();

            byte[] backupHeaderBytes = new byte[SectorSize];
            var backupHeader = new GptHeader()
            {
                Signature = EFI_MAGIC,
                Revision = CurrentRevision,
                HeaderSize = CurrentHeaderSize,
                Crc = 0,
                CurrentLba = backupLba,
                BackupLba = primaryLba,
                FirstUsableLba = firstUsableLba,
                LastUsableLba = lastUsableLba,
                DiskGuid = diskGuid,
                StartingLbaOfPartitionEntries = backupEntriesLba,
                NumberOfPartitions = entries.Length / PartitionEntrySize,
                SizeOfPartitionEntry = PartitionEntrySize,
                CrcOfPartitionEntry = entriesCrc,
            };
            backupHeader.Crc = ComputeHeaderCrc(backupHeader);
            MemoryMarshal.Write(new Span<byte>(backupHeaderBytes), ref backupHeader);

            byte[] primaryHeaderBytes = new byte[SectorSize];
            var primaryHeader = new GptHeader()
            {
                Signature = EFI_MAGIC,
                Revision = CurrentRevision,
                HeaderSize = CurrentHeaderSize,
                Crc = 0,
                CurrentLba = primaryLba,
                BackupLba = backupLba,
                FirstUsableLba = firstUsableLba,
                LastUsableLba = lastUsableLba,
                DiskGuid = diskGuid,
                StartingLbaOfPartitionEntries = primaryEntriesLba,
                NumberOfPartitions = entries.Length / PartitionEntrySize,
                SizeOfPartitionEntry = PartitionEntrySize,
                CrcOfPartitionEntry = entriesCrc,
            };
            primaryHeader.Crc = ComputeHeaderCrc(primaryHeader);
            MemoryMarshal.Write(new Span<byte>(primaryHeaderBytes), ref primaryHeader);

            stream.Seek(backupEntriesLoc, SeekOrigin.Begin);
            await stream.WriteAsync(entries, 0, entries.Length);
            await stream.WriteAsync(backupHeaderBytes, 0, backupHeaderBytes.Length);
            await stream.FlushAsync();

            stream.Seek(primaryEntriesLoc, SeekOrigin.Begin);
            await stream.WriteAsync(entries, 0, entries.Length);
            await stream.FlushAsync();
            stream.Seek(primaryLoc, SeekOrigin.Begin);
            await stream.WriteAsync(primaryHeaderBytes, 0, primaryHeaderBytes.Length);
            await stream.FlushAsync();

            //TODO: extend the size of the protective MBR
            await mProtectiveMbr.WriteParitionTableAsync(stream, diskLength);
        }
    }
}
