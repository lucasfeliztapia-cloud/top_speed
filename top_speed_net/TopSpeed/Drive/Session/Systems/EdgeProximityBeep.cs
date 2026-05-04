using System;
using TopSpeed.Audio;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Tracks;
using TopSpeed.Vehicles;

namespace TopSpeed.Drive.Session.Systems
{
    internal sealed class EdgeProximityBeep : Subsystem
    {
        // Center 30% of road is silent (dead zone = 0.15 from center on each side)
        private const float DeadZone = 0.15f;
        private const double MinFrequencyHz = 220.0;
        private const double MaxFrequencyHz = 900.0;
        private const float MinIntervalSeconds = 0.05f;
        private const float MaxIntervalSeconds = 2.0f;
        private const int ToneDurationMs = 40;
        private const float ToneVolume = 0.28f;

        private readonly ICar _car;
        private readonly Track _track;
        private readonly IGameAudio _audio;
        private readonly DriveSettings _settings;
        private readonly Func<bool> _isActive;
        private float _timer;

        public EdgeProximityBeep(
            string name,
            int order,
            ICar car,
            Track track,
            IGameAudio audio,
            DriveSettings settings,
            Func<bool> isActive)
            : base(name, order)
        {
            _car = car ?? throw new ArgumentNullException(nameof(car));
            _track = track ?? throw new ArgumentNullException(nameof(track));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        }

        public override void Update(SessionContext context, float elapsed)
        {
            if (!_settings.WallProximityFeedback || !_isActive())
            {
                _timer = 0f;
                return;
            }

            _timer += elapsed;

            var road = _track.RoadAtPosition(_car.PositionY);
            var roadWidth = road.Right - road.Left;
            if (roadWidth <= 0f)
                return;

            // relPos: 0 = left edge, 0.5 = center, 1 = right edge
            var relPos = (_car.PositionX - road.Left) / roadWidth;
            relPos = Math.Clamp(relPos, 0f, 1f);

            // distToCenter: 0 = at center, 0.5 = at edge
            var distToCenter = Math.Abs(relPos - 0.5f);

            // Only beep outside the dead zone
            if (distToCenter <= DeadZone)
            {
                _timer = 0f;
                return;
            }

            // proximity: 0 = just past dead zone, 1 = at edge
            var rawProximity = (distToCenter - DeadZone) / (0.5f - DeadZone);

            // Scale by curve type so tight curves warn earlier
            var curved = Math.Clamp(rawProximity * GetCurveMultiplier(road.Type), 0f, 1f);

            var intervalSeconds = MaxIntervalSeconds - (curved * (MaxIntervalSeconds - MinIntervalSeconds));
            if (_timer < intervalSeconds)
                return;

            _timer = 0f;

            var frequency = MinFrequencyHz + (curved * (MaxFrequencyHz - MinFrequencyHz));

            // Pan: left half → pan left, right half → pan right
            var pan = relPos < 0.5f ? -curved : curved;

            _audio.PlayTriangleTone(frequency, ToneDurationMs, ToneVolume, (float)pan);
        }

        private static float GetCurveMultiplier(TrackType type) => type switch
        {
            TrackType.Left or TrackType.Right => 1.4f,
            TrackType.HardLeft or TrackType.HardRight => 1.7f,
            TrackType.HairpinLeft or TrackType.HairpinRight => 2.0f,
            _ => 1.0f
        };
    }
}
