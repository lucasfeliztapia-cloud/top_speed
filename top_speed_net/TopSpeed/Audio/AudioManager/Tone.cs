using System;
using System.IO;
using System.Text;
using TS.Audio;

namespace TopSpeed.Audio
{
    internal sealed partial class AudioManager
    {
        public void PlayTriangleTone(double frequencyHz, int durationMs, float volume = 0.35f, float pan = 0f)
        {
            if (frequencyHz <= 0d || durationMs <= 0)
                return;

            var sampleRate = _engine.PrimaryOutput.SampleRate > 0 ? _engine.PrimaryOutput.SampleRate : 44100;
            var samplesPerCycle = Math.Max(1, (int)Math.Round(sampleRate / frequencyHz));
            var totalFrames = (int)((sampleRate * durationMs) / 1000.0);
            if (totalFrames <= 0)
                return;
            var remainder = totalFrames % samplesPerCycle;
            if (remainder != 0)
                totalFrames += samplesPerCycle - remainder;

            var clampedPan = Math.Clamp(pan, -1f, 1f);
            var wave = BuildTriangleToneWave(sampleRate, samplesPerCycle, totalFrames);
            var asset = _engine.CreateBufferAsset(wave, "ui-tone");
            _engine.PlayOneShot(asset, AudioEngineOptions.UiBusName, configure: source =>
            {
                source.SetVolume(volume);
                if (clampedPan != 0f)
                    source.SetPan(clampedPan);
            }, spatialize: false, useHrtf: false);
            asset.Dispose();
        }

        private static byte[] BuildTriangleToneWave(int sampleRate, int samplesPerCycle, int totalFrames)
        {
            using var stream = new MemoryStream(44 + (totalFrames * sizeof(short)));
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

            const short channels = 1;
            const short bitsPerSample = 16;
            var blockAlign = (short)(channels * (bitsPerSample / 8));
            var byteRate = sampleRate * blockAlign;
            var dataLength = totalFrames * blockAlign;

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            for (var frame = 0; frame < totalFrames; frame++)
            {
                var phase = (double)(frame % samplesPerCycle) / samplesPerCycle;
                double triangle;
                if (phase < 0.25d)
                {
                    triangle = phase * 4.0d;
                }
                else if (phase < 0.75d)
                {
                    triangle = 2.0d - (phase * 4.0d);
                }
                else
                {
                    triangle = (phase * 4.0d) - 4.0d;
                }

                var sample = (short)Math.Clamp((int)Math.Round(triangle * 0.65d * short.MaxValue), short.MinValue, short.MaxValue);
                writer.Write(sample);
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}

