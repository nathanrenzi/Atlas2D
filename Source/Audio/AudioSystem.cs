using NAudio.Wave.SampleProviders;
using NAudio.Wave;
using System.Collections.Concurrent;

namespace Atlas2D.Audio
{
    public static class AudioSystem
    {
        public static float MasterVolume
        {
            get => _masterVolumeSampleProvider.Volume;
            set => _masterVolumeSampleProvider.Volume = MathEx.Clamp(value, 0, 2);
        }

        private static IWavePlayer _outputDevice;
        private static MixingSampleProvider _mixer;
        private static VolumeSampleProvider _masterVolumeSampleProvider;
        private static readonly ConcurrentQueue<AudioStream> _streamEndedQueue = new();
        private static readonly ConcurrentDictionary<ISampleProvider, AudioStream> _inputStreams = new();

        public static void Initialize(float masterVolume, int sampleRate = 44100, int channelCount = 2)
        {
            _outputDevice = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 0);
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount));
            _mixer.ReadFully = true;
            _mixer.MixerInputEnded += OnMixerInputEnded;
            _masterVolumeSampleProvider = new VolumeSampleProvider(_mixer);
            MasterVolume = masterVolume;
            _outputDevice.Init(_masterVolumeSampleProvider);
            _outputDevice.Play();
        }

        /// <summary>
        /// Fires pending stream-end events on the main thread. Must be called once per frame.
        /// </summary>
        public static void Update()
        {
            while (_streamEndedQueue.TryDequeue(out AudioStream stream))
            {
                stream.FireStreamEnd();
            }
        }

        public static AudioInstance CreateInstance(string fileName, float volume = 1, float pitch = 1, bool isLoop = false)
        {
            var stream = new AudioStream(fileName);
            var instance = new AudioInstance(stream, volume, pitch, isLoop);
            return instance;
        }

        public static void PlayAudioInstance(AudioInstance audioInstance)
        {
            AudioStream stream = audioInstance.Stream;
            if (stream == null || stream.MixerInput != null) return;

            // Mono sounds get wrapped, so remember the exact input that went into the mixer
            ISampleProvider input = ConvertToMixerFormat(stream.SampleProvider);
            stream.MixerInput = input;
            _inputStreams[input] = stream;
            _mixer.AddMixerInput(input);
        }

        /// <summary>
        /// Stops a stream right away by taking it out of the mixer.
        /// </summary>
        internal static void RemoveStream(AudioStream stream)
        {
            ISampleProvider input = Interlocked.Exchange(ref stream.MixerInput, null);
            if (input == null) return;
            _inputStreams.TryRemove(input, out _);
            _mixer?.RemoveMixerInput(input);
        }

        // Runs on the audio thread when the mixer drops a finished input
        private static void OnMixerInputEnded(object sender, SampleProviderEventArgs e)
        {
            if (_inputStreams.TryRemove(e.SampleProvider, out AudioStream stream))
            {
                Interlocked.CompareExchange(ref stream.MixerInput, null, e.SampleProvider);
            }
        }

        internal static void NotifyStreamEnded(AudioStream stream)
        {
            _streamEndedQueue.Enqueue(stream);
        }

        private static ISampleProvider ConvertToMixerFormat(ISampleProvider input)
        {
            // The mixer only accepts inputs with its own sample rate
            if (input.WaveFormat.SampleRate != _mixer.WaveFormat.SampleRate)
                input = new WdlResamplingSampleProvider(input, _mixer.WaveFormat.SampleRate);

            if (input.WaveFormat.Channels == _mixer.WaveFormat.Channels)
                return input;
            if (input.WaveFormat.Channels == 1 && _mixer.WaveFormat.Channels == 2)
                return new MonoToStereoSampleProvider(input);
            if (input.WaveFormat.Channels == 2 && _mixer.WaveFormat.Channels == 1)
                return new StereoToMonoSampleProvider(input);
            throw new NotImplementedException("Not yet implemented this channel count conversion");
        }

        public static void Shutdown()
        {
            _mixer.RemoveAllMixerInputs();
            foreach (AudioStream stream in _inputStreams.Values)
            {
                stream.MixerInput = null;
            }
            _inputStreams.Clear();
            _outputDevice.Stop();
            _outputDevice.Dispose();
        }
    }
}
