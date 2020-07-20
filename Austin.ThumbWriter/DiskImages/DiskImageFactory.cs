using System;
using System.Collections.Generic;
using System.IO;

namespace Austin.ThumbWriter.DiskImages
{
    public sealed class DiskImageFactory
    {
        static readonly List<DiskImageFactory> sImageFormats = new List<DiskImageFactory>()
        {
            new DiskImageFactory(".img", "Raw image", path => new RawDiskImage(path)),
            new DiskImageFactory(".usb", "USB drive image", path => new RawDiskImage(path)),
            new DiskImageFactory(".iso", "ISO hybrid disc image", path => new RawDiskImage(path)),
            new DiskImageFactory(".vhd", "VHD file", path => VhdDiskImage.Create(new RawDiskImage(path))),
        };

        //TODO: the return type name does not really make sense
        public static DiskImageFactory[] GetAllSupportedFormats()
        {
            return sImageFormats.ToArray();
        }

        public static DiskImage Create(string path)
        {
            string ext = Path.GetExtension(path);
            foreach (var fmt in sImageFormats)
            {
                if (fmt.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))
                    return fmt.mCreator(path);
            }
            throw new InvalidOperationException("Unsupported image type.");
        }

        private readonly Func<string, DiskImage> mCreator;

        /// <summary>
        /// File extension including dot, for example: ".img".
        /// </summary>
        public string Extension { get; }

        public string Name { get; }

        private DiskImageFactory(string extention, string name, Func<string, DiskImage> creator)
        {
            mCreator = creator;
            Extension = extention;
            Name = name;
        }
    }
}
