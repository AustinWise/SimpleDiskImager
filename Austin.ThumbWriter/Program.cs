using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Austin.ThumbWriter
{
    static class Program
    {
        //TODO: handle disks and disk images whose sector size is different
        public const int SECTOR_SIZE = 512;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf<T>()
        {
            return Unsafe.SizeOf<T>();
        }

        public static T ToStruct<T>(byte[] bytes) where T : struct
        {
            return ToStruct<T>(bytes, 0);
        }

        public unsafe static T ToStruct<T>(byte[] bytes, int offset) where T : struct
        {
            return MemoryMarshal.Read<T>(new ReadOnlySpan<byte>(bytes, offset, Unsafe.SizeOf<T>()));
        }

        public static T ToStruct<T>(ArraySegment<byte> bytes) where T : struct
        {
            if (Unsafe.SizeOf<T>() != bytes.Count)
                throw new ArgumentOutOfRangeException();
            return ToStruct<T>(bytes.Array, bytes.Offset);
        }

        public static T ToStruct<T>(Span<byte> bytes) where T : struct
        {
            if (Unsafe.SizeOf<T>() != bytes.Length)
                throw new ArgumentOutOfRangeException();
            return MemoryMarshal.Cast<byte, T>(bytes)[0];
        }

        public static T ToStructFromBigEndian<T>(byte[] bytes) where T : struct
        {
            if (BitConverter.IsLittleEndian)
            {
                return ToStructByteSwap<T>(bytes);
            }
            else
            {
                return ToStruct<T>(bytes);
            }
        }

        static T ToStructByteSwap<T>(byte[] bytes) where T : struct
        {
            var copy = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, copy, 0, copy.Length);
            foreach (var f in typeof(T).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
            {
                ByteSwapField<T>(f.Name, f.FieldType, copy);
            }
            return ToStruct<T>(copy);
        }

        static Dictionary<Type, int> sStructSize = new Dictionary<Type, int>()
        {
            { typeof(byte), 1 },
            { typeof(sbyte), 1 },
            { typeof(short), 2 },
            { typeof(ushort), 2 },
            { typeof(int), 4 },
            { typeof(uint), 4 },
            { typeof(long), 8 },
            { typeof(ulong), 8 },
            { typeof(Guid), 16 },
        };

        static void ByteSwapField<T>(string fieldName, Type fieldType, byte[] byteArray) where T : struct
        {
            var itemOffset = Marshal.OffsetOf(typeof(T), fieldName).ToInt32();
            ByteSwap(fieldType, byteArray, itemOffset);
        }

        static void ByteSwap(Type type, byte[] byteArray, int itemOffset)
        {
            int itemSize;
            if (!sStructSize.TryGetValue(type, out itemSize))
            {
                if (type.IsEnum)
                {
                    var realType = type.GetEnumUnderlyingType();
                    ByteSwap(realType, byteArray, itemOffset);
                    return;
                }
                else if (type.GetCustomAttributes(typeof(UnsafeValueTypeAttribute), false).Length != 0)
                {
                    return;
                    //ignore fixed size buffers
                }
                else
                    throw new NotSupportedException();
            }

            for (int byteNdx = 0; byteNdx < itemSize / 2; byteNdx++)
            {
                int lowerNdx = itemOffset + byteNdx;
                int higherNdx = itemOffset + itemSize - byteNdx - 1;
                byte b = byteArray[lowerNdx];
                byteArray[lowerNdx] = byteArray[higherNdx];
                byteArray[higherNdx] = b;
            }
        }

        /// <summary>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dest">The place to store the read data.</param>
        /// <param name="blockKey">An identifier for the block.</param>
        /// <param name="startNdx">The offset within the block to start reading from.</param>
        public delegate void BlockReader<T>(Span<byte> dest, T blockKey, int startNdx);

        public static long RoundUp(long x, long y)
        {
            return ((x + (y - 1)) / y) * y;
        }

        public static long RoundDown(long x, long y)
        {
            return (x / y) * y;
        }

        /// <summary>
        /// Given a large amount of data stored in equal sized blocks, reads a subset of that data efficiently.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dest">The place to store the read data.</param>
        /// <param name="offset">The byte offset into the blocks to read.</param>
        /// <param name="blockSize">The size of the blocks.</param>
        /// <param name="GetBlockKey">Given a block offset returns a key for reading that block.</param>
        /// <param name="ReadBlock">Given a block key, reads the block.</param>
        public static void MultiBlockCopy<T>(Span<byte> dest, long offset, int blockSize, Func<long, T> GetBlockKey, BlockReader<T> ReadBlock)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");
            if (blockSize <= 0)
                throw new ArgumentOutOfRangeException("blockSize");

            long firstBlock = offset / blockSize;
            int numBlocks = (int)((RoundUp(offset + dest.Length, blockSize) - RoundDown(offset, blockSize)) / blockSize);

            int remaingBytes = dest.Length;
            int destOffset = 0;
            for (int i = 0; i < numBlocks; i++)
            {
                int blockOffset = (int)(offset % blockSize);
                int size = Math.Min(remaingBytes, blockSize - blockOffset);

                var key = GetBlockKey(firstBlock + i);
                ReadBlock(dest.Slice(destOffset, size), key, blockOffset);

                destOffset += size;
                offset += size;
                remaingBytes -= size;
            }
        }
    }


}
