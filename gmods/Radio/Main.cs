#if GLOADER_SERVER
public static class Mod
{
    public static void Load() { }
}
#else
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;

public static class Mod
{
    public static void Load() => GeneralRadio.Load();
}

internal static partial class GeneralRadio
{
    internal const string Version = "1.0.0";
    internal const string ModDirectoryDataKey = "GLoader.ModDirectory";
    internal const double HealthySeconds = 8.0;
    internal const double NotificationSeconds = 6.0;

    internal static readonly object StateLock = new object();
    internal static readonly Stopwatch Clock = Stopwatch.StartNew();
    internal static readonly ConcurrentQueue<byte[]> AudioBuffers = new ConcurrentQueue<byte[]>();
    internal static Type MainType;
    internal static string ModDirectory;
    internal static RadioState State;
    internal static Station SelectedStation;
    internal static TrackInfo CurrentTrack;
    internal static RadioHealth Health = RadioHealth.Unknown;
    internal static string StatusDetail = "Starting";
    internal static string ActiveStreamLabel = string.Empty;
    internal static int AudioGeneration;
    internal static int MetadataGeneration;
    internal static long LastAudioUtcTicks;
    internal static bool ResetOutputRequested;
    internal static int ConsecutiveMetadataFailures;

    private static float _savedMusicVolume;
    private static bool _musicVolumeSuppressed;

    internal static void Load()
    {
        ModDirectory = Convert.ToString(AppDomain.CurrentDomain.GetData(ModDirectoryDataKey));
        if (string.IsNullOrWhiteSpace(ModDirectory))
            ModDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gmods", "Radio");
        Directory.CreateDirectory(ModDirectory);

        State = RadioPersistence.LoadState(ModDirectory);
        RadioCatalog.Initialize(ModDirectory);
        RadioCatalog.AddDirectoryResults(State.SavedStations.Values);
        RadioProviderAugmentation.ApplyStaticFallbacks();
        RadioProviderAugmentation.BeginBackgroundDiscovery();
        SelectedStation = RadioCatalog.Find(State.SelectedStationId) ?? RadioCatalog.Find("rainwave:5") ?? FirstStation();
        if (SelectedStation != null) State.SelectedStationId = SelectedStation.Id;

        MainType = AccessTools.TypeByName("Terraria.Main");
        var harmony = new Harmony("gloader.radio.runtime");
        TryInstallAudioPatch(harmony);
        RadioUi.TryInstall(harmony);

        StartWorkersForSelection();
    }

    internal static List<Station> Stations() => RadioCatalog.Snapshot();

    internal static void SelectStation(Station station)
    {
        if (station == null) return;
        lock (StateLock)
        {
            SelectedStation = station;
            State.SelectedStationId = station.Id;
            State.Playing = true;
            RadioPersistence.RememberLiveStation(State, station);
            RadioPersistence.TouchRecent(State, station.Id);
            CurrentTrack = null;
            Health = RadioHealth.Buffering;
            StatusDetail = "Connecting";
            ActiveStreamLabel = string.Empty;
            Interlocked.Increment(ref AudioGeneration);
            Interlocked.Increment(ref MetadataGeneration);
            ResetOutputRequested = true;
            ClearAudioBuffers();
            RadioPersistence.SaveState(ModDirectory, State);
        }
        RadioDirectories.CountRadioBrowserClick(station);
        StartWorkersForSelection();
    }

    internal static void TogglePlaying()
    {
        lock (StateLock)
        {
            State.Playing = !State.Playing;
            if (State.Playing)
            {
                Health = RadioHealth.Buffering;
                StatusDetail = "Connecting";
                Interlocked.Increment(ref AudioGeneration);
                Interlocked.Increment(ref MetadataGeneration);
            }
            else
            {
                Interlocked.Increment(ref AudioGeneration);
                Interlocked.Increment(ref MetadataGeneration);
                Health = RadioHealth.Unknown;
                StatusDetail = "Paused";
                ResetOutputRequested = true;
                ClearAudioBuffers();
            }
            RadioPersistence.SaveState(ModDirectory, State);
        }
        if (State.Playing) StartWorkersForSelection();
    }

    internal static void SetVolume(float value)
    {
        lock (StateLock)
        {
            State.Volume = Math.Max(0f, Math.Min(1f, value));
            RadioPersistence.SaveState(ModDirectory, State);
        }
    }

    internal static void ToggleFavorite(Station station)
    {
        if (station == null) return;
        lock (StateLock)
        {
            if (State.Favorites.Add(station.Id))
                RadioPersistence.RememberLiveStation(State, station);
            else
                State.Favorites.Remove(station.Id);
            RadioPersistence.SaveState(ModDirectory, State);
        }
    }

    internal static bool IsFavorite(Station station)
    {
        lock (StateLock) return station != null && State.Favorites.Contains(station.Id);
    }

    internal static void ToggleNotifications()
    {
        lock (StateLock)
        {
            State.SongNotifications = !State.SongNotifications;
            RadioPersistence.SaveState(ModDirectory, State);
        }
    }

    internal static void SetTrack(TrackInfo track)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.Display)) return;
        var changed = false;
        lock (StateLock)
        {
            if (CurrentTrack == null || !string.Equals(CurrentTrack.Display, track.Display, StringComparison.Ordinal))
            {
                CurrentTrack = track;
                changed = true;
            }
            if (Health == RadioHealth.MetadataUnavailable) Health = RadioHealth.Online;
            ConsecutiveMetadataFailures = 0;
        }
        if (changed) RadioUi.NotifySongChange(track.Display);
    }

    internal static bool AudioIsHealthy()
    {
        if (State == null || !State.Playing) return false;
        var ticks = Interlocked.Read(ref LastAudioUtcTicks);
        if (ticks <= 0) return false;
        return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < TimeSpan.FromSeconds(HealthySeconds);
    }

    private static Station FirstStation()
    {
        var stations = RadioCatalog.Snapshot();
        return stations.Count == 0 ? null : stations[0];
    }

    private static void StartWorkersForSelection()
    {
        if (SelectedStation == null || State == null || !State.Playing) return;
        var audioGeneration = Interlocked.Increment(ref AudioGeneration);
        var metadataGeneration = Interlocked.Increment(ref MetadataGeneration);
        StartAudioWorker(audioGeneration, SelectedStation);
        StartMetadataWorker(metadataGeneration, SelectedStation);
    }

    private static void StartMetadataWorker(int generation, Station station)
    {
        new Thread(() =>
        {
            while (generation == Volatile.Read(ref MetadataGeneration) && State.Playing && ReferenceEquals(station, SelectedStation))
            {
                TrackInfo track;
                if (RadioMetadata.TryReadTrack(station, out track)) SetTrack(track);
                else
                {
                    lock (StateLock)
                    {
                        ConsecutiveMetadataFailures++;
                        if (ConsecutiveMetadataFailures >= 3 && AudioIsHealthy()) Health = RadioHealth.MetadataUnavailable;
                    }
                }
                for (var i = 0; i < 25 && generation == Volatile.Read(ref MetadataGeneration); i++) Thread.Sleep(100);
            }
        }) { IsBackground = true, Name = "gloader Radio metadata" }.Start();
    }

    private static void TryInstallAudioPatch(Harmony harmony)
    {
        try
        {
            if (MainType == null) return;
            var method = AccessTools.Method(MainType, "UpdateAudio", Type.EmptyTypes);
            if (method == null) return;
            harmony.Patch(method,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(GeneralRadio), nameof(UpdateAudioPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(GeneralRadio), nameof(UpdateAudioPostfix))));
        }
        catch { }
    }

    private static void UpdateAudioPrefix()
    {
        _musicVolumeSuppressed = false;
        try
        {
            if (!AudioIsHealthy()) return;
            var field = AccessTools.Field(MainType, "musicVolume");
            if (field == null) return;
            _savedMusicVolume = Convert.ToSingle(field.GetValue(null));
            field.SetValue(null, 0f);
            _musicVolumeSuppressed = true;
        }
        catch { _musicVolumeSuppressed = false; }
    }

    private static void UpdateAudioPostfix()
    {
        try
        {
            if (_musicVolumeSuppressed)
            {
                var field = AccessTools.Field(MainType, "musicVolume");
                if (field != null) field.SetValue(null, _savedMusicVolume);
            }
        }
        catch { }
        _musicVolumeSuppressed = false;
        try { TickOutput(); } catch { }
    }

    internal static float TerrariaMusicVolume()
    {
        try
        {
            var field = AccessTools.Field(MainType, "musicVolume");
            return field == null ? 1f : Math.Max(0f, Math.Min(1f, Convert.ToSingle(field.GetValue(null))));
        }
        catch { return 1f; }
    }

    internal static bool GamePaused()
    {
        try
        {
            var field = AccessTools.Field(MainType, "gamePaused");
            return field != null && Convert.ToBoolean(field.GetValue(null));
        }
        catch { return false; }
    }

    internal static void ClearAudioBuffers()
    {
        byte[] ignored;
        while (AudioBuffers.TryDequeue(out ignored)) { }
    }
}
#endif
