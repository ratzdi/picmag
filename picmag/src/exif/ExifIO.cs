// MIT License
//
// Copyright (c) 2025 Dimitri Ratz
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;

namespace ExifLib
{
    /// <summary>
    /// Utility to handle multi-byte primitives in both big and little endian.
    /// http://msdn.microsoft.com/en-us/library/system.bitconverter.islittleendian.aspx
    /// http://en.wikipedia.org/wiki/Endianness
    /// </summary>
    public static class ExifIO
    {
        public static short ReadShort(byte[] Data, int offset, bool littleEndian)
        {
            if ((littleEndian && BitConverter.IsLittleEndian) ||
                (!littleEndian && !BitConverter.IsLittleEndian))
            {
                return BitConverter.ToInt16(Data, offset);
            }
            else
            {
                byte[] beBytes = new byte[2] { Data[offset + 1], Data[offset] };
                return BitConverter.ToInt16(beBytes, 0);
            }
        }

        public static ushort ReadUShort(byte[] Data, int offset, bool littleEndian)
        {
            if ((littleEndian && BitConverter.IsLittleEndian) ||
                (!littleEndian && !BitConverter.IsLittleEndian))
            {
                return BitConverter.ToUInt16(Data, offset);
            }
            else
            {
                byte[] beBytes = new byte[2] { Data[offset + 1], Data[offset] };
                return BitConverter.ToUInt16(beBytes, 0);
            }
        }

        public static int ReadInt(byte[] Data, int offset, bool littleEndian)
        {
            if ((littleEndian && BitConverter.IsLittleEndian) ||
                (!littleEndian && !BitConverter.IsLittleEndian))
            {
                return BitConverter.ToInt32(Data, offset);
            }
            else
            {
                byte[] beBytes = new byte[4] { Data[offset + 3], Data[offset + 2], Data[offset + 1], Data[offset] };
                return BitConverter.ToInt32(beBytes, 0);
            }
        }

        public static uint ReadUInt(byte[] Data, int offset, bool littleEndian)
        {
            if ((littleEndian && BitConverter.IsLittleEndian) ||
                (!littleEndian && !BitConverter.IsLittleEndian))
            {
                return BitConverter.ToUInt32(Data, offset);
            }
            else
            {
                byte[] beBytes = new byte[4] { Data[offset + 3], Data[offset + 2], Data[offset + 1], Data[offset] };
                return BitConverter.ToUInt32(beBytes, 0);
            }
        }

        public static float ReadSingle(byte[] Data, int offset, bool littleEndian)
        {
            if ((littleEndian && BitConverter.IsLittleEndian) ||
                (!littleEndian && !BitConverter.IsLittleEndian))
            {
                return BitConverter.ToSingle(Data, offset);
            }
            else
            {
                // need to swap the data first
                byte[] beBytes = new byte[4] { Data[offset + 3], Data[offset + 2], Data[offset + 1], Data[offset] };
                return BitConverter.ToSingle(beBytes, 0);
            }
        }

        public static double ReadDouble(byte[] Data, int offset, bool littleEndian)
        {
            if ((littleEndian && BitConverter.IsLittleEndian) ||
                (!littleEndian && !BitConverter.IsLittleEndian))
            {
                return BitConverter.ToDouble(Data, offset);
            }
            else
            {
                // need to swap the data first
                byte[] beBytes = new byte[8] {
                    Data[offset + 7], Data[offset + 6], Data[offset + 5], Data[offset + 4],
                    Data[offset + 3], Data[offset + 2], Data[offset + 1], Data[offset]};
                return BitConverter.ToDouble(beBytes, 0);
            }
        }
    }
}
