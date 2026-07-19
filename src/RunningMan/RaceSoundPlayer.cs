using System;
using System.Collections;
using System.Collections.Generic;
using RunningMan.Net;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Plays custom audio files or Valheim SFX prefabs for race events on the local client.
    /// </summary>
    public static class RaceSoundPlayer
    {
        private static MonoBehaviour _host;
        private static readonly HashSet<string> MissingPrefabs = new HashSet<string>(StringComparer.Ordinal);

        public static void Initialize(MonoBehaviour host)
        {
            _host = host;
            RaceCustomAudio.Initialize(host);
        }

        public static void PreloadCustomSounds()
        {
            RaceCustomAudio.PreloadAll();
        }

        public static void PlayRaceStart()
        {
            if (!ModConfig.EnableRaceSounds.Value)
            {
                return;
            }

            var volume = ModConfig.RaceSoundVolume.Value;
            if (RaceCustomAudio.TryPlay(RaceSoundSlot.Start, volume, PlayRaceStartFallback))
            {
                return;
            }

            PlayRaceStartFallback();
        }

        public static void PlayCountdown()
        {
            if (!ModConfig.EnableRaceSounds.Value)
            {
                return;
            }

            var volume = ModConfig.RaceSoundVolume.Value;
            RaceCustomAudio.TryPlayStream(RaceSoundSlot.Countdown, volume);
        }

        public static void PlayFalseStart()
        {
            if (!ModConfig.EnableRaceSounds.Value)
            {
                return;
            }

            // Stop countdown for this client only, then play false-start cue.
            RaceCustomAudio.StopStream();
            var volume = ModConfig.RaceSoundVolume.Value;
            RaceCustomAudio.TryPlay(RaceSoundSlot.FalseStart, volume, null);
        }

        public static void PlayCheckpoint()
        {
            if (!ModConfig.EnableRaceSounds.Value)
            {
                return;
            }

            var volume = ModConfig.RaceSoundVolume.Value;
            if (RaceCustomAudio.TryPlay(RaceSoundSlot.Checkpoint, volume, PlayCheckpointFallback))
            {
                return;
            }

            PlayCheckpointFallback();
        }

        public static void PlayFirstPlaceFinish()
        {
            if (!ModConfig.EnableRaceSounds.Value)
            {
                return;
            }

            var volume = ModConfig.RaceSoundVolume.Value;
            if (RaceCustomAudio.TryPlay(RaceSoundSlot.Finish, volume, PlayFirstPlaceFinishFallback))
            {
                return;
            }

            PlayFirstPlaceFinishFallback();
        }

        private static void PlayRaceStartFallback()
        {
            PlaySequence(ModConfig.RaceStartSounds.Value, 0.35f);
        }

        private static void PlayCheckpointFallback()
        {
            PlaySingle(ModConfig.CheckpointSound.Value);
        }

        private static void PlayFirstPlaceFinishFallback()
        {
            PlaySequence(ModConfig.FirstPlaceFinishSounds.Value, 0.18f);
        }

        private static void PlaySingle(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                return;
            }

            PlayPrefab(prefabName.Trim(), ModConfig.RaceSoundVolume.Value);
        }

        private static void PlaySequence(string prefabList, float delaySeconds)
        {
            if (_host == null || string.IsNullOrWhiteSpace(prefabList))
            {
                return;
            }

            var names = prefabList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (names.Length == 0)
            {
                return;
            }

            _host.StartCoroutine(PlaySequenceRoutine(names, delaySeconds));
        }

        private static IEnumerator PlaySequenceRoutine(string[] prefabNames, float delaySeconds)
        {
            var volume = ModConfig.RaceSoundVolume.Value;
            for (var i = 0; i < prefabNames.Length; i++)
            {
                PlayPrefab(prefabNames[i].Trim(), volume);
                if (i < prefabNames.Length - 1 && delaySeconds > 0f)
                {
                    yield return new WaitForSeconds(delaySeconds);
                }
            }
        }

        private static void PlayPrefab(string prefabName, float volumeMultiplier)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || ZNetScene.instance == null)
            {
                return;
            }

            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                if (MissingPrefabs.Add(prefabName))
                {
                    RunningManPlugin.Log.LogWarning($"RunningMan sound prefab not found: {prefabName}");
                }

                return;
            }

            var position = GetListenPosition();
            var instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            ApplyVolume(instance, volumeMultiplier);
        }

        private static void ApplyVolume(GameObject instance, float volumeMultiplier)
        {
            if (instance == null)
            {
                return;
            }

            volumeMultiplier = Mathf.Clamp(volumeMultiplier, 0f, 2f);
            foreach (var audio in instance.GetComponentsInChildren<AudioSource>(true))
            {
                audio.volume = Mathf.Clamp01(audio.volume * volumeMultiplier);
                if (audio.clip != null && !audio.isPlaying)
                {
                    audio.Play();
                }
            }
        }

        private static Vector3 GetListenPosition()
        {
            if (Player.m_localPlayer != null)
            {
                return Player.m_localPlayer.transform.position;
            }

            if (Camera.main != null)
            {
                return Camera.main.transform.position;
            }

            return Vector3.zero;
        }
    }
}
