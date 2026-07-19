using System;
using System.Collections.Generic;
using System.Linq;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan.Net
{
    /// <summary>
    /// Syncs race event state and track layout from server to clients.
    /// </summary>
    public static class RaceNetSync
    {
        public const string RpcRaceState = "RunningMan_RaceState";
        public const string RpcTrack = "RunningMan_Track";
        public const string RpcRunCommand = "RunningMan_RunCommand";
        public const string RpcCommandReply = "RunningMan_CommandReply";
        public const string RpcCountdownStart = "RunningMan_CountdownStart";
        public const string RpcRaceCue = "RunningMan_RaceCue";
        public const string RpcCenterMessage = "RunningMan_CenterMessage";
        public const string RpcGearViolation = "RunningMan_GearViolation";
        public const string RpcRunnerPosition = "RunningMan_RunnerPos";
        public const string RpcWrBoards = "RunningMan_WrBoards";
        public const string RpcAdminStatus = "RunningMan_AdminStatus";

        public const byte CueCheckpoint = 1;
        public const byte CueFinishFirst = 2;
        public const byte CueFalseStart = 3;

        public static RaceStateSnapshot ClientState { get; private set; } = new RaceStateSnapshot();
        public static TrackConfig ClientTrack { get; private set; } = new TrackConfig();
        public static AllowedGearRules ClientAllowedGear { get; private set; } = AllowedGearRules.CreateDefaults();
        public static List<Vector3Data> ClientWrBoards { get; private set; } = new List<Vector3Data>();
        public static List<Vector3Data> ClientRulesBoards { get; private set; } = new List<Vector3Data>();
        /// <summary>Server-confirmed admin flag for this client (dedicated-server safe).</summary>
        public static bool ClientIsAdmin { get; private set; }
        public static DateTime? LocalCountdownEndUtc { get; private set; }

        public static event Action StateUpdated;
        public static event Action TrackUpdated;

        public static bool IsDebugModeActive()
        {
            if (ClientState?.DebugMode == true)
            {
                return true;
            }

            return ValheimUtil.IsServerAuthority() && ModConfig.DebugMode.Value;
        }

        public static int GetTotalCheckpoints()
        {
            var fromState = ClientState?.TotalCheckpoints ?? 0;
            if (fromState > 0)
            {
                return fromState;
            }

            return ClientTrack?.CheckpointCount ?? 0;
        }

        public static void ApplyOptimisticDebug(bool enabled)
        {
            EnsureClientState();
            ClientState.DebugMode = enabled;
            StateUpdated?.Invoke();
        }

        public static void SetLocalCountdown(DateTime endUtc)
        {
            LocalCountdownEndUtc = endUtc;
            StateUpdated?.Invoke();
        }

        public static void ClearLocalCountdown()
        {
            if (!LocalCountdownEndUtc.HasValue)
            {
                return;
            }

            LocalCountdownEndUtc = null;
            StateUpdated?.Invoke();
        }

        public static bool IsCountdownActive()
        {
            return LocalCountdownEndUtc.HasValue && DateTime.UtcNow < LocalCountdownEndUtc.Value;
        }

        public static int GetCountdownRemainingSeconds()
        {
            if (!LocalCountdownEndUtc.HasValue)
            {
                return 0;
            }

            return Math.Max(0,
                (int)Math.Ceiling((LocalCountdownEndUtc.Value - DateTime.UtcNow).TotalSeconds));
        }

        public static void ApplyOptimisticCancel()
        {
            EnsureClientState();
            ClientState.Phase = (int)RaceEventPhase.Idle;
            ClientState.RegistrationOpen = false;
            ClientState.Registered.Clear();
            ClientState.Runners.Clear();
            ClientState.CountdownEndUtcTicks = 0;
            ClearLocalCountdown();
        }

        public static bool CanJoinRace()
        {
            var state = ClientState;
            if (state == null || !state.RegistrationOpen)
            {
                return false;
            }

            var phase = (RaceEventPhase)state.Phase;
            if (phase == RaceEventPhase.Racing || phase == RaceEventPhase.Countdown)
            {
                return false;
            }

            return !IsLocalRegistered();
        }

        public static void ApplyOptimisticRegistrationOpened()
        {
            EnsureClientState();
            ClientState.RegistrationOpen = true;
            ClientState.Phase = (int)RaceEventPhase.Registration;
            StateUpdated?.Invoke();
        }

        public static void ApplyOptimisticRegistrationClosed()
        {
            EnsureClientState();
            ClientState.RegistrationOpen = false;
            if (ClientState.Phase == (int)RaceEventPhase.Registration)
            {
                ClientState.Phase = ClientState.Registered != null && ClientState.Registered.Count > 0
                    ? (int)RaceEventPhase.Ready
                    : (int)RaceEventPhase.Idle;
            }

            StateUpdated?.Invoke();
        }

        public static TrackConfig GetActiveTrack()
        {
            if (ValheimUtil.IsServerAuthority())
            {
                return JsonStorage.Track;
            }

            return ClientTrack ?? new TrackConfig();
        }

        public static void SyncToCommandSender()
        {
            if (!ValheimUtil.IsServerAuthority() || RaceManager.Instance == null)
            {
                return;
            }

            var peerId = CommandContext.SenderPeerId;
            if (peerId == 0)
            {
                RaceManager.Instance.BroadcastState();
                RaceManager.Instance.SyncTrack();
                RaceNetSync.SendBulletins();
                RaceNetSync.BroadcastAdminStatus();
                return;
            }

            SendStateToPeer(peerId, RaceManager.Instance.BuildClientSnapshot(DateTime.UtcNow));
            SendTrackToPeer(peerId, JsonStorage.Track);
            SendBulletinsToPeer(peerId);
            SendAdminStatusToPeer(peerId);
        }

        public static void ApplyOptimisticTrackCommand(string[] parts)
        {
            if (parts == null || parts.Length == 0 || (ZNet.instance != null && ZNet.instance.IsServer()))
            {
                return;
            }

            EnsureClientTrack();
            switch (parts[0].ToLowerInvariant())
            {
                case "clear":
                    ApplyOptimisticClear(parts);
                    break;
                case "remove":
                    ApplyOptimisticRemove(parts);
                    break;
            }

            TrackUpdated?.Invoke();
            if (ClientState != null)
            {
                ClientState.TotalCheckpoints = ClientTrack.CheckpointCount;
            }

            StateUpdated?.Invoke();
        }

        private static void EnsureClientTrack()
        {
            if (ClientTrack == null)
            {
                ClientTrack = new TrackConfig();
            }

            if (ClientTrack.Checkpoints == null)
            {
                ClientTrack.Checkpoints = new List<Checkpoint>();
            }
        }

        private static void ApplyOptimisticClear(string[] parts)
        {
            if (parts.Length < 2)
            {
                return;
            }

            switch (parts[1].ToLowerInvariant())
            {
                case "start":
                    ClientTrack.StartGate = new RaceGate();
                    break;
                case "finish":
                    ClientTrack.FinishGate = new RaceGate();
                    break;
                case "checkpoint":
                case "checkpoints":
                    ClientTrack.Checkpoints.Clear();
                    break;
                case "track":
                    ClientTrack.StartGate = new RaceGate();
                    ClientTrack.FinishGate = new RaceGate();
                    ClientTrack.Checkpoints.Clear();
                    break;
            }
        }

        private static void ApplyOptimisticRemove(string[] parts)
        {
            if (parts.Length < 2)
            {
                return;
            }

            switch (parts[1].ToLowerInvariant())
            {
                case "start":
                    ClientTrack.StartGate = new RaceGate();
                    break;
                case "finish":
                    ClientTrack.FinishGate = new RaceGate();
                    break;
                case "checkpoint":
                    if (parts.Length >= 3 && parts[2].Equals("last", StringComparison.OrdinalIgnoreCase) &&
                        ClientTrack.Checkpoints.Count > 0)
                    {
                        ClientTrack.Checkpoints.RemoveAt(ClientTrack.Checkpoints.Count - 1);
                        return;
                    }

                    if (parts.Length >= 3 &&
                        int.TryParse(parts[2], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var index))
                    {
                        ClientTrack.Checkpoints.RemoveAll(item => item.Index == index);
                    }

                    break;
            }
        }

        private static bool ShouldReceiveClientSync()
        {
            return !ValheimUtil.IsServerAuthority() || Player.m_localPlayer != null;
        }

        private static bool IsLocalPeer(long peerId)
        {
            if (peerId == 0 || Player.m_localPlayer == null || ZNet.instance == null)
            {
                return false;
            }

            var playerId = Player.m_localPlayer.GetPlayerID();
            if (peerId == playerId)
            {
                return true;
            }

            var peer = ZNet.instance.GetPeer(playerId);
            return peer != null && peer.m_uid == peerId;
        }

        public static void RequestStateRefresh()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                RaceManager.Instance?.BroadcastState();
                RaceManager.Instance?.SyncTrack();
                return;
            }

            SendRunCommand("sync");
        }

        private static void EnsureClientState()
        {
            if (ClientState == null)
            {
                ClientState = new RaceStateSnapshot();
            }
        }

        public static void Register()
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            ZRoutedRpc.instance.Register(RpcRaceState, new Action<long, ZPackage>(ReceiveRaceState));
            ZRoutedRpc.instance.Register(RpcTrack, new Action<long, ZPackage>(ReceiveTrack));
            ZRoutedRpc.instance.Register(RpcWrBoards, new Action<long, ZPackage>(ReceiveWrBoards));
            ZRoutedRpc.instance.Register(RpcRunCommand, new Action<long, ZPackage>(ReceiveRunCommand));
            ZRoutedRpc.instance.Register(RpcCommandReply, new Action<long, ZPackage>(ReceiveCommandReply));
            ZRoutedRpc.instance.Register(RpcCountdownStart, new Action<long, ZPackage>(ReceiveCountdownStart));
            ZRoutedRpc.instance.Register(RpcRaceCue, new Action<long, ZPackage>(ReceiveRaceCue));
            ZRoutedRpc.instance.Register(RpcCenterMessage, new Action<long, ZPackage>(ReceiveCenterMessage));
            ZRoutedRpc.instance.Register(RpcGearViolation, new Action<long, ZPackage>(ReceiveGearViolation));
            ZRoutedRpc.instance.Register(RpcRunnerPosition, new Action<long, ZPackage>(ReceiveRunnerPosition));
            ZRoutedRpc.instance.Register(RpcAdminStatus, new Action<long, ZPackage>(ReceiveAdminStatus));
        }

        /// <summary>
        /// Client → server: authoritative local position for checkpoint detection on dedicated hosts.
        /// </summary>
        public static void SendRunnerPosition(long playerId, Vector3 position)
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            if (ValheimUtil.IsServerAuthority())
            {
                RaceManager.Instance?.ReportRunnerPosition(playerId, position);
                return;
            }

            var package = new ZPackage();
            package.Write(playerId);
            package.Write(position.x);
            package.Write(position.y);
            package.Write(position.z);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcRunnerPosition, package);
        }

        private static void ReceiveRunnerPosition(long sender, ZPackage package)
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                return;
            }

            try
            {
                var playerId = package.ReadLong();
                var position = new Vector3(package.ReadSingle(), package.ReadSingle(), package.ReadSingle());
                // Prefer peer uid for session lookup; also store under reported player id.
                RaceManager.Instance?.ReportRunnerPosition(sender, position);
                if (playerId != 0 && playerId != sender)
                {
                    RaceManager.Instance?.ReportRunnerPosition(playerId, position);
                }
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read runner position: {ex.Message}");
            }
        }

        public static void SendYellowHud(string message)
        {
            RaceGui.ShowYellowHud(message);

            if (!ValheimUtil.IsServerAuthority() || ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(1); // mode: yellow HUD only
            package.Write(string.Empty);
            package.Write(message ?? string.Empty);
            package.Write(6f);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcCenterMessage, ZRoutedRpc.Everybody, package);
        }

        public static void SendYellowHudToPlayer(long playerId, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (IsLocalPlayerTarget(playerId))
            {
                RaceGui.ShowYellowHud(message);
                return;
            }

            if (!ValheimUtil.IsServerAuthority() || ZRoutedRpc.instance == null)
            {
                return;
            }

            var peerId = ResolvePeerId(playerId);
            if (peerId == 0)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(1);
            package.Write(string.Empty);
            package.Write(message);
            package.Write(8f);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcCenterMessage, package);
        }

        /// <summary>
        /// Client reports illegal mid-race gear (dedicated servers cannot inspect remote Player inventory).
        /// </summary>
        public static void SendGearViolation(string reason)
        {
            if (string.IsNullOrEmpty(reason) || ZRoutedRpc.instance == null)
            {
                return;
            }

            if (ValheimUtil.IsServerAuthority())
            {
                var player = Player.m_localPlayer;
                if (player == null)
                {
                    return;
                }

                RaceManager.Instance?.ReportGearViolation(player.GetPlayerID(), reason);
                return;
            }

            var package = new ZPackage();
            package.Write(reason);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcGearViolation, package);
        }

        private static void ReceiveGearViolation(long sender, ZPackage package)
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                return;
            }

            try
            {
                var reason = package.ReadString();
                var playerId = sender;
                if (ZNet.instance != null)
                {
                    var peer = ZNet.instance.GetPeer(sender);
                    if (peer != null)
                    {
                        var player = ValheimUtil.FindPlayerFromPeer(peer);
                        if (player != null)
                        {
                            playerId = player.GetPlayerID();
                        }
                        else if (peer.m_uid != 0)
                        {
                            playerId = peer.m_uid;
                        }
                    }
                }

                RaceManager.Instance?.ReportGearViolation(playerId, reason);
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read gear violation: {ex.Message}");
            }
        }

        private static void ReceiveCenterMessage(long sender, ZPackage package)
        {
            if (ValheimUtil.IsServerAuthority())
            {
                return;
            }

            try
            {
                var mode = package.ReadInt();
                var title = package.ReadString();
                var body = package.ReadString();
                var seconds = package.ReadSingle();
                if (mode == 1)
                {
                    RaceGui.ShowYellowHud(body);
                }
                else
                {
                    RaceGui.ShowInfoPanel(title, body, seconds);
                }
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read center message: {ex.Message}");
            }
        }

        /// <summary>
        /// Immediately notifies a player to play a race sound (does not wait for the next state sync).
        /// </summary>
        public static void SendRaceCue(long playerId, byte cue)
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                return;
            }

            if (IsLocalPlayerTarget(playerId))
            {
                PlayRaceCue(cue);
                return;
            }

            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            var peerId = ResolvePeerId(playerId);
            if (peerId == 0)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(cue);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcRaceCue, package);
        }

        private static void ReceiveRaceCue(long sender, ZPackage package)
        {
            try
            {
                PlayRaceCue(package.ReadByte());
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read race cue: {ex.Message}");
            }
        }

        private static void PlayRaceCue(byte cue)
        {
            switch (cue)
            {
                case CueCheckpoint:
                    RaceSoundPlayer.PlayCheckpoint();
                    RaceSoundMonitor.NotifyCheckpointCue();
                    break;
                case CueFinishFirst:
                    RaceSoundPlayer.PlayFirstPlaceFinish();
                    RaceSoundMonitor.NotifyFinishCue();
                    break;
                case CueFalseStart:
                    RaceSoundPlayer.PlayFalseStart();
                    break;
            }
        }

        private static bool IsLocalPlayerTarget(long playerId)
        {
            if (Player.m_localPlayer == null)
            {
                return false;
            }

            if (Player.m_localPlayer.GetPlayerID() == playerId)
            {
                return true;
            }

            if (ZNet.instance == null)
            {
                return false;
            }

            var peer = ZNet.instance.GetPeer(Player.m_localPlayer.GetPlayerID());
            return peer != null && peer.m_uid == playerId;
        }

        private static long ResolvePeerId(long playerId)
        {
            if (ZNet.instance == null)
            {
                return 0;
            }

            var byId = ZNet.instance.GetPeer(playerId);
            if (byId != null)
            {
                return byId.m_uid;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null)
                {
                    continue;
                }

                if (peer.m_uid == playerId)
                {
                    return peer.m_uid;
                }

                var player = ValheimUtil.FindPlayerFromPeer(peer);
                if (player != null && player.GetPlayerID() == playerId)
                {
                    return peer.m_uid;
                }
            }

            return 0;
        }

        public static void SendCountdownStart(DateTime endUtc)
        {
            SetLocalCountdown(endUtc);

            if (!ValheimUtil.IsServerAuthority() || ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(endUtc.Ticks);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcCountdownStart, ZRoutedRpc.Everybody, package);
        }

        private static void ReceiveCountdownStart(long sender, ZPackage package)
        {
            if (ValheimUtil.IsServerAuthority())
            {
                return;
            }

            try
            {
                SetLocalCountdown(new DateTime(package.ReadLong(), DateTimeKind.Utc));
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read countdown sync: {ex.Message}");
            }
        }

        public static void FlushCommandReplies(long targetPeerId)
        {
            if (!ValheimUtil.IsServerAuthority() || targetPeerId == 0 || ZRoutedRpc.instance == null)
            {
                return;
            }

            var replies = CommandContext.TakePendingReplies();
            if (replies.Count == 0)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(replies.Count);
            foreach (var reply in replies)
            {
                package.Write(reply ?? string.Empty);
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(targetPeerId, RpcCommandReply, package);
        }

        public static void SendTrackToPeer(long peerId, TrackConfig track)
        {
            if (!ValheimUtil.IsServerAuthority() || track == null || peerId == 0)
            {
                return;
            }

            if (IsLocalPeer(peerId))
            {
                ApplyLocalTrack(track);
            }

            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            WriteTrack(package, track);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcTrack, package);
        }

        public static void SendStateToPeer(long peerId, RaceStateSnapshot snapshot)
        {
            if (!ValheimUtil.IsServerAuthority() || snapshot == null || peerId == 0)
            {
                return;
            }

            if (IsLocalPeer(peerId))
            {
                ApplyLocalSnapshot(snapshot);
            }

            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            WriteSnapshot(package, snapshot);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcRaceState, package);
        }

        private static void ReceiveCommandReply(long sender, ZPackage package)
        {
            if (ValheimUtil.IsServerAuthority())
            {
                return;
            }

            try
            {
                var count = package.ReadInt();
                for (var i = 0; i < count; i++)
                {
                    var line = package.ReadString();
                    RaceGuiLog.Add(line);
                    if (!string.IsNullOrEmpty(line) &&
                        (line.IndexOf("starting area", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("Not all participants", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        RaceGui.ShowYellowHud(line);
                    }
                }
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read command reply: {ex.Message}");
            }
        }

        public static void SendRunCommand(string args, Vector3? originPosition = null, Vector3? originForward = null)
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(args ?? string.Empty);
            var hasOrigin = originPosition.HasValue && originForward.HasValue;
            package.Write(hasOrigin);
            if (hasOrigin)
            {
                package.Write(originPosition.Value.x);
                package.Write(originPosition.Value.y);
                package.Write(originPosition.Value.z);
                package.Write(originForward.Value.x);
                package.Write(originForward.Value.y);
                package.Write(originForward.Value.z);
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcRunCommand, package);
        }

        private static void ReceiveRunCommand(long sender, ZPackage package)
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                return;
            }

            var argLine = package.ReadString();
            if (package.ReadBool())
            {
                var position = new Vector3(package.ReadSingle(), package.ReadSingle(), package.ReadSingle());
                var forward = new Vector3(package.ReadSingle(), package.ReadSingle(), package.ReadSingle());
                CommandContext.SetOrigin(position, forward);
            }

            RunningManPlugin.Log.LogInfo($"RunningMan RPC command from peer {sender}: run {argLine}");
            try
            {
                Commands.ExecuteFromRemote(sender, argLine);
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan command RPC failed: {ex}");
            }
            finally
            {
                RaceNetSync.SyncToCommandSender();
                FlushCommandReplies(sender);
                CommandContext.ExecutingFromRemote = false;
                CommandContext.Clear();
            }
        }

        public static void SendRaceState(RaceStateSnapshot snapshot)
        {
            if (!ValheimUtil.IsServerAuthority() || snapshot == null)
            {
                return;
            }

            ApplyLocalSnapshot(snapshot);

            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            WriteSnapshot(package, snapshot);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcRaceState, ZRoutedRpc.Everybody, package);
            BroadcastAdminStatus();
        }

        public static void BroadcastAdminStatus()
        {
            if (!ValheimUtil.IsServerAuthority() || ZNet.instance == null)
            {
                return;
            }

            // Listen-server / host.
            if (ZNet.instance.IsServer())
            {
                ClientIsAdmin = ZNet.instance.LocalPlayerIsAdminOrHost() || Player.m_localPlayer == null;
            }

            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || peer.m_uid == 0)
                {
                    continue;
                }

                SendAdminStatusToPeer(peer.m_uid);
            }
        }

        public static void SendAdminStatusToPeer(long peerId, bool? forceAdmin = null)
        {
            if (!ValheimUtil.IsServerAuthority() || peerId == 0)
            {
                return;
            }

            var isAdmin = forceAdmin ?? false;
            if (!forceAdmin.HasValue && ZNet.instance != null)
            {
                var peer = ZNet.instance.GetPeer(peerId);
                if (peer != null)
                {
                    // Do not call IsAdmin(rpc) here — it can pick up CommandContext.SenderPeerId
                    // and attribute admin to the wrong peer during broadcasts.
                    isAdmin = ValheimUtil.IsConnectedPeerAdmin(peer);
                }
                else if (IsLocalPeer(peerId))
                {
                    isAdmin = ZNet.instance.LocalPlayerIsAdminOrHost();
                }
            }

            if (IsLocalPeer(peerId))
            {
                ClientIsAdmin = isAdmin;
            }

            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(isAdmin);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcAdminStatus, package);
        }

        private static void ReceiveAdminStatus(long sender, ZPackage package)
        {
            try
            {
                ClientIsAdmin = package.ReadBool();
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read admin status: {ex.Message}");
            }
        }

        public static void ApplyLocalSnapshot(RaceStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            ClientState = snapshot;
            if (snapshot.AllowedGear != null)
            {
                ClientAllowedGear = snapshot.AllowedGear.Clone();
            }

            UpdateLocalCountdownFromSnapshot(snapshot);
            StateUpdated?.Invoke();
        }

        private static void UpdateLocalCountdownFromSnapshot(RaceStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            if (snapshot.Phase == (int)RaceEventPhase.Countdown && snapshot.CountdownEndUtcTicks > 0)
            {
                SetLocalCountdown(new DateTime(snapshot.CountdownEndUtcTicks, DateTimeKind.Utc));
                return;
            }

            if (snapshot.Phase == (int)RaceEventPhase.Racing ||
                snapshot.Phase == (int)RaceEventPhase.Idle)
            {
                ClearLocalCountdown();
            }
        }

        public static void ApplyLocalTrack(TrackConfig track)
        {
            if (track == null)
            {
                return;
            }

            ClientTrack = track;
            TrackUpdated?.Invoke();
        }

        public static void SendTrack(TrackConfig track)
        {
            if (!ValheimUtil.IsServerAuthority() || track == null)
            {
                return;
            }

            ApplyLocalTrack(track);

            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            WriteTrack(package, track);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcTrack, ZRoutedRpc.Everybody, package);
        }

        public static void ApplyLocalWrBoards(List<Vector3Data> boards)
        {
            ClientWrBoards = boards != null
                ? boards.Select(b => new Vector3Data(b.X, b.Y, b.Z)).ToList()
                : new List<Vector3Data>();
        }

        public static void ApplyLocalRulesBoards(List<Vector3Data> boards)
        {
            ClientRulesBoards = boards != null
                ? boards.Select(b => new Vector3Data(b.X, b.Y, b.Z)).ToList()
                : new List<Vector3Data>();
        }

        public static void SendBulletins()
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                return;
            }

            ApplyLocalWrBoards(JsonStorage.WrBoards);
            ApplyLocalRulesBoards(JsonStorage.RulesBoards);

            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            WriteWrBoards(package, JsonStorage.WrBoards);
            WriteWrBoards(package, JsonStorage.RulesBoards);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcWrBoards, ZRoutedRpc.Everybody, package);
        }

        public static void SendBulletinsToPeer(long peerId)
        {
            if (!ValheimUtil.IsServerAuthority() || peerId == 0)
            {
                return;
            }

            if (IsLocalPeer(peerId))
            {
                ApplyLocalWrBoards(JsonStorage.WrBoards);
                ApplyLocalRulesBoards(JsonStorage.RulesBoards);
            }

            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            WriteWrBoards(package, JsonStorage.WrBoards);
            WriteWrBoards(package, JsonStorage.RulesBoards);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcWrBoards, package);
        }

        private static void ReceiveWrBoards(long sender, ZPackage package)
        {
            if (ValheimUtil.IsServerAuthority())
            {
                return;
            }

            try
            {
                ApplyLocalWrBoards(ReadWrBoards(package));
                try
                {
                    ApplyLocalRulesBoards(ReadWrBoards(package));
                }
                catch
                {
                    ApplyLocalRulesBoards(new List<Vector3Data>());
                }
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read bulletin sync: {ex.Message}");
            }
        }

        private static void WriteWrBoards(ZPackage package, List<Vector3Data> boards)
        {
            package.Write(boards?.Count ?? 0);
            if (boards == null)
            {
                return;
            }

            foreach (var board in boards)
            {
                WriteVector(package, board);
            }
        }

        private static List<Vector3Data> ReadWrBoards(ZPackage package)
        {
            var boards = new List<Vector3Data>();
            var count = package.ReadInt();
            for (var i = 0; i < count; i++)
            {
                boards.Add(ReadVector(package));
            }

            return boards;
        }

        public static RunnerSnapshot GetLocalRunner()
        {
            if (Player.m_localPlayer == null || ClientState?.Runners == null)
            {
                return null;
            }

            var id = Player.m_localPlayer.GetPlayerID();
            var playerName = Player.m_localPlayer.GetPlayerName();
            foreach (var runner in ClientState.Runners)
            {
                if (runner.PlayerId == id)
                {
                    return runner;
                }
            }

            if (!string.IsNullOrWhiteSpace(playerName))
            {
                foreach (var runner in ClientState.Runners)
                {
                    if (string.Equals(runner.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return runner;
                    }
                }
            }

            if (ZNet.instance != null)
            {
                var peer = ZNet.instance.GetPeer(id);
                if (peer != null)
                {
                    foreach (var runner in ClientState.Runners)
                    {
                        if (runner.PlayerId == peer.m_uid)
                        {
                            return runner;
                        }
                    }
                }
            }

            return null;
        }

        public static bool IsLocalRegistered()
        {
            if (Player.m_localPlayer == null || ClientState?.Registered == null)
            {
                return false;
            }

            var id = Player.m_localPlayer.GetPlayerID();
            var playerName = Player.m_localPlayer.GetPlayerName();
            foreach (var participant in ClientState.Registered)
            {
                if (participant.PlayerId == id)
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(playerName))
            {
                foreach (var participant in ClientState.Registered)
                {
                    if (string.Equals(participant.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ReceiveRaceState(long sender, ZPackage package)
        {
            if (!ShouldReceiveClientSync())
            {
                return;
            }

            try
            {
                ClientState = ReadSnapshot(package);
                if (ClientState?.AllowedGear != null)
                {
                    ClientAllowedGear = ClientState.AllowedGear.Clone();
                }

                UpdateLocalCountdownFromSnapshot(ClientState);
                StateUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read race state sync: {ex.Message}");
            }
        }

        private static void ReceiveTrack(long sender, ZPackage package)
        {
            if (!ShouldReceiveClientSync())
            {
                return;
            }

            try
            {
                ClientTrack = ReadTrack(package);
                TrackUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"RunningMan failed to read track sync: {ex.Message}");
            }
        }

        private static void WriteSnapshot(ZPackage package, RaceStateSnapshot snapshot)
        {
            package.Write(snapshot.Phase);
            package.Write(snapshot.CountdownEndUtcTicks);
            package.Write(snapshot.DebugMode);
            package.Write(snapshot.RegistrationOpen);
            package.Write(snapshot.TotalCheckpoints);

            package.Write(snapshot.Registered.Count);
            foreach (var participant in snapshot.Registered)
            {
                package.Write(participant.PlayerId);
                package.Write(participant.PlayerName ?? string.Empty);
            }

            package.Write(snapshot.Runners.Count);
            foreach (var runner in snapshot.Runners)
            {
                package.Write(runner.PlayerId);
                package.Write(runner.PlayerName ?? string.Empty);
                package.Write(runner.StartUtcTicks);
                package.Write(runner.NextCheckpointIndex);
                package.Write(runner.Finished);
                package.Write(runner.FinishTimeMs);
                package.Write(runner.Place);
                package.Write(runner.Disqualified);
                package.Write(runner.DisqualifiedReason ?? string.Empty);
                package.Write(runner.FeatherCapeUnlocked);
                package.Write(runner.LastCheckpointTimeMs);
            }

            package.Write(snapshot.AdminPlayerIds?.Count ?? 0);
            if (snapshot.AdminPlayerIds != null)
            {
                foreach (var adminId in snapshot.AdminPlayerIds)
                {
                    package.Write(adminId);
                }
            }

            package.Write(snapshot.TrackName ?? string.Empty);
            package.Write(snapshot.TrackId ?? string.Empty);
            package.Write(snapshot.WorldRecords?.Count ?? 0);
            if (snapshot.WorldRecords != null)
            {
                foreach (var record in snapshot.WorldRecords)
                {
                    package.Write(record.Place);
                    package.Write(record.PlayerName ?? string.Empty);
                    package.Write(record.Time ?? string.Empty);
                    package.Write(record.TimeMs);
                    package.Write(record.Date ?? string.Empty);
                }
            }

            package.Write(snapshot.ParTotalTimeMs);
            package.Write(snapshot.ParCheckpointTimesMs?.Count ?? 0);
            if (snapshot.ParCheckpointTimesMs != null)
            {
                foreach (var timeMs in snapshot.ParCheckpointTimesMs)
                {
                    package.Write(timeMs);
                }
            }

            WriteAllowedGear(package, snapshot.AllowedGear ?? AllowedGearRules.CreateDefaults());
        }

        private static RaceStateSnapshot ReadSnapshot(ZPackage package)
        {
            var snapshot = new RaceStateSnapshot
            {
                Phase = package.ReadInt(),
                CountdownEndUtcTicks = package.ReadLong(),
                DebugMode = package.ReadBool(),
                RegistrationOpen = package.ReadBool(),
                TotalCheckpoints = package.ReadInt()
            };

            var registeredCount = package.ReadInt();
            for (var i = 0; i < registeredCount; i++)
            {
                snapshot.Registered.Add(new RegisteredParticipant
                {
                    PlayerId = package.ReadLong(),
                    PlayerName = package.ReadString()
                });
            }

            var runnerCount = package.ReadInt();
            for (var i = 0; i < runnerCount; i++)
            {
                snapshot.Runners.Add(new RunnerSnapshot
                {
                    PlayerId = package.ReadLong(),
                    PlayerName = package.ReadString(),
                    StartUtcTicks = package.ReadLong(),
                    NextCheckpointIndex = package.ReadInt(),
                    Finished = package.ReadBool(),
                    FinishTimeMs = package.ReadLong(),
                    Place = package.ReadInt(),
                    Disqualified = package.ReadBool(),
                    DisqualifiedReason = package.ReadString() ?? string.Empty,
                    FeatherCapeUnlocked = package.ReadBool(),
                    LastCheckpointTimeMs = package.ReadLong()
                });
            }

            var adminCount = package.ReadInt();
            for (var i = 0; i < adminCount; i++)
            {
                snapshot.AdminPlayerIds.Add(package.ReadLong());
            }

            try
            {
                snapshot.TrackName = package.ReadString();
                snapshot.TrackId = package.ReadString();
                var recordCount = package.ReadInt();
                for (var i = 0; i < recordCount; i++)
                {
                    snapshot.WorldRecords.Add(new WorldRecordEntry
                    {
                        Place = package.ReadInt(),
                        PlayerName = package.ReadString(),
                        Time = package.ReadString(),
                        TimeMs = package.ReadLong(),
                        Date = package.ReadString()
                    });
                }

                snapshot.ParTotalTimeMs = package.ReadLong();
                var parCount = package.ReadInt();
                for (var i = 0; i < parCount; i++)
                {
                    snapshot.ParCheckpointTimesMs.Add(package.ReadLong());
                }

                snapshot.AllowedGear = TryReadAllowedGear(package);
            }
            catch
            {
                snapshot.TrackName = string.Empty;
                snapshot.TrackId = string.Empty;
                snapshot.WorldRecords = new List<WorldRecordEntry>();
                snapshot.ParCheckpointTimesMs = new List<long>();
                snapshot.ParTotalTimeMs = 0;
                snapshot.AllowedGear = null;
            }

            return snapshot;
        }

        private static void WriteAllowedGear(ZPackage package, AllowedGearRules rules)
        {
            package.Write(rules?.Helmet ?? string.Empty);
            package.Write(rules?.Chest ?? string.Empty);
            package.Write(rules?.Legs ?? string.Empty);
            package.Write(rules?.Cape ?? string.Empty);
            package.Write(rules?.AllowedHandItems ?? string.Empty);
            package.Write(rules?.AntiStingPrefab ?? string.Empty);
            package.Write(rules?.RatatoskPrefab ?? string.Empty);
            // Legacy frost-mead slots kept empty for wire compatibility (Feather cape covers frost).
            package.Write(string.Empty);
            package.Write(rules?.RequiredAntiSting ?? 0);
            package.Write(rules?.RequiredRatatosk ?? 0);
            package.Write(0);
            package.Write(rules?.SaladPrefab ?? string.Empty);
            package.Write(rules?.BloodPuddingPrefab ?? string.Empty);
            package.Write(rules?.MushroomOmelettePrefab ?? string.Empty);
            package.Write(rules?.RequiredSalad ?? 0);
            package.Write(rules?.RequiredBloodPudding ?? 0);
            package.Write(rules?.RequiredMushroomOmelette ?? 0);
        }

        private static AllowedGearRules TryReadAllowedGear(ZPackage package)
        {
            try
            {
                var rules = new AllowedGearRules
                {
                    Helmet = package.ReadString() ?? string.Empty,
                    Chest = package.ReadString() ?? string.Empty,
                    Legs = package.ReadString() ?? string.Empty,
                    Cape = package.ReadString() ?? string.Empty,
                    AllowedHandItems = package.ReadString() ?? string.Empty,
                    AntiStingPrefab = package.ReadString() ?? string.Empty,
                    RatatoskPrefab = package.ReadString() ?? string.Empty
                };
                package.ReadString(); // legacy FrostResistPrefab
                rules.RequiredAntiSting = package.ReadInt();
                rules.RequiredRatatosk = package.ReadInt();
                package.ReadInt(); // legacy RequiredFrostResist

                try
                {
                    rules.SaladPrefab = package.ReadString() ?? GearRules.Salad;
                    rules.BloodPuddingPrefab = package.ReadString() ?? GearRules.BloodPudding;
                    rules.MushroomOmelettePrefab = package.ReadString() ?? GearRules.MushroomOmelette;
                    rules.RequiredSalad = package.ReadInt();
                    rules.RequiredBloodPudding = package.ReadInt();
                    rules.RequiredMushroomOmelette = package.ReadInt();
                }
                catch
                {
                    rules.SaladPrefab = GearRules.Salad;
                    rules.BloodPuddingPrefab = GearRules.BloodPudding;
                    rules.MushroomOmelettePrefab = GearRules.MushroomOmelette;
                    rules.RequiredSalad = 2;
                    rules.RequiredBloodPudding = 2;
                    rules.RequiredMushroomOmelette = 2;
                }

                return rules;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteTrack(ZPackage package, TrackConfig track)
        {
            WriteGate(package, track.StartGate);
            WriteGate(package, track.FinishGate);
            package.Write(track.Checkpoints?.Count ?? 0);
            if (track.Checkpoints != null)
            {
                foreach (var checkpoint in track.Checkpoints)
                {
                    package.Write(checkpoint.Index);
                    WriteGate(package, checkpoint.Gate);
                }
            }

            package.Write(track.Name ?? string.Empty);
        }

        private static TrackConfig ReadTrack(ZPackage package)
        {
            var track = new TrackConfig
            {
                StartGate = ReadGate(package),
                FinishGate = ReadGate(package)
            };

            var count = package.ReadInt();
            for (var i = 0; i < count; i++)
            {
                track.Checkpoints.Add(new Checkpoint(package.ReadInt(), ReadGate(package)));
            }

            try
            {
                track.Name = package.ReadString() ?? string.Empty;
            }
            catch
            {
                track.Name = string.Empty;
            }

            return track;
        }

        private static void WriteGate(ZPackage package, RaceGate gate)
        {
            WriteVector(package, gate?.PointA);
            WriteVector(package, gate?.PointB);
            WriteVector(package, gate?.Forward);
        }

        private static RaceGate ReadGate(ZPackage package)
        {
            return new RaceGate
            {
                PointA = ReadVector(package),
                PointB = ReadVector(package),
                Forward = ReadVector(package)
            };
        }

        private static void WriteVector(ZPackage package, Vector3Data vector)
        {
            package.Write(vector?.X ?? 0f);
            package.Write(vector?.Y ?? 0f);
            package.Write(vector?.Z ?? 0f);
        }

        private static Vector3Data ReadVector(ZPackage package)
        {
            return new Vector3Data(package.ReadSingle(), package.ReadSingle(), package.ReadSingle());
        }
    }
}
