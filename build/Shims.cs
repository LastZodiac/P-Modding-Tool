// Small stand-ins so BrawlLib builds without NuGet packages or WPF.
// ReadOnlySpan and XxHash64 are only used by TEX0Node/PLT0Node to name textures the way Dolphin does;
// System.Windows.Point only by the model viewer, which this app never opens.
using System.Collections;
using System.Collections.Generic;

namespace System
{
    public readonly struct ReadOnlySpan<T> : IEnumerable<T>
    {
        private readonly T[] _a; private readonly int _s; public int Length { get; }
        public ReadOnlySpan(T[] a, int s, int n) { _a = a ?? new T[0]; _s = s; Length = n; }
        public static implicit operator ReadOnlySpan<T>(T[] a) => new ReadOnlySpan<T>(a, 0, a?.Length ?? 0);
        public T this[int i] => _a[_s + i];
        public ReadOnlySpan<T> Slice(int start, int length) => new ReadOnlySpan<T>(_a, _s + start, length);
        public T[] ToArray() { var r = new T[Length]; Array.Copy(_a, _s, r, 0, Length); return r; }
        public IEnumerator<T> GetEnumerator() { for (int i = 0; i < Length; i++) yield return _a[_s + i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace System.IO.Hashing
{
    public static class XxHash64
    {
        const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL, P3 = 1609587929392839161UL,
                    P4 = 9650029242287828579UL, P5 = 2870177450012600261UL;
        static ulong Rotl(ulong x, int r) => (x << r) | (x >> (64 - r));
        static ulong Round(ulong acc, ulong v) { acc += v * P2; acc = Rotl(acc, 31); return acc * P1; }
        static ulong Merge(ulong acc, ulong v) { acc ^= Round(0, v); return acc * P1 + P4; }
        static ulong R64(byte[] b, int i) => BitConverter.ToUInt64(b, i);
        static uint R32(byte[] b, int i) => BitConverter.ToUInt32(b, i);

        public static ulong HashToUInt64(ReadOnlySpan<byte> span, long seed = 0)
        {
            byte[] d = span.ToArray(); int len = d.Length, p = 0; ulong s = (ulong)seed, h;
            if (len >= 32)
            {
                ulong v1 = s + P1 + P2, v2 = s + P2, v3 = s, v4 = s - P1;
                for (; p <= len - 32; p += 32)
                { v1 = Round(v1, R64(d, p)); v2 = Round(v2, R64(d, p + 8)); v3 = Round(v3, R64(d, p + 16)); v4 = Round(v4, R64(d, p + 24)); }
                h = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
                h = Merge(h, v1); h = Merge(h, v2); h = Merge(h, v3); h = Merge(h, v4);
            }
            else h = s + P5;
            h += (ulong)len;
            for (; p + 8 <= len; p += 8) { h ^= Round(0, R64(d, p)); h = Rotl(h, 27) * P1 + P4; }
            if (p + 4 <= len) { h ^= R32(d, p) * P1; h = Rotl(h, 23) * P2 + P3; p += 4; }
            for (; p < len; p++) { h ^= d[p] * P5; h = Rotl(h, 11) * P1; }
            h ^= h >> 33; h *= P2; h ^= h >> 29; h *= P3; h ^= h >> 32;
            return h;
        }
    }
}

namespace System.Windows
{
    public struct Point
    {
        public double X { get; set; }
        public double Y { get; set; }
        public Point(double x, double y) { X = x; Y = y; }
    }
}
