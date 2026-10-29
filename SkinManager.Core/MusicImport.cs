// Your own music: an MP3 or WAV file made into a song the game can play (a BRSTM, the Wii's streamed music
// format): decoded (MP3 by NLayer), made stereo, resampled (32 kHz like P+'s own songs, or 44.1 kHz), quiet
// ends trimmed, evened out in loudness, then encoded with BrawlLib's BRSTM encoder with a loop point.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BrawlLib.Internal;
using BrawlLib.Internal.Audio;
using BrawlLib.Internal.IO;
using BrawlLib.Internal.Windows.Forms;
using BrawlLib.Wii.Audio;

namespace SkinManager.Core
{
    public sealed class AudioClip
    {
        public string Path, Title, Artist;
        public int Rate, Channels;          // after loading: as the file is; Channels is always 2 after Prepare
        public float[] Samples;             // interleaved, -1..1
        public int Frames => Channels == 0 ? 0 : Samples.Length / Channels;
        public TimeSpan Duration => TimeSpan.FromSeconds(Rate == 0 ? 0 : (double)Frames / Rate);
    }

    public sealed class MusicOptions
    {
        public int Rate = 32000;            // 32000 (P+'s own songs) or 44100 (better, bigger)
        public bool Loop = true;
        public double LoopStartSeconds;     // where it loops back to (0 = the start)
        public double EndSeconds;           // 0 = the end of the song
        public double VolumeDb;             // added after evening out (−6..+6)
        public bool Even = true;            // even out loudness with P+'s songs
        public bool Trim = true;            // cut quiet bits at the start and end
    }

    public static class MusicImport
    {
        public static readonly string[] Extensions = { ".mp3", ".wav" };
        public static bool IsAudio(string path) => Extensions.Contains(System.IO.Path.GetExtension(path ?? "").ToLowerInvariant());

        /// <summary>Reads an audio file (MP3 or WAV).</summary>
        public static AudioClip Load(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var clip = ext == ".wav" ? LoadWav(path) : LoadMp3(path);
            clip.Path = path;
            if (string.IsNullOrWhiteSpace(clip.Title)) clip.Title = CleanName(System.IO.Path.GetFileNameWithoutExtension(path));
            return clip;
        }

        /// <summary>"(free) cool song (prod x) [Official Audio]" → a tidier title.</summary>
        public static string CleanName(string n)
        {
            n = System.Text.RegularExpressions.Regex.Replace(n, @"[\[(](official|audio|lyrics?|video|hq|hd|ost|music video)[^\])]*[\])]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            n = n.Replace('_', ' ');
            n = System.Text.RegularExpressions.Regex.Replace(n, @"\s+", " ").Trim(' ', '-');
            return n.Length > 0 ? n : "My song";
        }

        static AudioClip LoadMp3(string path)
        {
            var clip = new AudioClip();
            ReadId3(path, clip);
            using (var mp3 = new NLayer.MpegFile(path))
            {
                clip.Rate = mp3.SampleRate; clip.Channels = mp3.Channels;
                var all = new List<float>(mp3.Length > 0 ? (int)Math.Min(int.MaxValue / 2, mp3.Length / 4) : 1 << 20);
                var buf = new float[clip.Channels * 4096];
                int n;
                while ((n = mp3.ReadSamples(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(buf[i]);
                clip.Samples = all.ToArray();
            }
            if (clip.Samples.Length == 0) throw new InvalidDataException("No sound could be read from this MP3.");
            return clip;
        }

        // title (TIT2) and artist (TPE1) from an ID3v2 tag, if there is one
        static void ReadId3(string path, AudioClip clip)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var h = new byte[10];
                    if (f.Read(h, 0, 10) < 10 || h[0] != 'I' || h[1] != 'D' || h[2] != '3') return;
                    int ver = h[3];
                    int size = (h[6] & 0x7F) << 21 | (h[7] & 0x7F) << 14 | (h[8] & 0x7F) << 7 | (h[9] & 0x7F);
                    var tag = new byte[Math.Min(size, 1 << 20)];
                    f.Read(tag, 0, tag.Length);
                    int at = 0;
                    while (at + 10 <= tag.Length)
                    {
                        string id = Encoding.ASCII.GetString(tag, at, 4);
                        if (id[0] == 0) break;
                        int len = ver >= 4 ? (tag[at + 4] & 0x7F) << 21 | (tag[at + 5] & 0x7F) << 14 | (tag[at + 6] & 0x7F) << 7 | (tag[at + 7] & 0x7F)
                                           : tag[at + 4] << 24 | tag[at + 5] << 16 | tag[at + 6] << 8 | tag[at + 7];
                        if (len <= 0 || at + 10 + len > tag.Length) break;
                        if (id == "TIT2" || id == "TPE1")
                        {
                            int enc = tag[at + 10];
                            var e = enc == 1 ? Encoding.Unicode : enc == 2 ? Encoding.BigEndianUnicode : enc == 3 ? Encoding.UTF8 : Encoding.GetEncoding(28591);
                            string text = e.GetString(tag, at + 11, len - 1).Trim('\0', '﻿', ' ');
                            if (id == "TIT2") clip.Title = text; else clip.Artist = text;
                        }
                        at += 10 + len;
                    }
                }
            }
            catch { }
        }

        static AudioClip LoadWav(string path)
        {
            var b = File.ReadAllBytes(path);
            if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE") throw new InvalidDataException("That isn't a WAV file.");
            int fmt = 0, ch = 0, rate = 0, bits = 0, at = 12, dataAt = -1, dataLen = 0;
            while (at + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, at, 4); int len = BitConverter.ToInt32(b, at + 4);
                if (id == "fmt ") { fmt = BitConverter.ToUInt16(b, at + 8); ch = BitConverter.ToUInt16(b, at + 10); rate = BitConverter.ToInt32(b, at + 12); bits = BitConverter.ToUInt16(b, at + 22); if (fmt == 0xFFFE && len >= 40) fmt = BitConverter.ToUInt16(b, at + 32); }
                if (id == "data") { dataAt = at + 8; dataLen = Math.Min(len < 0 ? int.MaxValue : len, b.Length - dataAt); break; }
                at += 8 + len + (len & 1);
            }
            if (dataAt < 0 || ch == 0) throw new InvalidDataException("This WAV file has no sound in it.");
            int bytes = bits / 8, count = dataLen / bytes;
            var s = new float[count];
            for (int i = 0; i < count; i++)
            {
                int p = dataAt + i * bytes;
                s[i] = fmt == 3 && bits == 32 ? BitConverter.ToSingle(b, p)
                    : bits == 16 ? BitConverter.ToInt16(b, p) / 32768f
                    : bits == 24 ? ((b[p] | b[p + 1] << 8 | (sbyte)b[p + 2] << 16) / 8388608f)
                    : bits == 32 ? BitConverter.ToInt32(b, p) / 2147483648f
                    : bits == 8 ? (b[p] - 128) / 128f : 0;
            }
            return new AudioClip { Rate = rate, Channels = ch, Samples = s };
        }

        /// <summary>Stereo, the target rate, trimmed and evened out. Returns 16-bit stereo samples.</summary>
        public static short[] Prepare(AudioClip clip, MusicOptions o, out int frames)
        {
            // stereo
            int n = clip.Frames;
            var l = new float[n]; var r = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (clip.Channels == 1) l[i] = r[i] = clip.Samples[i];
                else { l[i] = clip.Samples[i * clip.Channels]; r[i] = clip.Samples[i * clip.Channels + 1]; }
            }
            // the part to keep
            int start = 0, end = n;
            if (o.EndSeconds > 0) end = Math.Min(n, (int)(o.EndSeconds * clip.Rate));
            if (o.Trim)
            {
                const float quiet = 0.001f;   // about −60 dB
                while (start < end && Math.Abs(l[start]) < quiet && Math.Abs(r[start]) < quiet) start++;
                while (end > start && Math.Abs(l[end - 1]) < quiet && Math.Abs(r[end - 1]) < quiet) end--;
            }
            if (end - start < clip.Rate) throw new InvalidDataException("This song is too short (less than a second of sound).");
            int loopFrom = (int)(o.LoopStartSeconds * clip.Rate);   // in the original's frames
            // resample
            var L = Resample(l, start, end, clip.Rate, o.Rate);
            var R = Resample(r, start, end, clip.Rate, o.Rate);
            frames = L.Length;
            // loudness: an even RMS like P+'s songs, never clipping
            float gain = 1;
            if (o.Even)
            {
                double sum = 0; float peak = 0;
                for (int i = 0; i < frames; i++) { sum += L[i] * L[i] + R[i] * R[i]; peak = Math.Max(peak, Math.Max(Math.Abs(L[i]), Math.Abs(R[i]))); }
                double rms = Math.Sqrt(sum / (2.0 * Math.Max(1, frames)));
                double target = Math.Pow(10, -18 / 20.0);
                gain = rms > 1e-6 ? (float)(target / rms) : 1;
                if (peak * gain > 0.97f) gain = 0.97f / Math.Max(1e-6f, peak);
            }
            gain *= (float)Math.Pow(10, o.VolumeDb / 20.0);
            var pcm = new short[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                pcm[i * 2] = ToShort(L[i] * gain);
                pcm[i * 2 + 1] = ToShort(R[i] * gain);
            }
            return pcm;
        }

        static short ToShort(float v) { v = Math.Max(-1, Math.Min(1, v)); return (short)Math.Round(v * 32767); }

        /// <summary>Windowed-sinc resampling (good quality, no aliasing when going down).</summary>
        static float[] Resample(float[] x, int start, int end, int from, int to)
        {
            int n = end - start;
            if (from == to) { var c = new float[n]; Array.Copy(x, start, c, 0, n); return c; }
            double ratio = (double)to / from;
            int outN = (int)Math.Floor(n * ratio);
            var y = new float[outN];
            double cutoff = Math.Min(1.0, ratio) * 0.95;   // below the new Nyquist
            const int half = 16;
            System.Threading.Tasks.Parallel.For(0, (outN + 4095) / 4096, block =>
            {
                for (int k = block * 4096; k < Math.Min(outN, block * 4096 + 4096); k++)
                {
                    double pos = k / ratio;
                    int c = (int)Math.Floor(pos);
                    double sum = 0, wsum = 0;
                    for (int j = c - half + 1; j <= c + half; j++)
                    {
                        if (j < 0 || j >= n) continue;
                        double t = pos - j;
                        double sinc = Math.Abs(t) < 1e-9 ? 1 : Math.Sin(Math.PI * t * cutoff) / (Math.PI * t * cutoff);
                        double w = 0.5 + 0.5 * Math.Cos(Math.PI * t / half);   // Hann window
                        double v = sinc * w;
                        sum += x[start + j] * v; wsum += v;
                    }
                    y[k] = (float)(wsum > 1e-9 ? sum / wsum : 0);
                }
            });
            return y;
        }

        /// <summary>The finished song file (BRSTM) and how long it is. progress: 0..1.</summary>
        public static byte[] MakeBrstm(AudioClip clip, MusicOptions o, Action<float> progress = null)
        {
            var pcm = Prepare(clip, o, out int frames);
            int loopStart = o.Loop ? LoopFrame(clip, o, frames) : 0;
            using (var stream = new PcmStream(pcm, 2, o.Rate) { IsLooping = o.Loop, LoopStartSample = loopStart, LoopEndSample = frames })
            {
                var tracker = new Tracker(progress);
                FileMap map;
                lock (BrawlGate.Lock) map = RSTMConverter.Encode(stream, tracker);
                using (map)
                {
                    var bytes = new byte[map.Length];
                    System.Runtime.InteropServices.Marshal.Copy(map.Address, bytes, 0, bytes.Length);
                    return bytes;
                }
            }
        }

        /// <summary>The loop point in the finished song's frames (after the quiet start is cut, at the new rate).</summary>
        static int LoopFrame(AudioClip clip, MusicOptions o, int frames)
        {
            int trimmed = 0;
            if (o.Trim)
            {
                const float quiet = 0.001f;
                int n = clip.Frames;
                while (trimmed < n)
                {
                    bool loud = false;
                    for (int c = 0; c < clip.Channels; c++) if (Math.Abs(clip.Samples[trimmed * clip.Channels + c]) >= quiet) { loud = true; break; }
                    if (loud) break;
                    trimmed++;
                }
            }
            int f = Math.Max(0, (int)((o.LoopStartSeconds * clip.Rate - trimmed) * (double)o.Rate / clip.Rate));
            return f >= frames - o.Rate ? 0 : f;   // a loop point at the very end makes no sense: the whole song
        }

        /// <summary>A short WAV to listen to: the last seconds before the loop's end, then the loop's start (does the loop sound right?).</summary>
        public static byte[] LoopPreview(AudioClip clip, MusicOptions o, double seconds = 4)
        {
            var pcm = Prepare(clip, o, out int frames);
            int loopStart = LoopFrame(clip, o, frames);
            int take = (int)(seconds * o.Rate);
            var parts = new List<short>();
            for (int i = Math.Max(0, frames - take); i < frames; i++) { parts.Add(pcm[i * 2]); parts.Add(pcm[i * 2 + 1]); }
            for (int i = loopStart; i < Math.Min(frames, loopStart + take); i++) { parts.Add(pcm[i * 2]); parts.Add(pcm[i * 2 + 1]); }
            return Wav(parts.ToArray(), o.Rate);
        }

        /// <summary>The start of the song as it will sound (for a quick listen).</summary>
        public static byte[] Preview(AudioClip clip, MusicOptions o, double seconds = 12)
        {
            var pcm = Prepare(clip, o, out int frames);
            int take = Math.Min(frames, (int)(seconds * o.Rate));
            var part = new short[take * 2]; Array.Copy(pcm, part, part.Length);
            return Wav(part, o.Rate);
        }

        static byte[] Wav(short[] stereo, int rate)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int data = stereo.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data);
                foreach (var v in stereo) w.Write(v);
                return ms.ToArray();
            }
        }

        sealed class Tracker : IProgressTracker
        {
            readonly Action<float> _to;
            public Tracker(Action<float> to) { _to = to; }
            public float MinValue { get; set; }
            public float MaxValue { get; set; }
            public float CurrentValue { get; set; }
            public bool Cancelled { get; set; }
            public void Begin(float min, float max, float current) { MinValue = min; MaxValue = max; Update(current); }
            public void Update(float value) { CurrentValue = value; if (MaxValue > MinValue) _to?.Invoke(Math.Max(0, Math.Min(1, (value - MinValue) / (MaxValue - MinValue)))); }
            public void Finish() { _to?.Invoke(1); }
            public void Cancel() { Cancelled = true; }
        }

        /// <summary>16-bit interleaved samples as BrawlLib's audio stream.</summary>
        sealed unsafe class PcmStream : IAudioStream
        {
            readonly short[] _pcm; readonly int _ch, _rate;
            public PcmStream(short[] pcm, int channels, int rate) { _pcm = pcm; _ch = channels; _rate = rate; }
            public WaveFormatTag Format => WaveFormatTag.WAVE_FORMAT_PCM;
            public int BitsPerSample => 16;
            public int Samples => _pcm.Length / _ch;
            public int Channels => _ch;
            public int Frequency => _rate;
            public bool IsLooping { get; set; }
            public int LoopStartSample { get; set; }
            public int LoopEndSample { get; set; }
            public int SamplePosition { get; set; }
            public int ReadSamples(VoidPtr destAddr, int numSamples)
            {
                int n = Math.Max(0, Math.Min(numSamples, Samples - SamplePosition));
                short* d = (short*)destAddr;
                int from = SamplePosition * _ch;
                for (int i = 0; i < n * _ch; i++) d[i] = _pcm[from + i];
                SamplePosition += n;
                return n;
            }
            public void Wrap() { SamplePosition = LoopStartSample; }
            public void Dispose() { }
        }
    }
}
