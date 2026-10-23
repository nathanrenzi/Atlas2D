using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Atlas2D.Audio
{
    public class AudioStream : WaveStream
    {
        public event Action OnStreamEnd;
        public string FileName { get; }
        public AudioVolumeFadeSampleProvider VolumeFadeSampleProvider { get; private set; }
        public ISampleProvider SampleProvider { get; set; }
        public bool EnableLooping { get; set; }
        public override WaveFormat WaveFormat => _sourceStream.WaveFormat;
        public override long Length => _sourceStream.Length;
        public override long Position
        {
            get => _disposed ? 0 : _sourceStream.Position;
            set { if (!_disposed) _sourceStream.Position = value; }
        }
        public bool IsDisposed => _disposed;
        /// <summary>
        /// Controls how quickly spatialization values (volume and panning) track their targets.
        /// Lower values = slower/smoother transitions; higher values = faster response.
        /// Range: 0–1, where 1 means instant (no smoothing).
        /// </summary>
        public float SpatialSmoothingFactor { get; set; } = 0.15f;

        private WaveStream _sourceStream;
        private VolumeSampleProvider _volumeSampleProvider;
        private StereoPanningSampleProvider _panningSampleProvider;

        private float _volume = 1f;
        private float _panning = 0f;
        private float _spatialVolumeMultiplier = 1f;
        private float _spatialPanningAdditive = 0f;
        private float _smoothedSpatialVolumeMultiplier = 1f;
        private float _smoothedSpatialPanningAdditive = 0f;

        private volatile bool _streamEnded;
        private bool _disposed;

        // The input this stream has in the mixer, or null when it isn't in the mixer
        internal ISampleProvider MixerInput;

        public AudioStream(string fileName)
        {
            FileName = fileName;
            _sourceStream = new AudioFileReader(fileName);
            EnableLooping = true;

            // Panning needs stereo, so mono files get turned into stereo
            ISampleProvider source = this.ToSampleProvider();
            if (source.WaveFormat.Channels == 1)
            {
                source = new MonoToStereoSampleProvider(source);
            }
            _volumeSampleProvider = new VolumeSampleProvider(source);
            _panningSampleProvider = new StereoPanningSampleProvider(_volumeSampleProvider);

            VolumeFadeSampleProvider = new AudioVolumeFadeSampleProvider(_panningSampleProvider);
            SampleProvider = VolumeFadeSampleProvider;
        }

        public void SetFadeDirection(int direction)
        {
            if (direction > 0) _streamEnded = false;
            VolumeFadeSampleProvider.SetFadeDirection(direction);
        }

        public void SetFadeTime(float fade) => VolumeFadeSampleProvider.VolumeFadeTime = fade;
        public float GetFadeTime() => VolumeFadeSampleProvider.VolumeFadeTime;

        /// <summary>
        /// Sets the base volume and panning values read during playback.
        /// </summary>
        public void SetAudioValues(float volume, float panning)
        {
            _volume = volume;
            _panning = panning;
        }

        /// <summary>
        /// Sets the spatialization multipliers applied on top of base audio values during playback.
        /// Call <see cref="SnapSpatializationValues"/> first if you want immediate effect with no smoothing lag.
        /// </summary>
        public void SetSpatializationValues(float volumeMultiplier, float panningAdditive)
        {
            _spatialVolumeMultiplier = volumeMultiplier;
            _spatialPanningAdditive = panningAdditive;
        }

        /// <summary>
        /// Instantly snaps the smoothed spatialization values to the current targets, bypassing the lerp.
        /// Useful when teleporting a source to avoid a slow fade from its previous position.
        /// </summary>
        public void SnapSpatializationValues()
        {
            _smoothedSpatialVolumeMultiplier = _spatialVolumeMultiplier;
            _smoothedSpatialPanningAdditive = _spatialPanningAdditive;
        }

        internal void FireStreamEnd() => OnStreamEnd?.Invoke();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    // Out of the mixer first, so the audio thread stops reading the file
                    AudioSystem.RemoveStream(this);
                    _sourceStream.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) return 0;
            if (VolumeFadeSampleProvider.GetCurrentSampleCount() < 0)
            {
                _volumeSampleProvider.Volume = 0f;
                return 0;
            }

            _smoothedSpatialVolumeMultiplier = MathEx.Lerp(_smoothedSpatialVolumeMultiplier, _spatialVolumeMultiplier, SpatialSmoothingFactor);
            _smoothedSpatialPanningAdditive = MathEx.Lerp(_smoothedSpatialPanningAdditive, _spatialPanningAdditive, SpatialSmoothingFactor);
            _volumeSampleProvider.Volume = _volume * _smoothedSpatialVolumeMultiplier;
            _panningSampleProvider.Pan = MathEx.Clamp(_panning + _smoothedSpatialPanningAdditive, -1, 1);

            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                if (bytesRead == 0)
                {
                    if (_sourceStream.Position == 0 || !EnableLooping)
                    {
                        if (!_streamEnded)
                        {
                            _streamEnded = true;
                            AudioSystem.NotifyStreamEnded(this);
                        }
                        break;
                    }
                    _sourceStream.Position = 0;
                    continue;
                }
                totalBytesRead += bytesRead;
            }

            return totalBytesRead;
        }
    }
}
