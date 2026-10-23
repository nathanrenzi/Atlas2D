namespace Atlas2D.Audio
{
    public class AudioInstance : IDisposable
    {
        public event Action OnFadeInEnd;
        public event Action OnFadeOutEnd;

        public AudioStream? Stream
        {
            get => _stream;
            set
            {
                _stream = value;
                _stream?.SetAudioValues(_volume, _panning);
            }
        }
        private AudioStream? _stream;

        public PlaybackState PlaybackState { get; private set; }
        public float StartStopVolumeFadeTime { get; private set; }

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = value;
                Stream?.SetAudioValues(_volume, _panning);
            }
        }
        private float _volume = 1f;

        public float Panning
        {
            get => _panning;
            set
            {
                _panning = value;
                Stream?.SetAudioValues(_volume, _panning);
            }
        }
        private float _panning = 0f;

        public float Pitch { get; set; }

        public List<IAudioEffect> Effects { get; } = new List<IAudioEffect>();

        public bool IsLoop
        {
            get => Stream?.EnableLooping ?? false;
            set { if (Stream != null) Stream.EnableLooping = value; }
        }

        public AudioInstance(string fileName, float volume, float pitch, bool isLoop, float startStopVolumeFadeTime = 0.001f)
        {
            Pitch = pitch;
            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException(fileName);
            }
            Stream = new AudioStream(fileName);
            Volume = volume;
            IsLoop = isLoop;
            StartStopVolumeFadeTime = startStopVolumeFadeTime;
            HookStreamEvents();
        }

        public AudioInstance(AudioStream? stream, float volume, float pitch, bool isLoop, float startStopVolumeFadeTime = 0.001f)
        {
            Pitch = pitch;
            Stream = stream;
            Volume = volume;
            IsLoop = isLoop;
            StartStopVolumeFadeTime = startStopVolumeFadeTime;
            HookStreamEvents();
        }

        private void HookStreamEvents()
        {
            if (Stream == null) return;
            Stream.OnStreamEnd += Stop;
            Stream.VolumeFadeSampleProvider.OnFadeInEnd += FireFadeInEnd;
            Stream.VolumeFadeSampleProvider.OnFadeOutEnd += FireFadeOutEnd;
            Stream.SetFadeTime(StartStopVolumeFadeTime);
        }

        private void UnhookStreamEvents()
        {
            if (Stream == null) return;
            Stream.OnStreamEnd -= Stop;
            Stream.VolumeFadeSampleProvider.OnFadeInEnd -= FireFadeInEnd;
            Stream.VolumeFadeSampleProvider.OnFadeOutEnd -= FireFadeOutEnd;
        }

        private void FireFadeInEnd() => OnFadeInEnd?.Invoke();
        private void FireFadeOutEnd() => OnFadeOutEnd?.Invoke();

        /// <summary>
        /// Swaps the stream this instance plays. The old stream isn't disposed.
        /// </summary>

        public void SetAudioStream(AudioStream stream)
        {
            if (stream == null)
            {
                Debug.LogError("AudioStream is null, cannot set!");
                return;
            }
            if (Stream != null && !Stream.WaveFormat.Equals(stream.WaveFormat))
            {
                Debug.LogError("Cannot set audio stream. WaveFormat does not match the WaveFormat of the current stream." +
                    "Check the sample rate and other settings to make sure they match.");
                return;
            }
            if (stream == Stream) return;
            bool shouldPlay = PlaybackState == PlaybackState.Playing;
            if (shouldPlay) Stop();

            // Takes the old stream out of the mixer without closing it
            if (Stream != null)
            {
                UnhookStreamEvents();
                AudioSystem.RemoveStream(Stream);
            }
            Stream = stream;
            Stream.EnableLooping = IsLoop;
            HookStreamEvents();

            if (shouldPlay) Play();
        }

        public void AddEffect(IAudioEffect effect)
        {
            if (Stream == null) return;
            effect.Initialize(Stream.SampleProvider);
            Stream.SampleProvider = effect;
            Effects.Add(effect);
        }

        public void Play()
        {
            if (Stream == null || Stream.IsDisposed) return;
            Stream.SetFadeDirection(1);
            Stream.Position = 0;
            PlaybackState = PlaybackState.Playing;
            AudioSystem.PlayAudioInstance(this);
        }

        public void Play(long position)
        {
            if (Stream == null || Stream.IsDisposed) return;
            Stream.SetFadeDirection(1);
            Stream.Position = IsLoop ? position % Stream.Length : Math.Min(position, Stream.Length);
            PlaybackState = PlaybackState.Playing;
            AudioSystem.PlayAudioInstance(this);
        }

        public void Pause()
        {
            if (Stream == null) return;
            if (PlaybackState == PlaybackState.Stopped) return;
            Stream.SetFadeDirection(-1);
            PlaybackState = PlaybackState.Paused;
        }

        public void Unpause()
        {
            if (Stream == null || Stream.IsDisposed) return;
            if (PlaybackState == PlaybackState.Stopped || PlaybackState == PlaybackState.Playing) return;
            Stream.SetFadeDirection(1);
            PlaybackState = PlaybackState.Playing;
            AudioSystem.PlayAudioInstance(this);
        }

        public void Stop()
        {
            if (Stream == null) return;
            Stream.SetFadeDirection(-1);
            PlaybackState = PlaybackState.Stopped;
        }

        public void Dispose()
        {
            PlaybackState = PlaybackState.Stopped;
            Stream?.Dispose();
        }
    }

    public enum PlaybackState
    {
        Stopped,
        Paused,
        Playing
    }
}
