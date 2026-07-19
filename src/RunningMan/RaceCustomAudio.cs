using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace RunningMan
{
    public enum RaceSoundSlot
    {
        Start,
        Checkpoint,
        Finish,
        Countdown,
        FalseStart
    }

    /// <summary>
    /// Loads optional custom race audio from the plugin Sounds folder (start.mp3, checkpoint.mp3, finish.mp3).
    /// </summary>
    public static class RaceCustomAudio
    {
        private static readonly Dictionary<RaceSoundSlot, AudioClip> Clips =
            new Dictionary<RaceSoundSlot, AudioClip>();

        private static readonly HashSet<RaceSoundSlot> LoadAttempted = new HashSet<RaceSoundSlot>();
        private static readonly HashSet<RaceSoundSlot> Loading = new HashSet<RaceSoundSlot>();
        private static MonoBehaviour _host;
        private static AudioSource _oneShotSource;
        private static AudioSource _streamSource;
        private static bool _folderEnsured;

        public static void Initialize(MonoBehaviour host)
        {
            _host = host;
            _oneShotSource = host.gameObject.AddComponent<AudioSource>();
            _oneShotSource.playOnAwake = false;
            _oneShotSource.spatialBlend = 0f;
            _oneShotSource.spatialize = false;
            _oneShotSource.bypassEffects = true;
            _oneShotSource.bypassListenerEffects = true;
            _oneShotSource.bypassReverbZones = true;
            _streamSource = host.gameObject.AddComponent<AudioSource>();
            _streamSource.playOnAwake = false;
            _streamSource.spatialBlend = 0f;
            _streamSource.spatialize = false;
            _streamSource.loop = false;
            _streamSource.bypassEffects = true;
            _streamSource.bypassListenerEffects = true;
            _streamSource.bypassReverbZones = true;
            EnsureSoundsFolder();
        }

        public static string GetSoundsFolderPath()
        {
            var relative = ModConfig.CustomSoundsFolder.Value?.Trim() ?? "Sounds";
            relative = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            // Prefer the folder that contains RunningMan.dll (…/plugins/RunningMan/).
            var pluginDir = GetRunningManPluginDirectory();
            if (Path.IsPathRooted(relative))
            {
                return relative;
            }

            return Path.Combine(pluginDir, relative);
        }

        private static string GetRunningManPluginDirectory()
        {
            try
            {
                var location = typeof(RunningManPlugin).Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    var dir = Path.GetDirectoryName(location);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        return dir;
                    }
                }
            }
            catch
            {
                // Fall through to BepInEx plugins path.
            }

            return Path.Combine(Paths.PluginPath, "RunningMan");
        }

        public static void PreloadAll()
        {
            if (_host == null || !ModConfig.UseCustomRaceSounds.Value)
            {
                return;
            }

            _host.StartCoroutine(PreloadRoutine());
        }

        /// <summary>
        /// Returns true if a custom clip was played or is being loaded (no vanilla fallback needed yet).
        /// </summary>
        public static bool TryPlay(RaceSoundSlot slot, float volume, Action playFallback)
        {
            if (!ModConfig.UseCustomRaceSounds.Value)
            {
                return false;
            }

            if (Clips.TryGetValue(slot, out var cached) && cached != null)
            {
                PlayClip(cached, volume);
                return true;
            }

            if (LoadAttempted.Contains(slot))
            {
                return false;
            }

            if (_host == null)
            {
                return false;
            }

            if (!HasCustomFile(slot))
            {
                LoadAttempted.Add(slot);
                return false;
            }

            _host.StartCoroutine(LoadAndPlay(slot, volume, playFallback));
            return true;
        }

        public static bool HasCustomFile(RaceSoundSlot slot)
        {
            return TryResolveFilePath(GetConfiguredFileName(slot), out _);
        }

        private static IEnumerator PreloadRoutine()
        {
            yield return LoadClip(RaceSoundSlot.Start);
            yield return LoadClip(RaceSoundSlot.Checkpoint);
            yield return LoadClip(RaceSoundSlot.Finish);
            yield return LoadClip(RaceSoundSlot.Countdown);
            yield return LoadClip(RaceSoundSlot.FalseStart);
        }

        private static IEnumerator LoadAndPlay(RaceSoundSlot slot, float volume, Action playFallback)
        {
            if (Loading.Contains(slot))
            {
                yield break;
            }

            Loading.Add(slot);
            yield return LoadClip(slot);
            Loading.Remove(slot);

            if (Clips.TryGetValue(slot, out var clip) && clip != null)
            {
                PlayClip(clip, volume);
            }
            else
            {
                playFallback?.Invoke();
            }
        }

        private static IEnumerator LoadClip(RaceSoundSlot slot)
        {
            if (LoadAttempted.Contains(slot))
            {
                yield break;
            }

            LoadAttempted.Add(slot);
            var configuredName = GetConfiguredFileName(slot);
            if (!TryResolveFilePath(configuredName, out var path))
            {
                RunningManPlugin.Log.LogInfo(
                    $"RunningMan: no custom sound for {slot} (expected {configuredName} in {GetSoundsFolderPath()}).");
                yield break;
            }

            var url = "file:///" + path.Replace("\\", "/");
            var audioType = GetAudioType(path);
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                yield return request.SendWebRequest();

                if (!string.IsNullOrEmpty(request.error))
                {
                    RunningManPlugin.Log.LogWarning(
                        $"RunningMan: failed to load custom sound '{path}': {request.error}");
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    RunningManPlugin.Log.LogWarning($"RunningMan: custom sound '{path}' loaded empty.");
                    yield break;
                }

                clip.name = Path.GetFileName(path);
                Clips[slot] = clip;
                RunningManPlugin.Log.LogInfo($"RunningMan: loaded custom sound '{clip.name}' for {slot}.");
            }
        }

        /// <summary>
        /// Plays a clip that can be stopped (used for countdown).
        /// </summary>
        public static bool TryPlayStream(RaceSoundSlot slot, float volume)
        {
            if (!ModConfig.UseCustomRaceSounds.Value || _streamSource == null)
            {
                return false;
            }

            if (Clips.TryGetValue(slot, out var cached) && cached != null)
            {
                PlayStream(cached, volume);
                return true;
            }

            if (LoadAttempted.Contains(slot) || _host == null || !HasCustomFile(slot))
            {
                return false;
            }

            _host.StartCoroutine(LoadAndPlayStream(slot, volume));
            return true;
        }

        public static void StopStream()
        {
            if (_streamSource != null && _streamSource.isPlaying)
            {
                _streamSource.Stop();
            }
        }

        private static IEnumerator LoadAndPlayStream(RaceSoundSlot slot, float volume)
        {
            if (Loading.Contains(slot))
            {
                yield break;
            }

            Loading.Add(slot);
            yield return LoadClip(slot);
            Loading.Remove(slot);

            if (Clips.TryGetValue(slot, out var clip) && clip != null)
            {
                PlayStream(clip, volume);
            }
        }

        private static void PlayStream(AudioClip clip, float volume)
        {
            if (_streamSource == null || clip == null)
            {
                return;
            }

            _streamSource.Stop();
            _streamSource.clip = clip;
            _streamSource.volume = Mathf.Clamp(volume, 0f, 2f);
            _streamSource.Play();
        }

        private static void PlayClip(AudioClip clip, float volume)
        {
            if (_oneShotSource == null || clip == null)
            {
                return;
            }

            _oneShotSource.PlayOneShot(clip, Mathf.Clamp(volume, 0f, 2f));
        }

        private static string GetConfiguredFileName(RaceSoundSlot slot)
        {
            switch (slot)
            {
                case RaceSoundSlot.Start:
                    return ModConfig.CustomStartSoundFile.Value;
                case RaceSoundSlot.Checkpoint:
                    return ModConfig.CustomCheckpointSoundFile.Value;
                case RaceSoundSlot.Finish:
                    return ModConfig.CustomFinishSoundFile.Value;
                case RaceSoundSlot.Countdown:
                    return ModConfig.CustomCountdownSoundFile.Value;
                case RaceSoundSlot.FalseStart:
                    return ModConfig.CustomFalseStartSoundFile.Value;
                default:
                    return string.Empty;
            }
        }

        private static bool TryResolveFilePath(string configuredName, out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(configuredName))
            {
                return false;
            }

            var folder = GetSoundsFolderPath();
            var trimmed = configuredName.Trim();
            var candidate = Path.Combine(folder, trimmed);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }

            var baseName = Path.GetFileNameWithoutExtension(trimmed);
            foreach (var extension in new[] { ".mp3", ".wav", ".ogg" })
            {
                candidate = Path.Combine(folder, baseName + extension);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }

            return false;
        }

        private static AudioType GetAudioType(string path)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            switch (extension)
            {
                case ".ogg":
                    return AudioType.OGGVORBIS;
                case ".wav":
                    return AudioType.WAV;
                default:
                    return AudioType.MPEG;
            }
        }

        private static void EnsureSoundsFolder()
        {
            if (_folderEnsured)
            {
                return;
            }

            _folderEnsured = true;
            try
            {
                var folder = GetSoundsFolderPath();
                Directory.CreateDirectory(folder);
                RunningManPlugin.Log.LogInfo(
                    $"RunningMan custom sounds folder: {folder} (add start.mp3, checkpoint.mp3, finish.mp3, countdown.mp3, false_start.mp3)");
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogWarning($"RunningMan: could not create sounds folder: {ex.Message}");
            }
        }
    }
}
