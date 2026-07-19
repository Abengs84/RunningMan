using RunningMan.Net;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Watches synced race state on the local client and triggers race sounds.
    /// Checkpoint/finish cues are also played immediately via RPC (see RaceNetSync.SendRaceCue).
    /// </summary>
    public sealed class RaceSoundMonitor : MonoBehaviour
    {
        private static RaceSoundMonitor _instance;

        private int _lastCountdownRemaining = -1;
        private bool _wasCountdownActive;
        private int _lastPhase = -1;
        private bool _playedStartSound;
        private bool _playedCountdownSound;
        private int _lastCheckpointIndex;
        private bool _trackingRunner;
        private bool _localWasFinished;
        private bool _preloadedSounds;
        private bool _checkpointCueHandled;
        private bool _finishCueHandled;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public static void NotifyCheckpointCue()
        {
            if (_instance == null)
            {
                return;
            }

            _instance._checkpointCueHandled = true;
            // Cue can arrive before the next state sync — advance locally to avoid a double play.
            var local = RaceNetSync.GetLocalRunner();
            if (local != null && local.NextCheckpointIndex > _instance._lastCheckpointIndex)
            {
                _instance._lastCheckpointIndex = local.NextCheckpointIndex;
            }
            else
            {
                _instance._lastCheckpointIndex++;
            }
        }

        public static void NotifyFinishCue()
        {
            if (_instance == null)
            {
                return;
            }

            _instance._finishCueHandled = true;
            _instance._localWasFinished = true;
        }

        private void Update()
        {
            if (Player.m_localPlayer == null || !ModConfig.EnableRaceSounds.Value)
            {
                return;
            }

            if (!_preloadedSounds)
            {
                _preloadedSounds = true;
                RaceSoundPlayer.PreloadCustomSounds();
            }

            TrackCountdownStart();
            TrackPhaseStart();
            TrackRunnerEvents();
        }

        private void TrackCountdownStart()
        {
            var countdownActive = RaceNetSync.IsCountdownActive();
            var remaining = RaceNetSync.GetCountdownRemainingSeconds();

            if (countdownActive && !_wasCountdownActive)
            {
                _playedCountdownSound = false;
                TryPlayCountdown();
            }

            if (countdownActive && !_playedCountdownSound)
            {
                TryPlayCountdown();
            }

            if (countdownActive && _lastCountdownRemaining > 0 && remaining <= 0)
            {
                TryPlayRaceStart();
            }

            if (_wasCountdownActive && !countdownActive && _lastCountdownRemaining == 0)
            {
                TryPlayRaceStart();
            }

            _wasCountdownActive = countdownActive;
            if (countdownActive)
            {
                _lastCountdownRemaining = remaining;
            }
            else
            {
                _lastCountdownRemaining = -1;
                _playedCountdownSound = false;
            }
        }

        private void TrackPhaseStart()
        {
            var state = RaceNetSync.ClientState;
            if (state == null)
            {
                return;
            }

            if (_lastPhase == (int)RaceEventPhase.Countdown && state.Phase == (int)RaceEventPhase.Racing)
            {
                TryPlayRaceStart();
            }

            if (state.Phase == (int)RaceEventPhase.Idle ||
                state.Phase == (int)RaceEventPhase.Registration ||
                state.Phase == (int)RaceEventPhase.Ready)
            {
                _playedStartSound = false;
                _trackingRunner = false;
                _lastCheckpointIndex = 0;
                _localWasFinished = false;
                _checkpointCueHandled = false;
                _finishCueHandled = false;
            }

            _lastPhase = state.Phase;
        }

        private void TrackRunnerEvents()
        {
            var local = RaceNetSync.GetLocalRunner();
            if (local == null || local.StartUtcTicks <= 0)
            {
                _trackingRunner = false;
                _lastCheckpointIndex = 0;
                _localWasFinished = false;
                return;
            }

            if (!_trackingRunner)
            {
                _trackingRunner = true;
                _lastCheckpointIndex = local.NextCheckpointIndex;
                _localWasFinished = local.Finished;
                return;
            }

            // Fallback if the dedicated cue RPC was missed — avoid double-play when cue already fired.
            if (local.NextCheckpointIndex > _lastCheckpointIndex)
            {
                if (!_checkpointCueHandled)
                {
                    RaceSoundPlayer.PlayCheckpoint();
                }

                _checkpointCueHandled = false;
            }

            if (local.Finished && !_localWasFinished && local.Place == 1)
            {
                if (!_finishCueHandled)
                {
                    RaceSoundPlayer.PlayFirstPlaceFinish();
                }

                _finishCueHandled = false;
            }

            _lastCheckpointIndex = local.NextCheckpointIndex;
            _localWasFinished = local.Finished;
        }

        private void TryPlayCountdown()
        {
            if (_playedCountdownSound)
            {
                return;
            }

            _playedCountdownSound = true;
            RaceSoundPlayer.PlayCountdown();
        }

        private void TryPlayRaceStart()
        {
            if (_playedStartSound)
            {
                return;
            }

            _playedStartSound = true;
            RaceSoundPlayer.PlayRaceStart();
        }
    }
}
