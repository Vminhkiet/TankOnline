// BitPacker.cs — C# port of server's bit_packing library
// Algorithm must match exactly: LSB-first, uint64 scratch, uint32[] words
using System;

namespace TankNet
{
    public sealed class BitWriter
    {
        private readonly uint[] _buf;
        private int    _wordIdx;
        private ulong  _scratch;
        private int    _scratchBits;

        public BitWriter(int maxWords)
        {
            _buf = new uint[maxWords];
        }

        public void WriteBits(ulong value, int numBits)
        {
            value     &= (1UL << numBits) - 1;
            _scratch  |= value << _scratchBits;
            _scratchBits += numBits;
            while (_scratchBits >= 32)
            {
                _buf[_wordIdx++] = (uint)_scratch;
                _scratch       >>= 32;
                _scratchBits    -= 32;
            }
        }

        // serialize_int: write (value - min) using bits_required(min, max) bits
        public void WriteInt(int value, int min, int max)
        {
            int bits = BitsRequired(min, max);
            if (bits > 0) WriteBits((ulong)(value - min), bits);
        }

        public void Flush()
        {
            if (_scratchBits > 0)
            {
                _buf[_wordIdx++] = (uint)(_scratch & 0xFFFFFFFF);
                _scratch = 0; _scratchBits = 0;
            }
        }

        public byte[] ToBytes()
        {
            Flush();
            var result = new byte[_wordIdx * 4];
            Buffer.BlockCopy(_buf, 0, result, 0, result.Length);
            return result;
        }

        public static int BitsRequired(int min, int max)
        {
            if (min == max) return 0;
            uint range = (uint)(max - min);
            int bits = 0;
            while ((1UL << bits) <= range) bits++;
            return bits;
        }
    }

    public sealed class BitReader
    {
        private readonly uint[] _buf;
        private int   _wordIdx;
        private ulong _scratch;
        private int   _scratchBits;
        private int   _totalBytes;
        public  bool  IsError { get; private set; }

        public BitReader(byte[] data)
        {
            _totalBytes = data.Length;
            _buf = new uint[(data.Length + 3) / 4];
            Buffer.BlockCopy(data, 0, _buf, 0, data.Length);
        }

        public ulong ReadBits(int numBits)
        {
            if (IsError || numBits <= 0 || numBits > 64) return 0;
            while (_scratchBits < numBits)
            {
                if (_wordIdx * 4 >= _totalBytes) { IsError = true; return 0; }
                _scratch     |= (ulong)_buf[_wordIdx] << _scratchBits;
                _scratchBits += 32;
                _wordIdx++;
            }
            ulong result  = _scratch & ((1UL << numBits) - 1);
            _scratch     >>= numBits;
            _scratchBits  -= numBits;
            return result;
        }

        public int ReadInt(int min, int max)
        {
            int bits = BitWriter.BitsRequired(min, max);
            return bits > 0 ? (int)ReadBits(bits) + min : min;
        }
    }
}
