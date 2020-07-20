using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VdsWrapper;

namespace Austin.ThumbWriter.VirtualDiskService
{
    public class Disk : IDisposable, IEquatable<Disk>, IComparable<Disk>
    {
        static readonly Regex rDiskPath = new Regex(@"^\\\\\?\\PhysicalDrive(?<num>\d+)$", RegexOptions.CultureInvariant);

        private bool mDisposed;
        private readonly IVdsDisk mDisk;
        private readonly Guid mId;

        public int DiskNumber { get; }

        public string DiskPath { get; }

        public string Name { get; }

        public bool IsRemovable { get; }

        public long Capacity { get; }

        internal Disk(IVdsDisk vdsDisk, Guid id, string diskPath, string name, bool isRemovable, long capacity)
        {
            this.mDisk = vdsDisk;
            this.mId = id;
            this.DiskPath = diskPath;
            this.Name = name;
            this.IsRemovable = isRemovable;
            this.Capacity = capacity;
            Match m = rDiskPath.Match(diskPath);
            if (!m.Success)
                throw new ArgumentException("Disk path was did unexpected: " + diskPath);
            this.DiskNumber = int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture);
        }

        internal async Task Clean()
        {
            if (mDisposed)
                throw new ObjectDisposedException(nameof(Disk));

            await VdsHelper.RunActionAsync((out IVdsAsync vsdAsync) =>
            {
                var adv = mDisk as IVdsAdvancedDisk;
                if (adv == null)
                    throw new InvalidOperationException("Disk does not support cleaning.");
                adv.Clean(bForce: 1, bForceOEM: 1, bFullClean: 0, out vsdAsync);
            });
        }

        public void Eject()
        {
            var removeable = mDisk as IVdsRemovable;
            if (removeable != null)
            {
                removeable.Eject();
            }
        }

        public void Dispose()
        {
            if (mDisposed)
                throw new ObjectDisposedException(nameof(Disk));
            Marshal.ReleaseComObject(mDisk);
            mDisposed = true;
        }

        public bool Equals(Disk other)
        {
            if (other == null)
                return false;
            return this.mId == other.mId;
        }

        public int CompareTo(Disk other)
        {
            if (other is null)
                return 1;
            if (this.DiskNumber == other.DiskNumber)
                return this.mId.CompareTo(other.mId);
            else
                return this.DiskNumber.CompareTo(other.DiskNumber);
        }
    }
}
