using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RunningMan.Net;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Time formatting helpers for race display and JSON export.
    /// </summary>
    public static class TimeFormatter
    {
        public static string FormatDuration(double totalSeconds)
        {
            if (totalSeconds < 0)
            {
                totalSeconds = 0;
            }

            var totalMs = (long)Math.Round(totalSeconds * 1000.0);
            var minutes = totalMs / 60000L;
            var seconds = totalMs % 60000L / 1000L;
            var tenths = totalMs % 1000L / 100L;

            if (minutes >= 60)
            {
                var hours = minutes / 60L;
                minutes %= 60L;
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3}",
                    hours, minutes, seconds, tenths);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2}",
                minutes, seconds, tenths);
        }

        public static string FormatDurationMs(long milliseconds)
        {
            return FormatDuration(milliseconds / 1000.0);
        }

        /// <summary>
        /// Signed gap vs world record. Negative = ahead (faster), positive = behind (slower).
        /// </summary>
        public static string FormatSignedDeltaMs(long deltaMs)
        {
            var sign = deltaMs > 0 ? "+" : deltaMs < 0 ? "-" : "";
            return sign + FormatDurationMs(Math.Abs(deltaMs));
        }

        public static string FormatOrdinal(int place)
        {
            if (place <= 0)
            {
                return "-";
            }

            var ones = place % 10;
            var tens = place % 100 / 10;
            if (tens == 1)
            {
                return place + "th";
            }

            switch (ones)
            {
                case 1:
                    return place + "st";
                case 2:
                    return place + "nd";
                case 3:
                    return place + "rd";
                default:
                    return place + "th";
            }
        }
    }

    public readonly struct CommandParticipant
    {
        public CommandParticipant(long playerId, string playerName, Player player)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            Player = player;
        }

        public long PlayerId { get; }
        public string PlayerName { get; }
        public Player Player { get; }
        public bool IsValid => PlayerId != 0 && !string.IsNullOrWhiteSpace(PlayerName);
    }

    /// <summary>
    /// Shared helpers for player lookup and chat output.
    /// </summary>
    public static class ValheimUtil
    {
        public static bool IsServerAuthority()
        {
            return ZNet.instance != null && (ZNet.instance.IsServer() || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null);
        }

        public static Player FindPlayerById(long playerId)
        {
            foreach (var player in Player.GetAllPlayers())
            {
                if (player.GetPlayerID() == playerId)
                {
                    return player;
                }
            }

            return null;
        }

        public static Player FindPlayerFromPeer(ZNetPeer peer)
        {
            if (peer == null)
            {
                return null;
            }

            var byUid = FindPlayerById(peer.m_uid);
            if (byUid != null)
            {
                return byUid;
            }

            if (ZNet.instance != null)
            {
                foreach (var player in Player.GetAllPlayers())
                {
                    var playerPeer = ZNet.instance.GetPeer(player.GetPlayerID());
                    if (playerPeer != null &&
                        (playerPeer.m_uid == peer.m_uid || playerPeer.m_characterID == peer.m_characterID))
                    {
                        return player;
                    }
                }
            }

            foreach (var player in Player.GetAllPlayers())
            {
                var nview = player?.m_nview;
                if (nview == null || !nview.IsValid())
                {
                    continue;
                }

                var zdo = nview.GetZDO();
                if (zdo != null && zdo.m_uid == peer.m_characterID)
                {
                    return player;
                }
            }

            if (ZNet.instance != null)
            {
                foreach (var playerInfo in ZNet.instance.GetPlayerList())
                {
                    if (playerInfo.m_characterID != peer.m_characterID)
                    {
                        continue;
                    }

                    var byName = FindPlayerByName(playerInfo.m_name);
                    if (byName != null)
                    {
                        return byName;
                    }
                }
            }

            if (!string.IsNullOrEmpty(peer.m_playerName))
            {
                return FindPlayerByName(peer.m_playerName);
            }

            return null;
        }

        public static bool IsClientLocalPlayerCommand(string argLine)
        {
            if (Player.m_localPlayer == null || ZNet.instance == null || ZNet.instance.IsServer())
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(argLine))
            {
                return false;
            }

            var subcommand = argLine.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0]
                .ToLowerInvariant();
            switch (subcommand)
            {
                case "gearcheck":
                case "gear":
                case "loadout":
                    return true;
                default:
                    return false;
            }
        }

        public static RaceGate CreateRegistrationGate(Player player, ZNetPeer peer, float width)
        {
            if (player != null)
            {
                return RaceGate.FromPlayerPosition(player, width);
            }

            if (CommandContext.TryGetOrigin(out var position, out var forward))
            {
                return RaceGate.FromPositionAndForward(position, forward, width);
            }

            if (peer != null)
            {
                return RaceGate.FromPeerPosition(peer, width);
            }

            return null;
        }

        public static Player FindPlayerByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (var player in Player.GetAllPlayers())
            {
                if (string.Equals(player.GetPlayerName(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return player;
                }
            }

            return null;
        }

        public static bool TryResolveCommandParticipant(out CommandParticipant participant)
        {
            var peer = ResolveCommandPeer();
            if (peer != null)
            {
                participant = new CommandParticipant(
                    peer.m_uid,
                    peer.m_playerName,
                    FindPlayerFromPeer(peer));
                return participant.IsValid;
            }

            if (Player.m_localPlayer != null)
            {
                participant = new CommandParticipant(
                    Player.m_localPlayer.GetPlayerID(),
                    Player.m_localPlayer.GetPlayerName(),
                    Player.m_localPlayer);
                return participant.IsValid;
            }

            participant = default;
            return false;
        }

        public static Player ResolvePlayerForGearCheck()
        {
            if (TryResolveCommandParticipant(out var participant) && participant.Player != null)
            {
                return participant.Player;
            }

            if (TryResolveCommandParticipant(out participant))
            {
                var byId = FindPlayerById(participant.PlayerId);
                if (byId != null)
                {
                    return byId;
                }

                var byName = FindPlayerByName(participant.PlayerName);
                if (byName != null)
                {
                    return byName;
                }
            }

            return Player.m_localPlayer;
        }

        public static bool IsAdmin(ZRpc rpc)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return false;
            }

            if (Player.m_localPlayer != null && !CommandContext.ExecutingFromRemote &&
                ZNet.instance.LocalPlayerIsAdminOrHost())
            {
                var localPlayerId = Player.m_localPlayer.GetPlayerID();
                if (CommandContext.SenderPeerId == 0 || CommandContext.SenderPeerId == localPlayerId)
                {
                    return true;
                }
            }

            var peer = ResolveCommandPeer(rpc);
            if (peer != null && IsAdminPeer(peer))
            {
                return true;
            }

            var socket = rpc?.GetSocket() ?? peer?.m_socket;
            if (socket != null)
            {
                if (socket.IsHost())
                {
                    return true;
                }

                if (IsAdminHostName(socket.GetHostName()))
                {
                    return true;
                }
            }

            // Dedicated-server console / docker attach (no connected player context).
            if (rpc == null && CommandContext.SenderPeerId == 0 && Player.m_localPlayer == null)
            {
                return true;
            }

            var detectedId = DescribePeerIdentity(peer, socket);
            RunningManPlugin.Log.LogWarning($"RunningMan admin check failed for {detectedId}.");
            return false;
        }

        public static ZNetPeer ResolveCommandPeer(ZRpc rpc = null)
        {
            if (ZNet.instance == null)
            {
                return null;
            }

            rpc ??= CommandContext.CurrentRpc;
            if (rpc != null)
            {
                var peer = ZNet.instance.GetPeer(rpc);
                if (peer != null)
                {
                    return peer;
                }
            }

            if (CommandContext.SenderPeerId != 0)
            {
                return ZNet.instance.GetPeer(CommandContext.SenderPeerId);
            }

            return null;
        }

        public static bool IsAdminPeer(ZNetPeer peer)
        {
            if (peer?.m_socket == null || ZNet.instance == null)
            {
                return false;
            }

            var hostName = peer.m_socket.GetHostName();
            if (IsAdminHostName(hostName))
            {
                return true;
            }

            var platformUserId = GetPlatformUserIdString(peer);
            if (IsIdOnAdminList(platformUserId))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Admin check for a connected peer that does not consult CommandContext
        /// (safe to call while handling another player's command).
        /// </summary>
        public static bool IsConnectedPeerAdmin(ZNetPeer peer)
        {
            if (peer == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return false;
            }

            if (IsAdminPeer(peer))
            {
                return true;
            }

            return peer.m_socket != null && peer.m_socket.IsHost();
        }

        private static bool IsIdOnAdminList(string id)
        {
            id = NormalizePlatformUserId(id);
            if (string.IsNullOrEmpty(id) || ZNet.instance == null)
            {
                return false;
            }

            var adminList = ZNet.instance.GetAdminList();
            if (adminList != null)
            {
                foreach (var entry in adminList)
                {
                    if (string.Equals(entry, id, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (ZNet.instance.IsAdmin(id))
            {
                return true;
            }

            if (id.IndexOf("_", StringComparison.Ordinal) < 0 &&
                ZNet.instance.IsAdmin("Steam_" + id))
            {
                return true;
            }

            var underscore = id.IndexOf("_", StringComparison.Ordinal);
            if (underscore > 0 && ZNet.instance.IsAdmin(id.Substring(underscore + 1)))
            {
                return true;
            }

            return false;
        }

        public static string GetPlatformUserIdString(ZNetPeer peer)
        {
            if (peer == null || ZNet.instance == null)
            {
                return null;
            }

            foreach (var playerInfo in ZNet.instance.GetPlayerList())
            {
                if (playerInfo.m_characterID == peer.m_characterID)
                {
                    return NormalizePlatformUserId(playerInfo.m_userInfo.ToString());
                }
            }

            return null;
        }

        public static string NormalizePlatformUserId(string platformUserId)
        {
            if (string.IsNullOrEmpty(platformUserId))
            {
                return platformUserId;
            }

            var open = platformUserId.LastIndexOf('(');
            var close = platformUserId.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                return platformUserId.Substring(open + 1, close - open - 1).Trim();
            }

            return platformUserId.Trim();
        }

        public static string FormatAdminIdentity(ZNetPeer peer, ZRpc rpc)
        {
            var socket = peer?.m_socket ?? rpc?.GetSocket();
            var platformUserId = peer != null ? GetPlatformUserIdString(peer) : null;
            return DescribePeerIdentity(peer, socket, platformUserId);
        }

        private static bool IsAdminHostName(string hostName)
        {
            if (string.IsNullOrEmpty(hostName) || ZNet.instance == null)
            {
                return false;
            }

            if (ZNet.instance.IsAdmin(hostName))
            {
                return true;
            }

            // Crossplay servers store Platform IDs like Steam_7656119...
            if (hostName.IndexOf("_", StringComparison.Ordinal) < 0 &&
                ZNet.instance.IsAdmin("Steam_" + hostName))
            {
                return true;
            }

            return false;
        }

        private static string DescribePeerIdentity(ZNetPeer peer, ISocket socket, string platformUserId = null)
        {
            platformUserId ??= peer != null ? GetPlatformUserIdString(peer) : null;
            var hostName = peer?.m_socket?.GetHostName() ?? socket?.GetHostName() ?? "(unknown)";
            var platformText = platformUserId ?? "(no platform id)";
            return $"hostName={hostName}, platformId={platformText}";
        }

        public static List<long> BuildAdminPlayerIds()
        {
            var adminIds = new List<long>();
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return adminIds;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer?.m_rpc == null || !IsAdminPeer(peer))
                {
                    continue;
                }

                if (peer.m_uid != 0 && !adminIds.Contains(peer.m_uid))
                {
                    adminIds.Add(peer.m_uid);
                }

                // Also include character player id — clients often compare against GetPlayerID().
                var player = FindPlayerFromPeer(peer);
                if (player != null)
                {
                    var playerId = player.GetPlayerID();
                    if (playerId != 0 && !adminIds.Contains(playerId))
                    {
                        adminIds.Add(playerId);
                    }
                }
            }

            return adminIds;
        }

        public static bool TryGetParticipantPosition(long playerId, out Vector3 position)
        {
            var player = FindPlayerById(playerId);
            if (player != null)
            {
                position = player.transform.position;
                return true;
            }

            if (ZNet.instance != null)
            {
                var peer = ZNet.instance.GetPeer(playerId);
                if (peer != null && TryGetPeerWorldPosition(peer, out position))
                {
                    return true;
                }

                foreach (var connected in ZNet.instance.GetPeers())
                {
                    if (connected != null && connected.m_uid == playerId &&
                        TryGetPeerWorldPosition(connected, out position))
                    {
                        return true;
                    }
                }
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Best available world position for a connected peer:
        /// Player transform → character ZDO → peer.m_refPos fallback.
        /// </summary>
        public static bool TryGetPeerWorldPosition(ZNetPeer peer, out Vector3 position)
        {
            position = Vector3.zero;
            if (peer == null)
            {
                return false;
            }

            var player = FindPlayerFromPeer(peer);
            if (player != null)
            {
                position = player.transform.position;
                return true;
            }

            if (TryGetCharacterZdoPosition(peer.m_characterID, out position))
            {
                return true;
            }

            // Streaming / LOD reference — less precise, but always available.
            position = peer.m_refPos;
            return true;
        }

        public static bool TryGetCharacterZdoPosition(ZDOID characterId, out Vector3 position)
        {
            position = Vector3.zero;
            if (ZDOMan.instance == null || characterId.IsNone())
            {
                return false;
            }

            var zdo = ZDOMan.instance.GetZDO(characterId);
            if (zdo == null || !zdo.IsValid())
            {
                return false;
            }

            position = zdo.GetPosition();
            return true;
        }

        public static void Reply(Terminal context, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (ZNet.instance != null && ZNet.instance.IsServer() && CommandContext.SenderPeerId != 0)
            {
                CommandContext.QueueReply(message);
            }

            var rpc = CommandContext.CurrentRpc;
            if (rpc == null && ZNet.instance != null && ZNet.instance.IsServer() &&
                CommandContext.SenderPeerId != 0)
            {
                rpc = ZNet.instance.GetPeer(CommandContext.SenderPeerId)?.m_rpc;
            }

            if (ZNet.instance != null && ZNet.instance.IsServer() && rpc != null)
            {
                ZNet.instance.RemotePrint(rpc, message);
            }

            if (context != null)
            {
                context.AddString(message);
            }
            else if (Console.instance != null)
            {
                Console.instance.Print(message);
            }

            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                RaceGuiLog.Add(message);
            }
        }

        public static void Broadcast(string message)
        {
            if (!ModConfig.EnableBroadcasts.Value || string.IsNullOrEmpty(message))
            {
                return;
            }

            if (Chat.instance != null)
            {
                Chat.instance.SendText(Talker.Type.Shout, message);
                return;
            }

            // Dedicated server: no local Chat — still log so operators can see race events.
            RunningManPlugin.Log.LogInfo(message);
        }

        /// <summary>
        /// Race-critical announcement: chat shout (if enabled) plus yellow center MessageHud for all clients.
        /// Use for countdown/GO/finish/DQ-style events. Keep <see cref="Reply"/> for command feedback.
        /// </summary>
        public static void Announce(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Broadcast(message);
            RaceNetSync.SendYellowHud(message);
        }

        /// <summary>
        /// Yellow MessageHud to the player who invoked the current /run command (F6 or chat).
        /// </summary>
        public static void ReplyHud(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            // MessageHud is single-line friendly — flatten multi-line replies.
            var hud = message.Replace("\r\n", "\n").Replace("\n", " · ");
            if (CommandContext.SenderPeerId != 0)
            {
                RaceNetSync.SendYellowHudToPlayer(CommandContext.SenderPeerId, hud);
                return;
            }

            RaceGui.ShowYellowHud(hud);
        }

        public static bool IsLocalPlayerAdmin()
        {
            if (ZNet.instance == null)
            {
                return false;
            }

            if (ZNet.instance.IsServer() && Player.m_localPlayer == null)
            {
                return true;
            }

            if (ZNet.instance.LocalPlayerIsAdminOrHost())
            {
                return true;
            }

            // Dedicated clients: server pushes an explicit admin bool (same check as /run admincheck).
            if (RaceNetSync.ClientIsAdmin)
            {
                return true;
            }

            return IsLocalPlayerInSyncedAdminList();
        }

        /// <summary>
        /// True when the local player appears in RaceNetSync.ClientState.AdminPlayerIds
        /// (peer uid and/or character player id).
        /// </summary>
        public static bool IsLocalPlayerInSyncedAdminList()
        {
            var admins = RaceNetSync.ClientState?.AdminPlayerIds;
            if (admins == null || admins.Count == 0)
            {
                return false;
            }

            foreach (var id in EnumerateLocalAdminLookupIds())
            {
                if (id != 0 && admins.Contains(id))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<long> EnumerateLocalAdminLookupIds()
        {
            if (Player.m_localPlayer != null)
            {
                var playerId = Player.m_localPlayer.GetPlayerID();
                if (playerId != 0)
                {
                    yield return playerId;
                }

                if (ZNet.instance != null)
                {
                    var peer = ZNet.instance.GetPeer(playerId);
                    if (peer != null && peer.m_uid != 0 && peer.m_uid != playerId)
                    {
                        yield return peer.m_uid;
                    }
                }
            }

            if (ZNet.instance == null)
            {
                yield break;
            }

            // Fallback: match by local player name against connected peers known to the client.
            var localName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
            if (string.IsNullOrWhiteSpace(localName))
            {
                yield break;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || peer.m_uid == 0)
                {
                    continue;
                }

                if (string.Equals(peer.m_playerName, localName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return peer.m_uid;
                }
            }
        }

        public static void RunCommand(string args)
        {
            CommandContext.ClearOrigin();
            CaptureClientOrigin(args);

            if (IsClientLocalPlayerCommand(args))
            {
                Commands.Execute(args, null);
                return;
            }

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                ZRpc rpc = null;
                long senderPeerId = 0;
                if (Player.m_localPlayer != null)
                {
                    var localPlayerId = Player.m_localPlayer.GetPlayerID();
                    var peer = ZNet.instance.GetPeer(localPlayerId);
                    if (peer == null)
                    {
                        foreach (var connectedPeer in ZNet.instance.GetPeers())
                        {
                            if (FindPlayerFromPeer(connectedPeer) == Player.m_localPlayer)
                            {
                                peer = connectedPeer;
                                break;
                            }
                        }
                    }

                    rpc = peer?.m_rpc;
                    senderPeerId = peer?.m_uid ?? localPlayerId;
                }

                CommandContext.SenderPeerId = senderPeerId;
                try
                {
                    Commands.Execute(args, rpc);
                }
                finally
                {
                    RaceNetSync.SyncToCommandSender();
                    RaceNetSync.FlushCommandReplies(CommandContext.SenderPeerId);
                    CommandContext.Clear();
                }

                return;
            }

            if (ZNet.instance != null && !ZNet.instance.IsServer() && ZRoutedRpc.instance != null)
            {
                RaceNetSync.SendRunCommand(args, CommandContext.HasOrigin
                    ? CommandContext.OriginPosition
                    : (Vector3?)null,
                    CommandContext.HasOrigin
                        ? CommandContext.OriginForward
                        : (Vector3?)null);
                return;
            }

            Commands.Execute(args, null);
        }

        private static void CaptureClientOrigin(string args)
        {
            if (Player.m_localPlayer == null || string.IsNullOrWhiteSpace(args))
            {
                return;
            }

            var parts = args.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            switch (parts[0].ToLowerInvariant())
            {
                case "register":
                case "autodetect":
                case "setpoint":
                    break;
                case "wrboard":
                case "bulletin":
                case "rulesboard":
                case "rulesign":
                    if (RaceWrBoards.TryGetLookedAtSign(out _, out var boardPos))
                    {
                        var boardForward = Player.m_localPlayer.transform.forward;
                        boardForward.y = 0f;
                        if (boardForward.sqrMagnitude < 0.0001f)
                        {
                            boardForward = Vector3.forward;
                        }
                        else
                        {
                            boardForward.Normalize();
                        }

                        CommandContext.SetOrigin(boardPos, boardForward);
                    }

                    return;
                default:
                    return;
            }

            var position = RaceGateEditor.PendingPlacePosition ?? Player.m_localPlayer.transform.position;
            RaceGateEditor.PendingPlacePosition = null;

            var forward = Player.m_localPlayer.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            CommandContext.SetOrigin(position, forward);
        }

        public static Vector3Data ToData(Vector3 vector)
        {
            return new Vector3Data(vector.x, vector.y, vector.z);
        }

        public static Vector3 FromData(Vector3Data data)
        {
            return new Vector3(data.X, data.Y, data.Z);
        }
    }

    /// <summary>
    /// Serializable 3D vector for JSON track files.
    /// </summary>
    [Serializable]
    public sealed class Vector3Data
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3Data()
        {
        }

        public Vector3Data(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
