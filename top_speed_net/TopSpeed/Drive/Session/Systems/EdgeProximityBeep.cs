using System;
using TopSpeed.Audio;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Tracks;
using TopSpeed.Vehicles;
using TS.Audio;

namespace TopSpeed.Drive.Session.Systems
{
    internal sealed class EdgeProximityBeep : Subsystem, IDisposable
    {
        // Center 30% of road is silent (dead zone = 0.15 from center on each side)
        private const float DeadZone = 0.15f;
        private const double MinFrequencyHz = 150.0;
        private const double MaxFrequencyHz = 650.0;
        private const float MaxToneVolume = 0.20f;
        private const uint ToneSampleRate = 44100;

        private readonly ICar _car;
        private readonly Track _track;
        private readonly DriveSettings _settings;
        private readonly Func<bool> _isActive;

        // Desired frequency for each side.
        // Written by Update() on the game thread, read by audio callbacks on the audio thread.
        // volatile ensures the audio thread always sees the latest value.
        private volatile double _leftFrequencyHz = MinFrequencyHz;
        private volatile double _rightFrequencyHz = MinFrequencyHz;

        // Phase accumulators for sine generation — only ever touched by the audio callback thread.
        private double _leftPhase;
        private double _rightPhase;

        private readonly Source _leftSource;
        private readonly Source _rightSource;
        private bool _disposed;

        public EdgeProximityBeep(
            string name,
            int order,
            ICar car,
            Track track,
            AudioManager audio,
            DriveSettings settings,
            Func<bool> isActive)
            : base(name, order)
        {
            _car = car ?? throw new ArgumentNullException(nameof(car));
            _track = track ?? throw new ArgumentNullException(nameof(track));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));

            if (audio == null) throw new ArgumentNullException(nameof(audio));

            // Two mono procedural sources, one per ear.
            _leftSource = audio.CreateProceduralSource(RenderLeft, 1, ToneSampleRate, "ui", spatialize: false, useHrtf: false);
            _rightSource = audio.CreateProceduralSource(RenderRight, 1, ToneSampleRate, "ui", spatialize: false, useHrtf: false);

            _leftSource.SetPan(-1f);
            _rightSource.SetPan(1f);
            _leftSource.SetVolume(0f);
            _rightSource.SetVolume(0f);

            // Start looping immediately at zero volume; volume is raised by Update() as needed.
            _leftSource.Play(loop: true);
            _rightSource.Play(loop: true);
        }

        public override void Update(SessionContext context, float elapsed)
        {
            if (!_settings.WallProximityFeedback || !_isActive())
            {
                _leftSource.SetVolume(0f);
                _rightSource.SetVolume(0f);
                return;
            }

            var road = _track.RoadAtPosition(_car.PositionY);
            var roadWidth = road.Right - road.Left;
            if (roadWidth <= 0f)
            {
                _leftSource.SetVolume(0f);
                _rightSource.SetVolume(0f);
                return;
            }

            // relPos: 0 = left edge, 0.5 = center, 1 = right edge
            var relPos = Math.Clamp((_car.PositionX - road.Left) / roadWidth, 0f, 1f);

            // distToCenter: 0 = at center, 0.5 = at edge
            var distToCenter = Math.Abs(relPos - 0.5f);

            if (distToCenter <= DeadZone)
            {
                _leftSource.SetVolume(0f);
                _rightSource.SetVolume(0f);
                return;
            }

            // proximity: 0 = just past dead zone, 1 = at edge
            var rawProximity = (distToCenter - DeadZone) / (0.5f - DeadZone);

            // Scale by curve type so tight curves warn earlier
            var curved = (float)Math.Clamp(rawProximity * GetCurveMultiplier(road.Type), 0f, 1f);

            var frequency = MinFrequencyHz + (curved * (MaxFrequencyHz - MinFrequencyHz));
            var volume = curved * MaxToneVolume;

            if (relPos < 0.5f) // approaching left wall
            {
                _leftFrequencyHz = frequency;
                _leftSource.SetVolume(volume);
                _rightSource.SetVolume(0f);
            }
            else // approaching right wall
            {
                _rightFrequencyHz = frequency;
                _rightSource.SetVolume(volume);
                _leftSource.SetVolume(0f);
            }
        }

        // Audio callback for the left ear — runs on the audio thread.
        private void RenderLeft(float[] buffer, int frames, int channels, ref ulong frameIndex)
        {
            var freq = _leftFrequencyHz;
            for (var i = 0; i < frames; i++)
            {
                var sample = (float)Math.Sin(2.0 * Math.PI * _leftPhase);
                for (var c = 0; c < channels; c++)
                    buffer[i * channels + c] = sample;
                _leftPhase += freq / ToneSampleRate;
                if (_leftPhase >= 1.0) _leftPhase -= 1.0;
            }
        }

        // Audio callback for the right ear — runs on the audio thread.
        private void RenderRight(float[] buffer, int frames, int channels, ref ulong frameIndex)
        {
            var freq = _rightFrequencyHz;
            for (var i = 0; i < frames; i++)
            {
                var sample = (float)Math.Sin(2.0 * Math.PI * _rightPhase);
                for (var c = 0; c < channels; c++)
                    buffer[i * channels + c] = sample;
                _rightPhase += freq / ToneSampleRate;
                if (_rightPhase >= 1.0) _rightPhase -= 1.0;
            }
        }

        private static float GetCurveMultiplier(TrackType type) => type switch
        {
            TrackType.Left or TrackType.Right => 1.4f,
            TrackType.HardLeft or TrackType.HardRight => 1.7f,
            TrackType.HairpinLeft or TrackType.HairpinRight => 2.0f,
            _ => 1.0f
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _leftSource.Stop();
            _leftSource.Dispose();
            _rightSource.Stop();
            _rightSource.Dispose();
        }
    }
}
