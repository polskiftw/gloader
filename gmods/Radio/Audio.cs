#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using NAudio.Wave;

internal static partial class GeneralRadio
{
    private const int OutputRate = 44100;
    private const int OutputChannels = 2;
    private const int DesiredPendingBuffers = 5;
    private const int MaxQueuedBuffers = 12;
    private const double ChunkSeconds = 0.125;
    private const float PauseDuck = 0.22f;
    private const float DuckDownSeconds = 0.35f;
    private const float DuckUpSeconds = 0.50f;

    private static object _dynamicSound;
    private static Type _dynamicSoundType;
    private static PropertyInfo _pendingBufferCount;
    private static PropertyInfo _outputVolume;
    private static MethodInfo _submitBuffer;
    private static MethodInfo _playSound;
    private static MethodInfo _stopSound;
    private static MethodInfo _disposeSound;
    private static double _lastOutputTickSeconds;
    private static float _duck = 1f;

    internal static void StartAudioWorker(int generation, Station station)
    {
        new Thread(() => AudioWorker(generation, station))
        {
            IsBackground = true,
            Name = "gloader Radio audio"
        }.Start();
    }

    private static void AudioWorker(int generation, Station station)
    {
        var reconnectAttempt = 0;
        while (generation == Volatile.Read(ref AudioGeneration) && State.Playing && ReferenceEquals(station, SelectedStation))
        {
            var ranked = StreamRanking.Rank(station.Streams);
            if (ranked.Count == 0)
            {
                SetHealth(RadioHealth.Offline, "No compatible free stream");
                return;
            }

            var madeProgress = false;
            foreach (var variant in ranked)
            {
                if (generation != Volatile.Read(ref AudioGeneration) || !State.Playing || !ReferenceEquals(station, SelectedStation)) return;
                try
                {
                    SetHealth(reconnectAttempt == 0 ? RadioHealth.Buffering : RadioHealth.Reconnecting, reconnectAttempt == 0 ? "Buffering" : "Trying fallback");
                    var streamUrl = RadioNet.ResolveStreamVariant(station, variant);
                    ActiveStreamLabel = string.IsNullOrWhiteSpace(variant.Label) ? variant.Codec : variant.Label;
                    using (var reader = new MediaFoundationReader(streamUrl))
                    using (var resampler = new MediaFoundationResampler(reader, new WaveFormat(OutputRate, 16, OutputChannels)))
                    {
                        resampler.ResamplerQuality = 60;
                        var bytesPerChunk = (int)(OutputRate * OutputChannels * 2 * ChunkSeconds);
                        bytesPerChunk -= bytesPerChunk % (OutputChannels * 2);
                        var buffer = new byte[bytesPerChunk];
                        while (generation == Volatile.Read(ref AudioGeneration) && State.Playing && ReferenceEquals(station, SelectedStation))
                        {
                            while (AudioBuffers.Count >= MaxQueuedBuffers && generation == Volatile.Read(ref AudioGeneration)) Thread.Sleep(20);
                            var read = resampler.Read(buffer, 0, buffer.Length);
                            if (read <= 0) throw new InvalidOperationException("Radio stream ended.");
                            var chunk = new byte[read];
                            Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                            AudioBuffers.Enqueue(chunk);
                            Interlocked.Exchange(ref LastAudioUtcTicks, DateTime.UtcNow.Ticks);
                            madeProgress = true;
                            reconnectAttempt = 0;
                            SetHealth(RadioHealth.Online, "Online");
                        }
                    }
                }
                catch
                {
                    if (generation != Volatile.Read(ref AudioGeneration)) return;
                }
            }

            reconnectAttempt++;
            SetHealth(madeProgress ? RadioHealth.Reconnecting : RadioHealth.Offline, madeProgress ? "Reconnecting" : "Stream unavailable");
            var delay = Math.Min(15, 1 << Math.Min(4, reconnectAttempt));
            for (var i = 0; i < delay * 10 && generation == Volatile.Read(ref AudioGeneration); i++) Thread.Sleep(100);
        }
    }

    private static void SetHealth(RadioHealth health, string detail)
    {
        lock (StateLock)
        {
            // MetadataUnavailable is intentionally sticky while audio is otherwise healthy.
            if (Health != RadioHealth.MetadataUnavailable || health != RadioHealth.Online) Health = health;
            StatusDetail = detail ?? health.ToString();
        }
    }

    internal static void TickOutput()
    {
        if (State == null) return;
        if (ResetOutputRequested)
        {
            ResetOutputRequested = false;
            DisposeOutput();
        }
        if (!State.Playing)
        {
            DisposeOutput();
            return;
        }

        EnsureOutput();
        if (_dynamicSound == null) return;

        var now = Clock.Elapsed.TotalSeconds;
        var delta = _lastOutputTickSeconds <= 0 ? 0.016 : Math.Max(0.001, Math.Min(0.25, now - _lastOutputTickSeconds));
        _lastOutputTickSeconds = now;
        var shouldDuck = GamePaused() && !RadioUi.IsOpen;
        var target = shouldDuck ? PauseDuck : 1f;
        var seconds = target < _duck ? DuckDownSeconds : DuckUpSeconds;
        var step = seconds <= 0 ? 1f : (float)(delta / seconds);
        if (_duck < target) _duck = Math.Min(target, _duck + step);
        else if (_duck > target) _duck = Math.Max(target, _duck - step);

        var finalVolume = TerrariaMusicVolume() * State.Volume * _duck;
        try { _outputVolume.SetValue(_dynamicSound, finalVolume, null); } catch { }

        var pending = 0;
        try { pending = Convert.ToInt32(_pendingBufferCount.GetValue(_dynamicSound, null)); } catch { }
        while (pending < DesiredPendingBuffers)
        {
            byte[] chunk;
            if (!AudioBuffers.TryDequeue(out chunk)) break;
            _submitBuffer.Invoke(_dynamicSound, new object[] { chunk });
            pending++;
        }
        try { _playSound.Invoke(_dynamicSound, null); } catch { }
    }

    private static void EnsureOutput()
    {
        if (_dynamicSound != null) return;
        try
        {
            _dynamicSoundType = AccessTools.TypeByName("Microsoft.Xna.Framework.Audio.DynamicSoundEffectInstance");
            var channelsType = AccessTools.TypeByName("Microsoft.Xna.Framework.Audio.AudioChannels");
            if (_dynamicSoundType == null || channelsType == null) return;
            var stereo = Enum.Parse(channelsType, "Stereo");
            _dynamicSound = Activator.CreateInstance(_dynamicSoundType, new[] { (object)OutputRate, stereo });
            _pendingBufferCount = _dynamicSoundType.GetProperty("PendingBufferCount", BindingFlags.Instance | BindingFlags.Public);
            _outputVolume = _dynamicSoundType.GetProperty("Volume", BindingFlags.Instance | BindingFlags.Public);
            _submitBuffer = _dynamicSoundType.GetMethod("SubmitBuffer", new[] { typeof(byte[]) });
            _playSound = _dynamicSoundType.GetMethod("Play", Type.EmptyTypes);
            _stopSound = _dynamicSoundType.GetMethod("Stop", Type.EmptyTypes);
            _disposeSound = _dynamicSoundType.GetMethod("Dispose", Type.EmptyTypes);
            if (_pendingBufferCount == null || _outputVolume == null || _submitBuffer == null || _playSound == null) DisposeOutput();
        }
        catch { DisposeOutput(); }
    }

    private static void DisposeOutput()
    {
        var output = _dynamicSound;
        _dynamicSound = null;
        if (output == null) return;
        try { _stopSound?.Invoke(output, null); } catch { }
        try { _disposeSound?.Invoke(output, null); } catch { }
        ClearAudioBuffers();
    }
}
#endif
