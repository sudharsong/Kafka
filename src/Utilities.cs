﻿using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace codecrafterskafka.src
{
    public static class Utilities
    {
        public static async Task<byte[]> ReadExactlyAsync(this Socket socket,
                                                         int count,
                                                         CancellationToken token)
        {
            var buffer = new byte[count];
            int offset = 0;

            while (offset < count)
            {
                // ← use the Memory<byte> overload, which takes a CancellationToken
                int n = await socket.ReceiveAsync(
                    buffer.AsMemory(offset, count - offset),
                    SocketFlags.None,
                    token);

                if (n == 0)
                {
                    // peer closed before we got 'count' bytes
                    return Array.Empty<byte>();
                }

                offset += n;
            }

            return buffer;
        }

        public static async Task SendAllAsync(this Socket socket,
                                      ReadOnlyMemory<byte> buffer,
                                      CancellationToken token)
        {
            int sent = 0;
            while (sent < buffer.Length)
            {
                //int n = s.Send(buffer.ToArray()[sent..]);
                int n = await socket.SendAsync(buffer[sent..], SocketFlags.None, token);
                if (n == 0) throw new IOException("Peer closed while sending");
                sent += n;
            }
        }

        public static void WriteToBuffer(this ArrayBufferWriter<byte> writer, short value)
        {
            Span<byte> span = writer.GetSpan(2);
            BinaryPrimitives.WriteInt16BigEndian(span, value);
            writer.Advance(2);
        }

        public static void WriteToBuffer(this ArrayBufferWriter<byte> writer, int value)
        {
            Span<byte> span = writer.GetSpan(4);
            BinaryPrimitives.WriteInt32BigEndian(span, value);
            writer.Advance(4);
        }

        public static void WriteToBuffer(this ArrayBufferWriter<byte> writer, uint value)
        {
            Span<byte> span = writer.GetSpan(4);
            Console.WriteLine($"CRC value passed {value}");
            BinaryPrimitives.WriteUInt32BigEndian(span, value);
            writer.Advance(4);
        }

        public static void WriteToBuffer(this ArrayBufferWriter<byte> writer, long value)
        {
            Span<byte> span = writer.GetSpan(8);
            BinaryPrimitives.WriteInt64BigEndian(span, value);
            writer.Advance(8);
        }

        public static void WriteToBuffer(this ArrayBufferWriter<byte> writer, byte value)
        {
            Span<byte> span = writer.GetSpan(1);
            span[0] = value;
            writer.Advance(1);
        }

        public static void WriteToBuffer(this ArrayBufferWriter<byte> writer, byte[] value)
        {
            Span<byte> span = writer.GetSpan(value.Length);            
            value.CopyTo(span);
            writer.Advance(value.Length);
        }

        public static void WriteGuidToBuffer(this ArrayBufferWriter<byte> writer, Guid guid)
        {
            // Allocate a 16-byte span to write into
            Span<byte> span = writer.GetSpan(16);

            // Grab the CLR little-endian bytes
            byte[] le = guid.ToByteArray();

            // Reverse Data1 (first 4 bytes)
            span[0] = le[3];
            span[1] = le[2];
            span[2] = le[1];
            span[3] = le[0];

            // Reverse Data2 (next 2 bytes)
            span[4] = le[5];
            span[5] = le[4];

            // Reverse Data3 (next 2 bytes)
            span[6] = le[7];
            span[7] = le[6];

            // Copy Data4 (final 8 bytes) as-is
            new ReadOnlySpan<byte>(le, 8, 8).CopyTo(span.Slice(8, 8));

            writer.Advance(16);
        }

        public static void WriteVarIntToBuffer(this ArrayBufferWriter<byte> writer, int value)
        {
            uint zig = (uint)((value << 1) ^ (value >> 31));

            // 2) Emit 7 bits per byte, setting the high bit if more follow
            while ((zig & ~0x7Fu) != 0)
            {
                // write (lower7bits | continuation)
                writer.GetSpan(1)[0] = (byte)((zig & 0x7F) | 0x80);
                writer.Advance(1);
                zig >>= 7;
            }

            // last byte (high bit = 0)
            writer.GetSpan(1)[0] = (byte)zig;
            writer.Advance(1);
        }

        public static void WriteUVarInt(this ArrayBufferWriter<byte> w, uint value)
        {
            while ((value & ~0x7Fu) != 0)
            {
                w.GetSpan(1)[0] = (byte)((value & 0x7F) | 0x80);
                w.Advance(1);
                value >>= 7;
            }
            w.GetSpan(1)[0] = (byte)value;
            w.Advance(1);
        }

        public static int ReadInt32FromBuffer(this byte[] buffer, ref int offset)
        {            
            var result = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(offset, 4));
            offset += 4;
            return result;
        }

        public static long ReadInt64FromBuffer(this byte[] buffer, ref int offset)
        {
            var result = BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(offset, 8));
            offset += 8;
            return result;
        }

        public static short ReadInt16FromBuffer(this byte[] buffer, ref int offset)
        {
            var result = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(offset, 2));
            offset += 2;
            return result;
        }

        public static Guid ReadGuidFromBuffer(this byte[] buffer, ref int offset)
        {
            //Guid result = new Guid(buffer.AsSpan(offset, 16).ToArray());
            //offset += 16;
            //return result;
            var b = buffer.AsSpan(offset, 16).ToArray();
            Array.Reverse(b, 0, 4);  // Data1
            Array.Reverse(b, 4, 2);  // Data2
            Array.Reverse(b, 6, 2);  // Data3
                                     // Data4 (bytes 8–15) stays in network order
            offset += 16;
            // 3. Construct the Guid from the now-correct little-endian byte array
            return new Guid(b);
        }

        public static string ReadStringFromBuffer(this byte[] buffer, ref int offset, int length)
        { 
            var result = Encoding.UTF8.GetString(buffer.Skip(offset).Take(length).ToArray());
            offset += length;
            return result;
        }

        public static byte ReadByteFromBuffer(this byte[] buffer, ref int offset)
        {
            var result = buffer[offset];
            offset += 1;
            return result;
        }

        public static int ReadVarInt(this byte[] buffer, ref int offset)
        {
            int value = 0;
            int shift = 0;
            bool continuationBit = true;
            for (int i = 0; i < 5 && continuationBit; i++)
            {
                if (offset >= buffer.Length)
                {
                    throw new IndexOutOfRangeException("End of buffer reading varint.");
                }
                continuationBit = (buffer[offset] & 0x80) == 0x80;
                value |= (buffer[offset++] & 0x7f) << shift;
                shift += 7;
            }
            return (value >> 1) ^ -(value & 1);
        }

        internal static uint ReadUVarInt(this byte[] buffer, ref int offset)
        {
            uint value = 0;
            int shift = 0;

            bool continuationBit = true;
            for (int i = 0; i < 5 && continuationBit; i++)
            {
                if (offset >= buffer.Length)
                {
                    throw new IndexOutOfRangeException("End of buffer reading varint.");
                }
                continuationBit = (buffer[offset] & 0x80) == 0x80;
                value |= (uint)(buffer[offset++] & 0x7f) << shift;
                shift += 7;
            }
            return value;
        }

        public static int CalculateCrcForBatch(this byte[] buffer, int offset, int peOffset)
        {            
            // 1) Extract the exact range we want to CRC:
            int length = peOffset - (offset + 4);
            if (length < 0 || (offset + 4) + length > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(peOffset));

            ReadOnlySpan<byte> crcData = buffer.AsSpan((offset + 4), length);

            // 2) Compute CRC32-C (Castagnoli)
            //    You can either use the instance API:
            var crc = new System.IO.Hashing.Crc32();
            crc.Append(crcData);
            //offset = offset + 4;
            uint checksum = crc.GetCurrentHashAsUInt32();
            return (int)checksum;
            //byte[] result = BitConverter.GetBytes((int)checksum);

            //if(BitConverter.IsLittleEndian)
            //{
            //    Array.Reverse(result); // Ensure big-endian order
            //}

            //return BitConverter.ToInt32(result);
        }


        public static ArraySegment<byte> GetCRCData(this byte[] buffer, int offset, int peOffset)
        {
            int length = peOffset - (offset + 4);
            if (length < 0 || (offset + 4) + length > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(peOffset));

            return new ArraySegment<byte>(buffer, offset + 4, length);
        }

        public static int CalculateCRCforRecordBatch(this ArraySegment<byte> crcData)
        {
            var crc = new System.IO.Hashing.Crc32();
            crc.Append(crcData);
            //offset = offset + 4;
            uint checksum = crc.GetCurrentHashAsUInt32();
            return (int)checksum;
        }

        //public static uint PatchRecordBatchCrc(this byte[] buffer, int batchStartOffset, int batchLength)
        //{
        //    // 1) Read batchLength (INT32 BE) at offset + 8
        //    int lengthOffset = batchStartOffset + 8;
        //    if (lengthOffset + 4 > buffer.Length)
        //        throw new ArgumentOutOfRangeException(nameof(batchStartOffset));

        //    //int batchLength = BinaryPrimitives.ReadInt32BigEndian(
        //    //buffer.AsSpan(lengthOffset, 4));

        //    // 2) Compute offsets of CRC field and CRC data
        //    //    Skip: BaseOffset(8) + BatchLength(4) + LeaderEpoch(4) + Magic(1)
        //    int crcFieldOffset = batchStartOffset + 8 + 4 + 4 + 1;
        //    int crcDataStart = crcFieldOffset + 4;              // skip the CRC itself
        //    int crcDataLen = batchLength - (1 + 4 + 4);           // drop magic + CRC

        //    Console.WriteLine($"Buffer Length : {buffer.Length}");
        //    if (crcDataLen <= 0 || crcDataStart + crcDataLen > buffer.Length)
        //        throw new ArgumentOutOfRangeException(
        //            $"Invalid CRC slice: start={crcDataStart}, len={crcDataLen}");
        //    Console.WriteLine($"Calling Calculate");
        //    return CalculateCrcForBatch(buffer, crcDataStart-4, crcDataStart + crcDataLen);
        //}

        //public static byte[] GetBytes(this int num)
        //{
        //    var result = BitConverter.GetBytes(num);
        //    if(BitConverter.IsLittleEndian)
        //    {
        //        Array.Reverse(result);
        //    }

        //    return result;
        //}

        //public static byte[] GetBytes(this short num)
        //{
        //    var result = BitConverter.GetBytes(num);
        //    if (BitConverter.IsLittleEndian)
        //    {
        //        Array.Reverse(result);
        //    }

        //    return result;
        //}
    }
}
