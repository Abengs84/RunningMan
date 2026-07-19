using System;
using System.Collections.Generic;
using System.Linq;
using RunningMan.Net;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Server-side race orchestration: events, registration, countdown, triggers, standings.
    /// </summary>
    public sealed class RaceManager : MonoBehaviour
    {
        public static RaceManager Instance { get; private set; }

        private readonly Dictionary<long, RaceSession> _activeSessions = new Dictionary<long, RaceSession>();
        private readonly Dictionary<long, RegisteredParticipant> _registered = new Dictionary<long, RegisteredParticipant>();
        private readonly Dictionary<long, Vector3> _previousPositions = new Dictionary<long, Vector3>();
        private readonly Dictionary<long, Vector3> _reportedPositions = new Dictionary<long, Vector3>();
        private readonly Dictionary<long, float> _reportedPositionAt = new Dictionary<long, float>();

        private RaceEventPhase _phase = RaceEventPhase.Idle;
        private DateTime _countdownEndUtc;
        private bool _registrationOpen;
        private bool _debugMode;
        private float _nextTickTime;
        private float _nextSyncTime;
        private int _lastCountdownSecond = -1;

        public static void Initialize(RunningManPlugin plugin)
        {
            if (Instance != null)
            {
                return;
            }

            Instance = plugin.gameObject.AddComponent<RaceManager>();
        }

        private void Start()
        {
            _debugMode = ModConfig.DebugMode.Value;
            if (ValheimUtil.IsServerAuthority())
            {
                RaceNetSync.SendTrack(JsonStorage.Track);
                BroadcastState();
            }
        }

        private void Update()
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (Time.time >= _nextTickTime)
            {
                _nextTickTime = Time.time + ModConfig.UpdateInterval.Value;
                Tick(nowUtc);
            }

            if (Time.time >= _nextSyncTime)
            {
                _nextSyncTime = Time.time + (_phase == RaceEventPhase.Countdown ? 0.1f : 0.5f);
                BroadcastState();
            }
        }

        public void Tick(DateTime nowUtc)
        {
            if (_phase == RaceEventPhase.Countdown)
            {
                TickCountdown(nowUtc);
                return;
            }

            _lastCountdownSecond = -1;

            if (_phase != RaceEventPhase.Racing)
            {
                return;
            }

            var track = JsonStorage.Track;
            if (!track.HasStart)
            {
                return;
            }

            var processed = new HashSet<long>();
            foreach (var player in Player.GetAllPlayers())
            {
                if (player == null)
                {
                    continue;
                }

                var playerId = player.GetPlayerID();
                processed.Add(playerId);
                MarkPeerProcessed(player, processed);
                try
                {
                    var position = ResolveParticipantPosition(playerId, player.transform.position);
                    ProcessParticipant(playerId, player.GetPlayerName(), position, track, nowUtc, player);
                }
                catch (Exception ex)
                {
                    RunningManPlugin.Log.LogError(
                        $"RunningMan ProcessParticipant failed for {player.GetPlayerName()}: {ex}");
                }
            }

            if (ZNet.instance == null)
            {
                return;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || processed.Contains(peer.m_uid))
                {
                    continue;
                }

                if (!TryResolvePeerRacePosition(peer, out var position))
                {
                    continue;
                }

                processed.Add(peer.m_uid);
                try
                {
                    ProcessParticipant(peer.m_uid, peer.m_playerName, position, track, nowUtc, null);
                }
                catch (Exception ex)
                {
                    RunningManPlugin.Log.LogError(
                        $"RunningMan ProcessParticipant failed for peer {peer.m_playerName}: {ex}");
                }
            }
        }

        /// <summary>
        /// Client-reported positions beat peer.m_refPos on dedicated servers.
        /// </summary>
        public void ReportRunnerPosition(long id, Vector3 position)
        {
            if (id == 0)
            {
                return;
            }

            _reportedPositions[id] = position;
            _reportedPositionAt[id] = Time.time;
        }

        private Vector3 ResolveParticipantPosition(long id, Vector3 fallback)
        {
            if (TryGetFreshReportedPosition(id, out var reported))
            {
                return reported;
            }

            return fallback;
        }

        private bool TryResolvePeerRacePosition(ZNetPeer peer, out Vector3 position)
        {
            position = Vector3.zero;
            if (peer == null)
            {
                return false;
            }

            if (TryGetFreshReportedPosition(peer.m_uid, out position))
            {
                return true;
            }

            var player = ValheimUtil.FindPlayerFromPeer(peer);
            if (player != null && TryGetFreshReportedPosition(player.GetPlayerID(), out position))
            {
                return true;
            }

            return ValheimUtil.TryGetPeerWorldPosition(peer, out position);
        }

        private bool TryGetFreshReportedPosition(long id, out Vector3 position)
        {
            position = Vector3.zero;
            if (!_reportedPositions.TryGetValue(id, out position))
            {
                return false;
            }

            if (!_reportedPositionAt.TryGetValue(id, out var at) || Time.time - at > 1.5f)
            {
                return false;
            }

            return true;
        }

        private static void MarkPeerProcessed(Player player, HashSet<long> processed)
        {
            if (player == null || ZNet.instance == null)
            {
                return;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null)
                {
                    continue;
                }

                var matched = ValheimUtil.FindPlayerFromPeer(peer);
                if (matched == player || peer.m_uid == player.GetPlayerID())
                {
                    processed.Add(peer.m_uid);
                }
            }
        }

        private void TickCountdown(DateTime nowUtc)
        {
            var track = JsonStorage.Track;
            if (track.HasStart)
            {
                CheckFalseStarts(track);
            }

            var remaining = (int)Math.Ceiling((_countdownEndUtc - nowUtc).TotalSeconds);
            if (remaining != _lastCountdownSecond && remaining >= 0)
            {
                _lastCountdownSecond = remaining;
                if (remaining > 0)
                {
                    ValheimUtil.Announce($"RunningMan: {remaining}...");
                }
            }

            if (nowUtc >= _countdownEndUtc)
            {
                BeginRacing(nowUtc);
            }
        }

        private void CheckFalseStarts(TrackConfig track)
        {
            if (!ModConfig.DisqualifyOnFalseStart.Value || !track.HasStart)
            {
                return;
            }

            var toRemove = new List<RegisteredParticipant>();
            foreach (var participant in _registered.Values)
            {
                if (!ValheimUtil.TryGetParticipantPosition(participant.PlayerId, out var current))
                {
                    continue;
                }

                if (!sessionHasPrevious(participant.PlayerId))
                {
                    StorePreviousPosition(participant.PlayerId, current);
                    continue;
                }

                var previous = GetPreviousPosition(participant.PlayerId);
                StorePreviousPosition(participant.PlayerId, current);

                if (!TriggerDetector.HasFalseStarted(previous, current, track.StartGate,
                        ModConfig.StartTriggerDistance.Value, ModConfig.GateVerticalDistance.Value))
                {
                    continue;
                }

                toRemove.Add(participant);
            }

            foreach (var participant in toRemove)
            {
                _registered.Remove(participant.PlayerId);
                ValheimUtil.Announce(
                    $"RunningMan: {participant.PlayerName} false started and is out of this race!");
                RaceNetSync.SendRaceCue(participant.PlayerId, RaceNetSync.CueFalseStart);
                RunningManPlugin.Log.LogWarning($"False start: {participant.PlayerName}");
            }

            if (toRemove.Count > 0)
            {
                BroadcastState();
            }
        }

        private void ProcessParticipant(long playerId, string playerName, Vector3 currentPosition, TrackConfig track,
            DateTime nowUtc, Player player)
        {
            if (playerId == 0)
            {
                return;
            }

            if (!_activeSessions.TryGetValue(playerId, out var session))
            {
                if (!TryGetSession(playerId, playerName, out session))
                {
                    session = null;
                }
            }

            if (session == null)
            {
                if (ModConfig.RequireRegistration.Value && !_registered.ContainsKey(playerId) &&
                    !IsRegisteredByName(playerName))
                {
                    StorePreviousPosition(playerId, currentPosition);
                    return;
                }

                if (!sessionHasPrevious(playerId))
                {
                    StorePreviousPosition(playerId, currentPosition);
                    return;
                }

                var previous = GetPreviousPosition(playerId);
                if (TriggerDetector.DidCrossForward(previous, currentPosition, track.StartGate,
                        ModConfig.StartTriggerDistance.Value, ModConfig.GateVerticalDistance.Value))
                {
                    if (player != null)
                    {
                        StartRace(player, nowUtc);
                    }
                    else
                    {
                        StartRaceSession(playerId, playerName, nowUtc, currentPosition);
                    }
                }

                StorePreviousPosition(playerId, currentPosition);
                return;
            }

            if (session.Finished)
            {
                return;
            }

            if (session.Disqualified)
            {
                StorePreviousPosition(playerId, currentPosition);
                return;
            }

            if (player != null)
            {
                RaceSkillUtil.EnforceRunLevel(player, ModConfig.NormalizedRunSkillLevel.Value);
            }

            if (!session.HasPreviousPosition)
            {
                session.LastPosition = currentPosition;
                session.HasPreviousPosition = true;
                // Still check gear after arming position tracking.
                if (player != null)
                {
                    ValidateRuntimeGear(player, session);
                }

                return;
            }

            var prev = session.LastPosition;
            session.LastPosition = currentPosition;

            // Checkpoints/finish first — a mid-tick gear DQ must not erase a gate already crossed.
            if (TryCompleteCheckpoint(session, track, prev, currentPosition, nowUtc))
            {
                if (player != null && !session.Disqualified)
                {
                    ValidateRuntimeGear(player, session);
                }

                return;
            }

            CheckMissedCheckpoints(session, track, prev, currentPosition);
            TryFinishRace(session, track, prev, currentPosition, nowUtc, playerId);

            if (player != null && !session.Finished && !session.Disqualified)
            {
                ValidateRuntimeGear(player, session);
            }
        }

        private void CheckMissedCheckpoints(RaceSession session, TrackConfig track, Vector3 previous,
            Vector3 current)
        {
            if (track.Checkpoints == null || session.Disqualified || session.Finished)
            {
                return;
            }

            var expected = session.NextCheckpointIndex;
            if (expected <= 0)
            {
                return;
            }

            if (session.LastMissedCheckpointNotified >= expected)
            {
                // Still watch for finish-with-missing, but avoid repeat CP spam.
            }
            else
            {
                foreach (var checkpoint in track.Checkpoints)
                {
                    if (checkpoint.Index <= expected)
                    {
                        continue;
                    }

                    if (!TriggerDetector.DidTriggerGate(previous, current, checkpoint.Gate,
                            ModConfig.CheckpointDistance.Value, ModConfig.CheckpointVerticalDistance.Value))
                    {
                        continue;
                    }

                    session.LastMissedCheckpointNotified = expected;
                    var message =
                        $"{session.PlayerName} missed checkpoint {expected} (triggered CP{checkpoint.Index} early).";
                    ValheimUtil.Announce($"RunningMan: {message}");
                    RaceNetSync.SendYellowHudToPlayer(session.PlayerId, $"Missed checkpoint {expected}!");
                    return;
                }
            }

            if (track.HasFinish && expected <= track.CheckpointCount &&
                TriggerDetector.DidTriggerGate(previous, current, track.FinishGate,
                    ModConfig.FinishTriggerDistance.Value, ModConfig.GateVerticalDistance.Value))
            {
                if (session.LastMissedCheckpointNotified < expected)
                {
                    session.LastMissedCheckpointNotified = expected;
                    ValheimUtil.Announce(
                        $"RunningMan: {session.PlayerName} reached finish early — still missing checkpoint {expected}+.");
                    RaceNetSync.SendYellowHudToPlayer(session.PlayerId,
                        $"Finish early — you still need checkpoint {expected}!");
                }
            }
        }

        private bool TryCompleteCheckpoint(RaceSession session, TrackConfig track, Vector3 previous,
            Vector3 current, DateTime nowUtc)
        {
            var expectedIndex = session.NextCheckpointIndex;
            var checkpoint = track.Checkpoints.Find(item => item.Index == expectedIndex);
            if (checkpoint == null)
            {
                return false;
            }

            if (!TriggerDetector.DidTriggerGate(previous, current, checkpoint.Gate,
                    ModConfig.CheckpointDistance.Value, ModConfig.CheckpointVerticalDistance.Value))
            {
                return false;
            }

            session.RecordCheckpoint(nowUtc);
            var elapsed = session.CheckpointTimesMs[session.CheckpointTimesMs.Count - 1];
            var message =
                $"{session.PlayerName} reached checkpoint {expectedIndex} in {TimeFormatter.FormatDurationMs(elapsed)}";
            RunningManPlugin.Log.LogInfo(message);
            ValheimUtil.Announce(message);
            BroadcastState();
            RaceNetSync.SendRaceCue(session.PlayerId, RaceNetSync.CueCheckpoint);
            return true;
        }

        private bool TryFinishRace(RaceSession session, TrackConfig track, Vector3 previous, Vector3 current,
            DateTime nowUtc, long playerId)
        {
            if (!track.HasFinish || session.NextCheckpointIndex <= track.CheckpointCount)
            {
                return false;
            }

            if (!TriggerDetector.DidTriggerGate(previous, current, track.FinishGate,
                    ModConfig.FinishTriggerDistance.Value, ModConfig.GateVerticalDistance.Value))
            {
                return false;
            }

            CompleteRace(session, track, nowUtc, playerId);
            return true;
        }

        public void OpenRegistration()
        {
            _registrationOpen = true;
            _phase = RaceEventPhase.Registration;
            ValheimUtil.Announce("RunningMan: registration is open! Use /run join or press F6.");
            BroadcastState();
        }

        public void CloseRegistration()
        {
            _registrationOpen = false;
            if (_phase == RaceEventPhase.Registration)
            {
                _phase = _registered.Count > 0 ? RaceEventPhase.Ready : RaceEventPhase.Idle;
            }

            ValheimUtil.Announce("RunningMan: registration is closed.");
            BroadcastState();
        }

        public bool JoinPlayer(long playerId, string playerName)
        {
            if (playerId == 0 || string.IsNullOrWhiteSpace(playerName))
            {
                return false;
            }

            if (!_registrationOpen && _phase != RaceEventPhase.Registration)
            {
                return false;
            }

            if (_registered.ContainsKey(playerId))
            {
                return false;
            }

            _registered[playerId] = new RegisteredParticipant
            {
                PlayerId = playerId,
                PlayerName = playerName
            };

            ValheimUtil.Broadcast($"{playerName} joined the marathon.");

            if (ModConfig.EnableGearCheck.Value)
            {
                var player = ValheimUtil.FindPlayerById(playerId);
                if (player != null)
                {
                    var gear = GearValidator.CheckStartGear(player);
                    if (!gear.IsValid)
                    {
                        ValheimUtil.Broadcast($"{playerName} gear warning: {gear.Issues[0]}");
                    }
                }
            }

            BroadcastState();
            return true;
        }

        public GearValidator.GearCheckResult CheckPlayerGear(Player player)
        {
            return GearValidator.CheckStartGear(player);
        }

        private void PreflightGearCheck()
        {
            foreach (var participant in _registered.Values.ToList())
            {
                var player = ValheimUtil.FindPlayerById(participant.PlayerId);
                if (player == null)
                {
                    continue;
                }

                var gear = GearValidator.CheckStartGear(player);
                if (!gear.IsValid)
                {
                    ValheimUtil.Broadcast($"{participant.PlayerName} gear issue: {gear.Issues[0]}");
                }
            }
        }

        private bool ValidateRuntimeGear(Player player, RaceSession session)
        {
            var gear = GearValidator.CheckRuntimeGear(player);
            if (gear.IsValid)
            {
                return true;
            }

            var reason = gear.Issues.FirstOrDefault() ?? "illegal gear change";
            HandleGearViolation(session, reason);
            return !session.Disqualified;
        }

        /// <summary>
        /// Called from client gear RPC (dedicated server) or local ValidateRuntimeGear.
        /// </summary>
        public void ReportGearViolation(long playerId, string reason)
        {
            if (!ValheimUtil.IsServerAuthority() || !ModConfig.EnableGearCheck.Value)
            {
                return;
            }

            if (_phase != RaceEventPhase.Racing)
            {
                return;
            }

            if (!TryResolveActiveSession(playerId, out var session) ||
                session.Disqualified ||
                session.Finished)
            {
                return;
            }

            HandleGearViolation(session, reason ?? "illegal gear change");
        }

        private bool TryResolveActiveSession(long playerId, out RaceSession session)
        {
            if (_activeSessions.TryGetValue(playerId, out session))
            {
                return true;
            }

            if (ZNet.instance != null)
            {
                var peer = ZNet.instance.GetPeer(playerId);
                if (peer != null)
                {
                    var player = ValheimUtil.FindPlayerFromPeer(peer);
                    if (player != null && _activeSessions.TryGetValue(player.GetPlayerID(), out session))
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(peer.m_playerName))
                    {
                        foreach (var active in _activeSessions.Values)
                        {
                            if (string.Equals(active.PlayerName, peer.m_playerName,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                session = active;
                                return true;
                            }
                        }
                    }
                }
            }

            session = null;
            return false;
        }

        private void HandleGearViolation(RaceSession session, string reason)
        {
            if (session == null)
            {
                return;
            }

            if (ModConfig.DisqualifyOnGearViolation.Value)
            {
                DisqualifyPlayer(session, reason);
                return;
            }

            ValheimUtil.Broadcast($"{session.PlayerName} gear warning: {reason}");
            RaceNetSync.SendYellowHudToPlayer(session.PlayerId, $"Gear warning: {reason}");
        }

        private void DisqualifyPlayer(RaceSession session, string reason)
        {
            if (session.Disqualified)
            {
                return;
            }

            session.Disqualified = true;
            session.DisqualifiedReason = reason ?? "rule violation";
            if (session.FinishTimeMs <= 0)
            {
                session.FinishTimeMs = session.ElapsedMs(DateTime.UtcNow);
            }

            var player = ValheimUtil.FindPlayerById(session.PlayerId);
            RaceSkillUtil.RestoreRunLevel(player, session);
            ValheimUtil.Broadcast($"{session.PlayerName} disqualified: {session.DisqualifiedReason}");
            RaceNetSync.SendYellowHudToPlayer(session.PlayerId,
                $"DISQUALIFIED: {session.DisqualifiedReason}");
            RaceNetSync.SendRaceCue(session.PlayerId, RaceNetSync.CueFalseStart);
            RunningManPlugin.Log.LogWarning($"Disqualified {session.PlayerName}: {session.DisqualifiedReason}");
            BroadcastState();
        }

        public void TryFlagIllegalConsumable(Player player, ItemDrop.ItemData item)
        {
            if (!ValheimUtil.IsServerAuthority() || player == null || item == null ||
                !ModConfig.EnableGearCheck.Value)
            {
                return;
            }

            if (_phase != RaceEventPhase.Racing)
            {
                return;
            }

            var playerId = player.GetPlayerID();
            if (!_activeSessions.TryGetValue(playerId, out var session) || session.Disqualified || session.Finished)
            {
                return;
            }

            if (GearValidator.IsAllowedRaceConsumable(item))
            {
                return;
            }

            var label = GearValidator.GetPrefabName(item);
            if (string.IsNullOrEmpty(label))
            {
                label = GearValidator.IsFoodItem(item) ? "food" : "consumable";
            }

            var reason = GearValidator.IsFoodItem(item)
                ? $"illegal food: {label}"
                : $"illegal consumable: {label}";
            DisqualifyPlayer(session, reason);
        }

        public bool LeavePlayer(long playerId)
        {
            if (!_registered.Remove(playerId))
            {
                return false;
            }

            ResetActiveRun(playerId);
            if (_phase == RaceEventPhase.Ready && _registered.Count == 0)
            {
                _phase = RaceEventPhase.Idle;
            }

            BroadcastState();
            return true;
        }

        public bool StartCountdown(out string failureReason)
        {
            failureReason = null;
            if (_registered.Count == 0)
            {
                TryAutoRegisterLocalAdmin();
                TryAutoRegisterCommandSender();
            }

            if (_registered.Count == 0)
            {
                failureReason = "Could not start countdown. Use /run open, have runners /run join, then /run start.";
                RunningManPlugin.Log.LogWarning("RunningMan countdown blocked: no registered runners.");
                return false;
            }

            if (ModConfig.RequireStartingArea.Value)
            {
                var track = JsonStorage.Track;
                if (!track.HasStart)
                {
                    failureReason = "No start gate registered — cannot verify starting area.";
                    return false;
                }

                var missing = GetParticipantsOutsideStartingArea(track);
                if (missing.Count > 0)
                {
                    failureReason = "Not all participants inside starting area: " + string.Join(", ", missing);
                    RaceNetSync.SendYellowHud(failureReason);
                    return false;
                }
            }

            PreflightGearCheck();

            _activeSessions.Clear();
            _previousPositions.Clear();
            _phase = RaceEventPhase.Countdown;
            _countdownEndUtc = DateTime.UtcNow.AddSeconds(ModConfig.CountdownSeconds.Value);
            _registrationOpen = false;
            _lastCountdownSecond = -1;
            _nextSyncTime = 0f;
            ValheimUtil.Announce($"RunningMan: race starts in {ModConfig.CountdownSeconds.Value} seconds!");
            RaceNetSync.SendCountdownStart(_countdownEndUtc);
            BroadcastState();
            return true;
        }

        private List<string> GetParticipantsOutsideStartingArea(TrackConfig track)
        {
            var missing = new List<string>();
            foreach (var participant in _registered.Values)
            {
                if (!ValheimUtil.TryGetParticipantPosition(participant.PlayerId, out var position))
                {
                    missing.Add(participant.PlayerName + " (no position)");
                    continue;
                }

                if (!TriggerDetector.IsInStartingArea(position, track.StartGate,
                        ModConfig.StartingAreaOffset.Value,
                        ModConfig.StartingAreaDepth.Value,
                        ModConfig.StartingAreaSidePadding.Value,
                        ModConfig.GateVerticalDistance.Value))
                {
                    missing.Add(participant.PlayerName);
                }
            }

            return missing;
        }

        private bool TryAutoRegisterLocalAdmin()
        {
            if (Player.m_localPlayer == null || !ValheimUtil.IsLocalPlayerAdmin())
            {
                return false;
            }

            var playerId = Player.m_localPlayer.GetPlayerID();
            if (playerId == 0 || _registered.ContainsKey(playerId))
            {
                return _registered.ContainsKey(playerId);
            }

            _registered[playerId] = new RegisteredParticipant
            {
                PlayerId = playerId,
                PlayerName = Player.m_localPlayer.GetPlayerName()
            };
            return true;
        }

        private bool TryAutoRegisterCommandSender()
        {
            if (!CommandContext.ExecutingFromRemote || CommandContext.SenderPeerId == 0)
            {
                return false;
            }

            if (_registered.ContainsKey(CommandContext.SenderPeerId))
            {
                return true;
            }

            var peer = ZNet.instance?.GetPeer(CommandContext.SenderPeerId);
            if (peer == null || string.IsNullOrWhiteSpace(peer.m_playerName))
            {
                return false;
            }

            _registered[CommandContext.SenderPeerId] = new RegisteredParticipant
            {
                PlayerId = CommandContext.SenderPeerId,
                PlayerName = peer.m_playerName
            };
            return true;
        }

        private void BeginRacing(DateTime nowUtc)
        {
            if (_registered.Count == 0)
            {
                _phase = RaceEventPhase.Idle;
                ValheimUtil.Announce("RunningMan: no runners left to start (false starts?).");
                BroadcastState();
                return;
            }

            _phase = RaceEventPhase.Racing;
            ValheimUtil.Announce("RunningMan: GO!");

            foreach (var participant in _registered.Values.ToList())
            {
                if (_activeSessions.ContainsKey(participant.PlayerId) ||
                    TryGetSession(participant.PlayerId, participant.PlayerName, out _))
                {
                    continue;
                }

                // Always key sessions by the registered id (usually peer uid on dedicated).
                // StartRace uses character PlayerID and can disagree with peer uid.
                if (ValheimUtil.TryGetParticipantPosition(participant.PlayerId, out var position))
                {
                    StartRaceSession(participant.PlayerId, participant.PlayerName, nowUtc, position);
                }
                else
                {
                    StartRaceSession(participant.PlayerId, participant.PlayerName, nowUtc, Vector3.zero);
                }

                var player = ValheimUtil.FindPlayerById(participant.PlayerId) ??
                             ValheimUtil.FindPlayerFromPeer(ZNet.instance?.GetPeer(participant.PlayerId));
                if (player != null && _activeSessions.TryGetValue(participant.PlayerId, out var session))
                {
                    var gear = GearValidator.CheckStartGear(player);
                    if (!gear.IsValid)
                    {
                        ValheimUtil.Broadcast($"{participant.PlayerName} started with gear issue: {gear.Issues[0]}");
                    }

                    RaceSkillUtil.TrySaveAndSetRunLevel(player, session, ModConfig.NormalizedRunSkillLevel.Value);
                }
            }

            BroadcastState();
        }

        public void CancelEvent()
        {
            RestoreRunSkillsForAllSessions();
            _phase = RaceEventPhase.Idle;
            _registrationOpen = false;
            _lastCountdownSecond = -1;
            _registered.Clear();
            _activeSessions.Clear();
            _previousPositions.Clear();
            _reportedPositions.Clear();
            _reportedPositionAt.Clear();
            RaceNetSync.ClearLocalCountdown();
            RaceNetSync.SendCountdownStart(DateTime.UtcNow);
            ValheimUtil.Announce("RunningMan: event cancelled.");
            BroadcastState();
        }

        public void StartRaceSession(long playerId, string playerName, DateTime nowUtc, Vector3 position)
        {
            if (_activeSessions.ContainsKey(playerId))
            {
                return;
            }

            var session = new RaceSession(playerId, playerName, nowUtc);
            if (position != Vector3.zero)
            {
                session.LastPosition = position;
                session.HasPreviousPosition = true;
                StorePreviousPosition(playerId, position);
            }

            _activeSessions[playerId] = session;

            var player = ValheimUtil.FindPlayerById(playerId);
            RaceSkillUtil.TrySaveAndSetRunLevel(player, session, ModConfig.NormalizedRunSkillLevel.Value);

            ValheimUtil.Announce($"{session.PlayerName} has started the marathon!");
            BroadcastState();
        }

        public void StartRace(Player player, DateTime nowUtc)
        {
            var playerId = player.GetPlayerID();
            if (_activeSessions.ContainsKey(playerId))
            {
                return;
            }

            var gear = GearValidator.CheckStartGear(player);
            if (!gear.IsValid)
            {
                RunningManPlugin.Log.LogWarning($"{player.GetPlayerName()} blocked from start: {gear.Format(false)}");
                ValheimUtil.Announce($"{player.GetPlayerName()} cannot start — {gear.Issues[0]}");
                return;
            }

            var session = new RaceSession(playerId, player.GetPlayerName(), nowUtc)
            {
                LastPosition = player.transform.position,
                HasPreviousPosition = true
            };
            _activeSessions[playerId] = session;
            RaceSkillUtil.TrySaveAndSetRunLevel(player, session, ModConfig.NormalizedRunSkillLevel.Value);

            if (_phase == RaceEventPhase.Racing)
            {
                ValheimUtil.Announce($"{session.PlayerName} has started the marathon!");
            }
        }

        public void CompleteRace(RaceSession session, TrackConfig track, DateTime finishUtc, long playerId)
        {
            if (session.Disqualified)
            {
                return;
            }

            session.Finished = true;
            session.FinishTimeMs = session.ElapsedMs(finishUtc);

            var player = ValheimUtil.FindPlayerById(playerId);
            RaceSkillUtil.RestoreRunLevel(player, session);

            var record = session.ToCompletedRecord(finishUtc, track.CheckpointCount);
            Leaderboard.RecordCompletedRun(record);

            ValheimUtil.Announce($"{session.PlayerName} finished in {record.TotalTime}!");
            RunningManPlugin.Log.LogInfo($"Race complete: {session.PlayerName} in {record.TotalTime}");
            BroadcastState();

            var place = BuildStandings(finishUtc)
                .Find(runner => runner.PlayerId == playerId ||
                                string.Equals(runner.PlayerName, session.PlayerName, StringComparison.OrdinalIgnoreCase))
                ?.Place ?? 0;
            if (place == 1)
            {
                RaceNetSync.SendRaceCue(session.PlayerId, RaceNetSync.CueFinishFirst);
            }

            TryEndEventWhenAllFinished();
        }

        /// <summary>
        /// After the last active runner finishes, leave Racing so admin UI returns to "Start countdown".
        /// Clears sessions so reconnecting clients do not keep seeing the finished race HUD.
        /// </summary>
        private void TryEndEventWhenAllFinished()
        {
            if (_phase != RaceEventPhase.Racing || _activeSessions.Count == 0)
            {
                return;
            }

            foreach (var session in _activeSessions.Values)
            {
                if (!session.Finished && !session.Disqualified)
                {
                    return;
                }
            }

            RestoreRunSkillsForAllSessions();
            _phase = RaceEventPhase.Idle;
            _registrationOpen = false;
            _registered.Clear();
            _activeSessions.Clear();
            _previousPositions.Clear();
            _reportedPositions.Clear();
            _reportedPositionAt.Clear();
            ValheimUtil.Announce("RunningMan: all runners finished!");
            BroadcastState();
        }

        private bool TryGetSession(long playerId, string playerName, out RaceSession session)
        {
            if (_activeSessions.TryGetValue(playerId, out session))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(playerName))
            {
                session = null;
                return false;
            }

            foreach (var candidate in _activeSessions.Values)
            {
                if (string.Equals(candidate.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                {
                    session = candidate;
                    return true;
                }
            }

            session = null;
            return false;
        }

        private bool IsRegisteredByName(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
            {
                return false;
            }

            foreach (var participant in _registered.Values)
            {
                if (string.Equals(participant.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool ResetActiveRun(long playerId)
        {
            if (!_activeSessions.TryGetValue(playerId, out var session))
            {
                return false;
            }

            var player = ValheimUtil.FindPlayerById(playerId);
            RaceSkillUtil.RestoreRunLevel(player, session);
            _activeSessions.Remove(playerId);
            BroadcastState();
            return true;
        }

        private void RestoreRunSkillsForAllSessions()
        {
            foreach (var session in _activeSessions.Values)
            {
                var player = ValheimUtil.FindPlayerById(session.PlayerId);
                RaceSkillUtil.RestoreRunLevel(player, session);
            }
        }

        public RaceSession GetActiveSession(long playerId)
        {
            _activeSessions.TryGetValue(playerId, out var session);
            return session;
        }

        public string FormatStatus(long playerId, DateTime nowUtc)
        {
            var session = GetActiveSession(playerId);
            if (session == null)
            {
                var registered = _registered.ContainsKey(playerId) ? "registered" : "not registered";
                return $"Event: {_phase}\nStatus: not running ({registered})";
            }

            var elapsed = TimeFormatter.FormatDurationMs(session.ElapsedMs(nowUtc));
            var place = GetPlace(playerId, nowUtc);
            var checkpoint = session.NextCheckpointIndex;
            var total = JsonStorage.Track.CheckpointCount;
            return $"Event: {_phase}\nPlace: {place}\nElapsed: {elapsed}\nNext checkpoint: {checkpoint} / {total + 1}";
        }

        public int GetPlace(long playerId, DateTime nowUtc)
        {
            var standings = BuildStandings(nowUtc);
            for (var i = 0; i < standings.Count; i++)
            {
                if (standings[i].PlayerId == playerId)
                {
                    return standings[i].Place;
                }
            }

            return 0;
        }

        public enum GateKind
        {
            Start,
            Finish,
            Checkpoint
        }

        /// <summary>
        /// Moves one endpoint of a gate. CheckpointIndex is 1-based for checkpoints.
        /// </summary>
        public bool SetGateEndpoint(GateKind kind, int checkpointIndex, bool pointA, Vector3 position,
            Vector3? preferredForward = null)
        {
            var track = JsonStorage.Track;
            var previousTrackId = TrackIdentity.GetId(track);
            RaceGate gate = null;
            switch (kind)
            {
                case GateKind.Start:
                    gate = track.StartGate;
                    break;
                case GateKind.Finish:
                    gate = track.FinishGate;
                    break;
                case GateKind.Checkpoint:
                    gate = track.Checkpoints?.Find(item => item.Index == checkpointIndex)?.Gate;
                    break;
            }

            if (gate == null || !gate.IsConfigured())
            {
                return false;
            }

            gate.SetEndpoint(pointA, position, preferredForward);
            FinishTrackEdit(previousTrackId);
            return true;
        }

        public void RegisterStartGate(RaceGate gate)
        {
            var previousTrackId = TrackIdentity.GetId(JsonStorage.Track);
            JsonStorage.Track.StartGate = gate;
            FinishTrackEdit(previousTrackId);
        }

        public void RegisterFinishGate(RaceGate gate)
        {
            var previousTrackId = TrackIdentity.GetId(JsonStorage.Track);
            JsonStorage.Track.FinishGate = gate;
            FinishTrackEdit(previousTrackId);
        }

        public int RegisterCheckpoint(RaceGate gate)
        {
            var previousTrackId = TrackIdentity.GetId(JsonStorage.Track);
            var track = JsonStorage.Track;
            if (track.Checkpoints == null)
            {
                track.Checkpoints = new List<Checkpoint>();
            }

            // Keep list order aligned with Index before inserting.
            track.Checkpoints.Sort((left, right) => left.Index.CompareTo(right.Index));

            var insertIndex = TrackPath.FindCheckpointInsertIndex(track, gate.GetMidpoint());
            insertIndex = Mathf.Clamp(insertIndex, 1, track.Checkpoints.Count + 1);
            track.Checkpoints.Insert(insertIndex - 1, new Checkpoint(insertIndex, gate));
            RenumberCheckpoints(track);
            FinishTrackEdit(previousTrackId);
            return insertIndex;
        }

        public void ReplaceCheckpoints(List<Checkpoint> checkpoints)
        {
            var previousTrackId = TrackIdentity.GetId(JsonStorage.Track);
            JsonStorage.Track.Checkpoints = checkpoints;
            FinishTrackEdit(previousTrackId);
        }

        public bool RemoveCheckpoint(int index)
        {
            var track = JsonStorage.Track;
            if (track.Checkpoints == null || track.Checkpoints.Count == 0)
            {
                return false;
            }

            var previousTrackId = TrackIdentity.GetId(track);
            var removed = track.Checkpoints.RemoveAll(item => item.Index == index);
            if (removed == 0)
            {
                return false;
            }

            RenumberCheckpoints(track);
            FinishTrackEdit(previousTrackId);
            return true;
        }

        public bool RemoveNearestCheckpoint(Vector3 origin)
        {
            var track = JsonStorage.Track;
            if (track.Checkpoints == null || track.Checkpoints.Count == 0)
            {
                return false;
            }

            Checkpoint nearest = null;
            var bestDistance = float.MaxValue;
            foreach (var checkpoint in track.Checkpoints)
            {
                if (checkpoint?.Gate == null || !checkpoint.Gate.IsConfigured())
                {
                    continue;
                }

                var distance = Vector3.Distance(origin, checkpoint.Gate.GetMidpoint());
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                nearest = checkpoint;
            }

            return nearest != null && RemoveCheckpoint(nearest.Index);
        }

        public bool RemoveLastCheckpoint()
        {
            var track = JsonStorage.Track;
            if (track.Checkpoints == null || track.Checkpoints.Count == 0)
            {
                return false;
            }

            var previousTrackId = TrackIdentity.GetId(track);
            track.Checkpoints.RemoveAt(track.Checkpoints.Count - 1);
            RenumberCheckpoints(track);
            FinishTrackEdit(previousTrackId);
            return true;
        }

        public int ClearCheckpoints()
        {
            var track = JsonStorage.Track;
            var count = track.Checkpoints?.Count ?? 0;
            if (count == 0)
            {
                return 0;
            }

            var previousTrackId = TrackIdentity.GetId(track);
            track.Checkpoints.Clear();
            FinishTrackEdit(previousTrackId);
            return count;
        }

        public bool ClearStartGate()
        {
            if (!JsonStorage.Track.HasStart)
            {
                return false;
            }

            var previousTrackId = TrackIdentity.GetId(JsonStorage.Track);
            JsonStorage.Track.StartGate = new RaceGate();
            FinishTrackEdit(previousTrackId);
            return true;
        }

        public bool ClearFinishGate()
        {
            if (!JsonStorage.Track.HasFinish)
            {
                return false;
            }

            var previousTrackId = TrackIdentity.GetId(JsonStorage.Track);
            JsonStorage.Track.FinishGate = new RaceGate();
            FinishTrackEdit(previousTrackId);
            return true;
        }

        private void FinishTrackEdit(string previousTrackId)
        {
            JsonStorage.SaveTrack();
            if (!string.IsNullOrEmpty(previousTrackId))
            {
                var newTrackId = TrackIdentity.GetId(JsonStorage.Track);
                if (!string.Equals(previousTrackId, newTrackId, StringComparison.OrdinalIgnoreCase))
                {
                    var removed = Leaderboard.ClearRecordsForTrackId(previousTrackId);
                    if (removed > 0)
                    {
                        RunningManPlugin.Log.LogInfo(
                            $"Track layout changed; cleared {removed} world record(s) for previous layout.");
                    }
                }
            }

            SyncTrack();
            BroadcastState();
        }

        public void SetTrackName(string name)
        {
            JsonStorage.Track.Name = name?.Trim() ?? string.Empty;
            JsonStorage.SaveTrack();
            SyncTrack();
            BroadcastState();
        }

        public int ClearTrackWorldRecords()
        {
            var removed = Leaderboard.ClearWorldRecords(JsonStorage.Track);
            BroadcastState();
            return removed;
        }

        public int ClearAllWorldRecords()
        {
            var removed = Leaderboard.ClearAllWorldRecords();
            BroadcastState();
            return removed;
        }

        private static void RenumberCheckpoints(TrackConfig track)
        {
            for (var i = 0; i < track.Checkpoints.Count; i++)
            {
                track.Checkpoints[i].Index = i + 1;
            }
        }

        public void SetDebugMode(bool enabled)
        {
            _debugMode = enabled;
            ModConfig.DebugMode.Value = enabled;
            ModConfig.ConfigFile.Save();
            BroadcastState();
        }

        public RaceStateSnapshot BuildClientSnapshot(DateTime nowUtc)
        {
            _debugMode = ModConfig.DebugMode.Value;
            var track = JsonStorage.Track;
            var snapshot = new RaceStateSnapshot
            {
                Phase = (int)_phase,
                CountdownEndUtcTicks = _countdownEndUtc.Ticks,
                DebugMode = _debugMode,
                RegistrationOpen = _registrationOpen,
                TotalCheckpoints = track.CheckpointCount,
                Registered = _registered.Values.ToList(),
                Runners = BuildStandings(nowUtc),
                AdminPlayerIds = ValheimUtil.BuildAdminPlayerIds(),
                TrackName = TrackIdentity.GetDisplayName(track),
                TrackId = TrackIdentity.GetId(track),
                WorldRecords = Leaderboard.BuildWorldRecordEntries(track, ModConfig.WorldRecordsLimit.Value)
            };

            var wr = Leaderboard.GetWorldRecords(track, 1);
            if (wr.Count > 0)
            {
                snapshot.ParTotalTimeMs = wr[0].TotalTimeMs;
                if (wr[0].CheckpointTimesMs != null)
                {
                    snapshot.ParCheckpointTimesMs = new List<long>(wr[0].CheckpointTimesMs);
                }
            }

            snapshot.AllowedGear = (JsonStorage.AllowedGear ?? AllowedGearRules.CreateDefaults()).Clone();
            return snapshot;
        }

        private List<RunnerSnapshot> BuildStandings(DateTime nowUtc)
        {
            var track = JsonStorage.Track;
            var runners = new List<RunnerSnapshot>();
            foreach (var session in _activeSessions.Values)
            {
                var position = session.HasPreviousPosition
                    ? session.LastPosition
                    : (ValheimUtil.TryGetParticipantPosition(session.PlayerId, out var live)
                        ? live
                        : Vector3.zero);

                var progress = TrackPath.GetProgress(track, position, session.NextCheckpointIndex, session.Finished);
                var lastCpMs = session.CheckpointTimesMs != null && session.CheckpointTimesMs.Count > 0
                    ? session.CheckpointTimesMs[session.CheckpointTimesMs.Count - 1]
                    : 0L;
                runners.Add(new RunnerSnapshot
                {
                    PlayerId = session.PlayerId,
                    PlayerName = session.PlayerName,
                    StartUtcTicks = session.StartUtc.Ticks,
                    NextCheckpointIndex = session.NextCheckpointIndex,
                    Finished = session.Finished,
                    FinishTimeMs = session.Finished || session.Disqualified
                        ? session.FinishTimeMs
                        : session.ElapsedMs(nowUtc),
                    PathProgress = progress,
                    Disqualified = session.Disqualified,
                    DisqualifiedReason = session.DisqualifiedReason ?? string.Empty,
                    FeatherCapeUnlocked = session.FeatherCapeUnlocked,
                    LastCheckpointTimeMs = lastCpMs
                });
            }

            var finished = runners.Where(r => r.Finished)
                .OrderBy(r => r.FinishTimeMs)
                .ToList();
            var active = runners.Where(r => !r.Finished && !r.Disqualified)
                .OrderByDescending(r => r.PathProgress)
                .ThenBy(r => r.FinishTimeMs)
                .ToList();
            var disqualified = runners.Where(r => r.Disqualified && !r.Finished)
                .OrderByDescending(r => r.PathProgress)
                .ThenBy(r => r.FinishTimeMs)
                .ToList();

            var ranked = new List<RunnerSnapshot>();
            var place = 1;
            foreach (var runner in finished)
            {
                runner.Place = place++;
                ranked.Add(runner);
            }

            foreach (var runner in active)
            {
                runner.Place = place++;
                ranked.Add(runner);
            }

            foreach (var runner in disqualified)
            {
                runner.Place = place++;
                ranked.Add(runner);
            }

            return ranked;
        }

        public void BroadcastState()
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                return;
            }

            RaceNetSync.SendRaceState(BuildClientSnapshot(DateTime.UtcNow));
        }

        public void SyncTrack()
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                return;
            }

            RaceNetSync.SendTrack(JsonStorage.Track);
            BroadcastState();
        }

        private bool sessionHasPrevious(long playerId) => _previousPositions.ContainsKey(playerId);
        private Vector3 GetPreviousPosition(long playerId) => _previousPositions[playerId];
        private void StorePreviousPosition(long playerId, Vector3 position) => _previousPositions[playerId] = position;
    }
}
