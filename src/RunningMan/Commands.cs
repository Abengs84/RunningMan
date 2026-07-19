using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RunningMan.Net;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// /run chat and console commands for players and admins.
    /// </summary>
    public static class Commands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            // Client F5/console must use our RPC path so admin checks run on the server.
            new Terminal.ConsoleCommand("run", "RunningMan marathon commands", HandleRunCommand,
                isCheat: false, isNetwork: false);
            RunningManPlugin.Log.LogInfo("Registered /run chat command.");
        }

        public static void ExecuteFromRemote(long senderPeerId, string argLine)
        {
            ZRpc rpc = null;
            if (ZNet.instance != null)
            {
                var peer = ZNet.instance.GetPeer(senderPeerId);
                rpc = peer?.m_rpc;
            }

            CommandContext.ExecutingFromRemote = true;
            CommandContext.SenderPeerId = senderPeerId;
            try
            {
                Execute(argLine, rpc);
            }
            finally
            {
                CommandContext.ExecutingFromRemote = false;
            }
        }

        public static void Execute(string argLine, ZRpc rpc)
        {
            CommandContext.CurrentRpc = rpc;
            if (rpc != null && ZNet.instance != null)
            {
                var peer = ZNet.instance.GetPeer(rpc);
                if (peer != null)
                {
                    CommandContext.SenderPeerId = peer.m_uid;
                }
            }

            var fullLine = "run" + (string.IsNullOrWhiteSpace(argLine) ? string.Empty : " " + argLine.Trim());
            HandleRunCommand(new Terminal.ConsoleEventArgs(fullLine, Console.instance));
        }

        private static void HandleRunCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                var subArgs = NormalizeArgs(args.Args);
                if (TryHandleClientLocalCommand(subArgs, args))
                {
                    return;
                }

                if (!ValheimUtil.IsServerAuthority() && !CommandContext.ExecutingFromRemote &&
                    ZNet.instance != null && !ZNet.instance.IsServer())
                {
                    ValheimUtil.RunCommand(string.Join(" ", subArgs));
                    return;
                }

                if (subArgs.Length == 0)
                {
                    PrintHelp(args.Context);
                    return;
                }

                var subcommand = subArgs[0].ToLowerInvariant();
                switch (subcommand)
                {
                    case "status":
                        HandleStatus(args);
                        break;
                    case "join":
                        HandleJoin(args);
                        break;
                    case "leave":
                        HandleLeave(args);
                        break;
                    case "gearcheck":
                    case "gear":
                        HandleGearCheck(args);
                        break;
                    case "loadout":
                        ValheimUtil.Reply(args.Context, GearValidator.FormatRequiredLoadout());
                        break;
                    case "admincheck":
                        HandleAdminCheck(args);
                        break;
                    case "last":
                        HandleLastRun(args);
                        break;
                    case "pb":
                        HandlePersonalBest(args);
                        break;
                    case "open":
                        RequireAdmin(args, a =>
                        {
                            RaceManager.Instance.OpenRegistration();
                            ValheimUtil.Reply(a.Context, "Registration opened.");
                        });
                        break;
                    case "close":
                        RequireAdmin(args, a =>
                        {
                            RaceManager.Instance.CloseRegistration();
                            ValheimUtil.Reply(a.Context, "Registration closed.");
                        });
                        break;
                    case "start":
                        RequireAdmin(args, HandleStart);
                        break;
                    case "cancel":
                        RequireAdmin(args, a =>
                        {
                            RaceManager.Instance.CancelEvent();
                            ValheimUtil.Reply(a.Context, "Event cancelled.");
                        });
                        break;
                    case "debug":
                        RequireAdmin(args, HandleDebug);
                        break;
                    case "leaderboard":
                    case "worldrecords":
                    case "records":
                        HandleWorldRecords(args);
                        break;
                    case "reset":
                        RequireAdmin(args, HandleReset);
                        break;
                    case "export":
                        RequireAdmin(args, HandleExport);
                        break;
                    case "reload":
                        RequireAdmin(args, HandleReload);
                        break;
                    case "sync":
                        HandleSync(args);
                        break;
                    case "register":
                        RequireAdmin(args, HandleRegister);
                        break;
                    case "remove":
                        RequireAdmin(args, HandleRemove);
                        break;
                    case "clear":
                        RequireAdmin(args, HandleClear);
                        break;
                    case "autodetect":
                        RequireAdmin(args, HandleAutoDetect);
                        break;
                    case "trackname":
                        RequireAdmin(args, HandleTrackName);
                        break;
                    case "gearset":
                        RequireAdmin(args, HandleGearSet);
                        break;
                    case "edit":
                        RequireAdmin(args, HandleEdit);
                        break;
                    case "setpoint":
                        RequireAdmin(args, HandleSetPoint);
                        break;
                    case "wrboard":
                    case "bulletin":
                        RequireAdmin(args, a => HandleBulletinBoard(a, RaceBulletinKind.Records));
                        break;
                    case "rulesboard":
                    case "rulesign":
                        RequireAdmin(args, a => HandleBulletinBoard(a, RaceBulletinKind.Rules));
                        break;
                    default:
                        ValheimUtil.Reply(args.Context, $"Unknown /run subcommand: {subcommand}");
                        PrintHelp(args.Context);
                        break;
                }
            }
            catch (Exception ex)
            {
                ValheimUtil.Reply(args.Context, $"RunningMan error: {ex.Message}");
                RunningManPlugin.Log.LogError(ex);
            }
        }

        /// <summary>
        /// Chat sometimes includes the command name as the first argument (e.g. /run run).
        /// </summary>
        private static string[] NormalizeArgs(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Array.Empty<string>();
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                return args.Skip(1).ToArray();
            }

            return args;
        }

        private static void HandleStatus(Terminal.ConsoleEventArgs args)
        {
            if (!ValheimUtil.TryResolveCommandParticipant(out var participant))
            {
                ValheimUtil.ReplyHud("Could not resolve player for status.");
                return;
            }

            ValheimUtil.ReplyHud(RaceManager.Instance.FormatStatus(participant.PlayerId, DateTime.UtcNow));
        }

        private static void HandleJoin(Terminal.ConsoleEventArgs args)
        {
            if (!ValheimUtil.TryResolveCommandParticipant(out var participant))
            {
                ValheimUtil.ReplyHud("Could not resolve player.");
                return;
            }

            if (RaceManager.Instance.JoinPlayer(participant.PlayerId, participant.PlayerName))
            {
                ValheimUtil.ReplyHud($"{participant.PlayerName} joined the race.");
            }
            else
            {
                ValheimUtil.ReplyHud("Could not join — registration may be closed or you are already registered.");
            }
        }

        private static void HandleLeave(Terminal.ConsoleEventArgs args)
        {
            if (!ValheimUtil.TryResolveCommandParticipant(out var participant))
            {
                ValheimUtil.ReplyHud("Could not resolve player.");
                return;
            }

            if (RaceManager.Instance.LeavePlayer(participant.PlayerId))
            {
                ValheimUtil.ReplyHud($"{participant.PlayerName} left the race.");
            }
            else
            {
                ValheimUtil.ReplyHud("You are not registered.");
            }
        }

        private static void HandleStart(Terminal.ConsoleEventArgs args)
        {
            if (RaceManager.Instance.StartCountdown(out var failureReason))
            {
                ValheimUtil.Reply(args.Context, $"Countdown started ({ModConfig.CountdownSeconds.Value}s).");
            }
            else
            {
                ValheimUtil.Reply(args.Context,
                    string.IsNullOrEmpty(failureReason)
                        ? "Could not start countdown. Use /run open, have runners /run join, then /run start."
                        : failureReason);
            }
        }

        private static void HandleDebug(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            var enabled = ResolveDebugToggle(subArgs);
            if (!enabled.HasValue)
            {
                ValheimUtil.Reply(args.Context, "Usage: /run debug [on|off]");
                return;
            }

            RaceManager.Instance.SetDebugMode(enabled.Value);
            ValheimUtil.Reply(args.Context, $"Debug markers: {(enabled.Value ? "ON" : "OFF")}");
        }

        private static bool? ResolveDebugToggle(string[] subArgs)
        {
            if (subArgs.Length >= 2)
            {
                switch (subArgs[1].ToLowerInvariant())
                {
                    case "on":
                    case "true":
                    case "1":
                        return true;
                    case "off":
                    case "false":
                    case "0":
                        return false;
                }
            }

            var currentlyEnabled = RaceNetSync.IsDebugModeActive();
            return !currentlyEnabled;
        }

        private static bool TryHandleClientLocalCommand(string[] subArgs, Terminal.ConsoleEventArgs args)
        {
            if (subArgs.Length == 0 || Player.m_localPlayer == null || CommandContext.ExecutingFromRemote)
            {
                return false;
            }

            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return false;
            }

            switch (subArgs[0].ToLowerInvariant())
            {
                case "gearcheck":
                case "gear":
                    HandleGearCheck(args, Player.m_localPlayer);
                    return true;
                case "edit":
                    HandleEdit(args);
                    return true;
                case "loadout":
                    ValheimUtil.Reply(args.Context, GearValidator.FormatRequiredLoadout());
                    return true;
                default:
                    return false;
            }
        }

        private static void HandleGearCheck(Terminal.ConsoleEventArgs args, Player player = null)
        {
            player ??= ValheimUtil.ResolvePlayerForGearCheck();
            if (player == null)
            {
                ValheimUtil.Reply(args.Context, "Could not resolve player.");
                return;
            }

            var gear = RaceManager.Instance.CheckPlayerGear(player);
            var title = gear.IsValid ? "Gear check passed" : "Gear check failed";
            var body = gear.IsValid
                ? "Gear check passed."
                : "- " + string.Join("\n- ", gear.Issues);
            RaceGui.ShowInfoPanel(title, body, gear.IsValid ? 5f : 14f);
            ValheimUtil.Reply(args.Context, gear.Format(false));
        }

        private static void HandleAdminCheck(Terminal.ConsoleEventArgs args)
        {
            if (ValheimUtil.IsServerAuthority())
            {
                var rpc = CommandContext.CurrentRpc;
                var peer = rpc != null ? ZNet.instance.GetPeer(rpc) : null;
                var identity = ValheimUtil.FormatAdminIdentity(peer, rpc);
                var platformId = peer != null ? ValheimUtil.GetPlatformUserIdString(peer) : null;
                var admin = ValheimUtil.IsAdmin(rpc);
                ValheimUtil.Reply(args.Context, $"Server admin check: admin={admin}");
                ValheimUtil.Reply(args.Context, identity);
                if (!string.IsNullOrEmpty(platformId))
                {
                    ValheimUtil.Reply(args.Context,
                        $"Add this exact line to adminlist.txt on the server if crossplay is enabled: {platformId}");
                }

                ValheimUtil.ReplyHud(admin ? "Admin: true" : "Admin: false");

                // Push the same verdict the server just used for admincheck.
                if (CommandContext.SenderPeerId != 0)
                {
                    RaceNetSync.SendAdminStatusToPeer(CommandContext.SenderPeerId, admin);
                }

                return;
            }

            var localAdmin = (ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost()) ||
                             RaceNetSync.ClientIsAdmin;
            ValheimUtil.Reply(args.Context,
                $"Client: Valheim admin={ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost()}, synced admin={RaceNetSync.ClientIsAdmin}");
            ValheimUtil.ReplyHud(localAdmin ? "Admin: true" : "Admin: false");
        }

        private static void HandlePersonalBest(Terminal.ConsoleEventArgs args)
        {
            if (!ValheimUtil.TryResolveCommandParticipant(out var participant))
            {
                ValheimUtil.Reply(args.Context, "Could not resolve player.");
                return;
            }

            ValheimUtil.Reply(args.Context,
                Leaderboard.FormatPersonalBest(Leaderboard.GetPersonalBest(participant.PlayerId)));
        }

        private static void HandleLastRun(Terminal.ConsoleEventArgs args)
        {
            if (!ValheimUtil.TryResolveCommandParticipant(out var participant))
            {
                ValheimUtil.Reply(args.Context, "Could not resolve player.");
                return;
            }

            ValheimUtil.Reply(args.Context, Leaderboard.FormatLastRun(Leaderboard.GetLastRun(participant.PlayerId)));
        }

        private static void HandleWorldRecords(Terminal.ConsoleEventArgs args)
        {
            var text = Leaderboard.FormatWorldRecords(JsonStorage.Track, ModConfig.LeaderboardLimit.Value);
            ValheimUtil.Reply(args.Context, text);
            ValheimUtil.ReplyHud(text);
        }

        private static void HandleTrackName(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            if (subArgs.Length < 2)
            {
                ValheimUtil.Reply(args.Context,
                    $"Current track: {TrackIdentity.GetDisplayName(JsonStorage.Track)}. Usage: /run trackname <name>");
                return;
            }

            var name = string.Join(" ", subArgs.Skip(1));
            RaceManager.Instance.SetTrackName(name);
            ValheimUtil.Reply(args.Context, $"Track name set to: {name}");
        }

        private static void HandleGearSet(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            if (subArgs.Length < 2)
            {
                ValheimUtil.Reply(args.Context,
                    "Usage: /run gearset reset | /run gearset save <payload>  (use F6 Allowed gear)");
                ValheimUtil.Reply(args.Context, GearValidator.FormatRequiredLoadout());
                return;
            }

            switch (subArgs[1].ToLowerInvariant())
            {
                case "reset":
                case "defaults":
                    JsonStorage.ResetAllowedGearToDefaults();
                    RaceManager.Instance.BroadcastState();
                    ValheimUtil.Reply(args.Context, "Allowed gear reset to marathon defaults.");
                    ValheimUtil.Reply(args.Context, GearValidator.FormatRequiredLoadout());
                    break;
                case "save":
                case "set":
                    var payload = string.Join(" ", subArgs.Skip(2));
                    if (!TryParseGearSetPayload(payload, out var rules))
                    {
                        ValheimUtil.Reply(args.Context,
                            "Invalid gearset payload. Use F6 Allowed gear → Save.");
                        return;
                    }

                    JsonStorage.SetAllowedGear(rules);
                    RaceManager.Instance.BroadcastState();
                    ValheimUtil.Reply(args.Context, "Allowed gear saved.");
                    ValheimUtil.Reply(args.Context, GearValidator.FormatRequiredLoadout());
                    break;
                default:
                    ValheimUtil.Reply(args.Context,
                        "Usage: /run gearset reset | /run gearset save <payload>");
                    break;
            }
        }

        private static bool TryParseGearSetPayload(string payload, out AllowedGearRules rules)
        {
            rules = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            var parts = payload.Split(new[] { '§' }, StringSplitOptions.None);
            if (parts.Length < 11)
            {
                return false;
            }

            if (!int.TryParse(parts[8], out var anti) ||
                !int.TryParse(parts[9], out var ratatosk) ||
                !int.TryParse(parts[10], out _))
            {
                return false;
            }

            var current = JsonStorage.AllowedGear ?? AllowedGearRules.CreateDefaults();
            rules = current.Clone();
            rules.Helmet = parts[0] ?? string.Empty;
            rules.Chest = parts[1] ?? string.Empty;
            rules.Legs = parts[2] ?? string.Empty;
            rules.Cape = parts[3] ?? string.Empty;
            rules.AllowedHandItems = parts[4] ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(parts[5]))
            {
                rules.AntiStingPrefab = parts[5].Trim();
            }

            if (!string.IsNullOrWhiteSpace(parts[6]))
            {
                rules.RatatoskPrefab = parts[6].Trim();
            }

            // parts[7] / frost count: legacy frost-mead slots ignored (Feather cape covers frost).

            rules.RequiredAntiSting = Math.Max(0, anti);
            rules.RequiredRatatosk = Math.Max(0, ratatosk);

            if (parts.Length >= 17 &&
                int.TryParse(parts[14], out var salad) &&
                int.TryParse(parts[15], out var blood) &&
                int.TryParse(parts[16], out var omelette))
            {
                if (!string.IsNullOrWhiteSpace(parts[11]))
                {
                    rules.SaladPrefab = parts[11].Trim();
                }

                if (!string.IsNullOrWhiteSpace(parts[12]))
                {
                    rules.BloodPuddingPrefab = parts[12].Trim();
                }

                if (!string.IsNullOrWhiteSpace(parts[13]))
                {
                    rules.MushroomOmelettePrefab = parts[13].Trim();
                }

                rules.RequiredSalad = Math.Max(0, salad);
                rules.RequiredBloodPudding = Math.Max(0, blood);
                rules.RequiredMushroomOmelette = Math.Max(0, omelette);
            }

            return true;
        }

        private static void HandleEdit(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            bool enabled;
            if (subArgs.Length >= 2)
            {
                switch (subArgs[1].ToLowerInvariant())
                {
                    case "on":
                    case "true":
                    case "1":
                        enabled = true;
                        break;
                    case "off":
                    case "false":
                    case "0":
                        enabled = false;
                        break;
                    default:
                        ValheimUtil.Reply(args.Context, "Usage: /run edit [on|off]");
                        return;
                }
            }
            else
            {
                enabled = !RaceGateEditor.EditMode;
            }

            RaceGateEditor.SetEditMode(enabled);
            ValheimUtil.Reply(args.Context, enabled
                ? "Gate edit ON. Equip Hammer, look at endpoint dots, Use (E) to pick up / place."
                : "Gate edit OFF.");
        }

        private static void HandleSetPoint(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            // setpoint start a|b
            // setpoint finish a|b
            // setpoint checkpoint <index> a|b
            if (subArgs.Length < 3)
            {
                ValheimUtil.Reply(args.Context, "Usage: /run setpoint start|finish a|b  OR  /run setpoint checkpoint <index> a|b");
                return;
            }

            if (!TryResolveSearchOrigin(ResolvePlayer(args), out var origin))
            {
                ValheimUtil.Reply(args.Context, "Could not resolve position for setpoint.");
                return;
            }

            var kindToken = subArgs[1].ToLowerInvariant();
            RaceManager.GateKind kind;
            int checkpointIndex = 0;
            string endToken;

            if (kindToken == "start")
            {
                kind = RaceManager.GateKind.Start;
                endToken = subArgs[2];
            }
            else if (kindToken == "finish")
            {
                kind = RaceManager.GateKind.Finish;
                endToken = subArgs[2];
            }
            else if (kindToken == "checkpoint" || kindToken == "cp")
            {
                if (subArgs.Length < 4 ||
                    !int.TryParse(subArgs[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out checkpointIndex))
                {
                    ValheimUtil.Reply(args.Context, "Usage: /run setpoint checkpoint <index> a|b");
                    return;
                }

                kind = RaceManager.GateKind.Checkpoint;
                endToken = subArgs[3];
            }
            else
            {
                ValheimUtil.Reply(args.Context, "Usage: /run setpoint start|finish a|b  OR  /run setpoint checkpoint <index> a|b");
                return;
            }

            bool pointA;
            switch (endToken.ToLowerInvariant())
            {
                case "a":
                case "1":
                case "left":
                    pointA = true;
                    break;
                case "b":
                case "2":
                case "right":
                    pointA = false;
                    break;
                default:
                    ValheimUtil.Reply(args.Context, "Endpoint must be a or b.");
                    return;
            }

            Vector3? preferredForward = null;
            if (CommandContext.TryGetOrigin(out _, out var forward))
            {
                preferredForward = forward;
            }

            if (RaceManager.Instance.SetGateEndpoint(kind, checkpointIndex, pointA, origin, preferredForward))
            {
                var label = kind == RaceManager.GateKind.Checkpoint
                    ? $"CP{checkpointIndex}"
                    : kind.ToString().ToUpperInvariant();
                ValheimUtil.Reply(args.Context, $"Moved {label} point {(pointA ? "A" : "B")}.");
            }
            else
            {
                ValheimUtil.Reply(args.Context, "Could not move that endpoint (gate missing?).");
            }
        }

        private static void HandleReset(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            if (subArgs.Length < 2)
            {
                ValheimUtil.Reply(args.Context, "Usage: /run reset <player>");
                return;
            }

            var target = ValheimUtil.FindPlayerByName(subArgs[1]);
            if (target == null)
            {
                ValheimUtil.Reply(args.Context, $"Player not found: {subArgs[1]}");
                return;
            }

            if (RaceManager.Instance.ResetActiveRun(target.GetPlayerID()))
            {
                ValheimUtil.Reply(args.Context, $"Reset active run for {target.GetPlayerName()}.");
            }
            else
            {
                ValheimUtil.Reply(args.Context, $"{target.GetPlayerName()} has no active run.");
            }
        }

        private static void HandleExport(Terminal.ConsoleEventArgs args)
        {
            JsonStorage.ExportAll();
            ValheimUtil.Reply(args.Context, $"Exported race data to {JsonStorage.ExportFilePath}");
        }

        private static void HandleSync(Terminal.ConsoleEventArgs args)
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                ValheimUtil.Reply(args.Context, "State sync must run on the server.");
                return;
            }

            var snapshot = RaceManager.Instance.BuildClientSnapshot(DateTime.UtcNow);
            var peerId = CommandContext.SenderPeerId;
            if (peerId != 0)
            {
                RaceNetSync.SendStateToPeer(peerId, snapshot);
                RaceNetSync.SendTrackToPeer(peerId, JsonStorage.Track);
                return;
            }

            RaceManager.Instance.BroadcastState();
            ValheimUtil.Reply(args.Context, "RunningMan state refreshed.");
        }

        private static void HandleReload(Terminal.ConsoleEventArgs args)
        {
            ModConfig.ConfigFile.Reload();
            ModConfig.Initialize(ModConfig.ConfigFile);
            JsonStorage.Initialize(BepInEx.Paths.ConfigPath);
            RaceManager.Instance.SyncTrack();
            RaceNetSync.SendBulletins();
            ValheimUtil.Reply(args.Context, "RunningMan configuration and data reloaded.");
        }

        private static void HandleBulletinBoard(Terminal.ConsoleEventArgs args, RaceBulletinKind kind)
        {
            var label = kind == RaceBulletinKind.Rules ? "RULES" : "WR";
            var boards = kind == RaceBulletinKind.Rules ? JsonStorage.RulesBoards : JsonStorage.WrBoards;
            var subArgs = NormalizeArgs(args.Args);
            var action = subArgs.Length >= 2 ? subArgs[1].ToLowerInvariant() : "add";
            var usage = kind == RaceBulletinKind.Rules
                ? "Usage: /run rulesboard [add|remove|list|clear]"
                : "Usage: /run wrboard [add|remove|list|clear]";

            switch (action)
            {
                case "list":
                    ValheimUtil.Reply(args.Context,
                        boards.Count == 0
                            ? $"No {label} bulletin boards marked."
                            : $"{boards.Count} {label} bulletin board(s) marked.");
                    return;
                case "clear":
                    if (kind == RaceBulletinKind.Rules)
                    {
                        JsonStorage.SetRulesBoards(new List<Vector3Data>());
                    }
                    else
                    {
                        JsonStorage.SetWrBoards(new List<Vector3Data>());
                    }

                    RaceNetSync.SendBulletins();
                    ValheimUtil.Reply(args.Context, $"All {label} bulletin boards cleared.");
                    return;
                case "remove":
                {
                    if (!TryResolveBoardPosition(args, out var removePos))
                    {
                        ValheimUtil.Reply(args.Context,
                            $"Look at a marked {label} Sign (or stand near one) and run the remove command.");
                        return;
                    }

                    var bestIndex = -1;
                    var bestDist = float.MaxValue;
                    for (var i = 0; i < boards.Count; i++)
                    {
                        var dist = Vector3.Distance(removePos, ValheimUtil.FromData(boards[i]));
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestIndex = i;
                        }
                    }

                    if (bestIndex < 0 || bestDist > 8f)
                    {
                        ValheimUtil.Reply(args.Context, $"No {label} board within 8m to remove.");
                        return;
                    }

                    boards.RemoveAt(bestIndex);
                    if (kind == RaceBulletinKind.Rules)
                    {
                        JsonStorage.SaveRulesBoards();
                    }
                    else
                    {
                        JsonStorage.SaveWrBoards();
                    }

                    RaceNetSync.SendBulletins();
                    ValheimUtil.Reply(args.Context, $"Removed {label} board ({boards.Count} remaining).");
                    return;
                }
                case "add":
                default:
                {
                    if (action != "add" && subArgs.Length >= 2)
                    {
                        ValheimUtil.Reply(args.Context, usage);
                        return;
                    }

                    if (!TryResolveBoardPosition(args, out var addPos))
                    {
                        ValheimUtil.Reply(args.Context,
                            $"Look at a Hammer Sign and run /run {(kind == RaceBulletinKind.Rules ? "rulesboard" : "wrboard")}.");
                        return;
                    }

                    // Don't allow the same Sign to be both WR and RULES.
                    if (RaceWrBoards.TryFindBoardIndex(RaceBulletinKind.Records, addPos, 1.5f, out _)
                        || RaceWrBoards.TryFindBoardIndex(RaceBulletinKind.Rules, addPos, 1.5f, out _))
                    {
                        ValheimUtil.Reply(args.Context, "That Sign is already marked as a bulletin.");
                        return;
                    }

                    boards.Add(ValheimUtil.ToData(addPos));
                    if (kind == RaceBulletinKind.Rules)
                    {
                        JsonStorage.SaveRulesBoards();
                    }
                    else
                    {
                        JsonStorage.SaveWrBoards();
                    }

                    RaceNetSync.SendBulletins();
                    ValheimUtil.Reply(args.Context,
                        $"{label} Sign marked ({boards.Count} total). Walk up to it to read.");
                    return;
                }
            }
        }

        private static bool TryResolveBoardPosition(Terminal.ConsoleEventArgs args, out Vector3 position)
        {
            if (CommandContext.TryGetOrigin(out position, out _))
            {
                return true;
            }

            var player = ResolvePlayer(args);
            if (player != null)
            {
                position = player.transform.position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        private static void HandleRegister(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            if (subArgs.Length < 2)
            {
                ValheimUtil.Reply(args.Context, "Usage: /run register start|finish|checkpoint [width]");
                return;
            }

            var player = ResolvePlayer(args);
            var width = ParseRegistrationWidth(subArgs, 2);
            var gate = ValheimUtil.CreateRegistrationGate(player, ValheimUtil.ResolveCommandPeer(), width);
            if (gate == null)
            {
                ValheimUtil.Reply(args.Context, "Could not resolve player position for registration.");
                return;
            }

            switch (subArgs[1].ToLowerInvariant())
            {
                case "start":
                    RaceManager.Instance.RegisterStartGate(gate);
                    ValheimUtil.Reply(args.Context, $"Start gate registered (width {width:0.#}m). Enable debug to preview markers.");
                    break;
                case "finish":
                    RaceManager.Instance.RegisterFinishGate(gate);
                    ValheimUtil.Reply(args.Context, $"Finish gate registered (width {width:0.#}m). Enable debug to preview markers.");
                    break;
                case "checkpoint":
                    var checkpointIndex = RaceManager.Instance.RegisterCheckpoint(gate);
                    ValheimUtil.Reply(args.Context,
                        $"Checkpoint {checkpointIndex} registered (width {width:0.#}m). Later checkpoints were renumbered. Enable debug to preview markers.");
                    break;
                default:
                    ValheimUtil.Reply(args.Context, "Usage: /run register start|finish|checkpoint [width]");
                    break;
            }
        }

        private static void HandleRemove(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            if (subArgs.Length < 2)
            {
                ValheimUtil.Reply(args.Context, "Usage: /run remove start|finish|checkpoint <index|last>");
                return;
            }

            switch (subArgs[1].ToLowerInvariant())
            {
                case "start":
                    ValheimUtil.Reply(args.Context, RaceManager.Instance.ClearStartGate()
                        ? "Start gate removed."
                        : "No start gate is registered.");
                    break;
                case "finish":
                    ValheimUtil.Reply(args.Context, RaceManager.Instance.ClearFinishGate()
                        ? "Finish gate removed."
                        : "No finish gate is registered.");
                    break;
                case "checkpoint":
                    if (subArgs.Length < 3)
                    {
                        ValheimUtil.Reply(args.Context, "Usage: /run remove checkpoint <index|last|nearest>");
                        return;
                    }

                    if (subArgs[2].Equals("last", StringComparison.OrdinalIgnoreCase))
                    {
                        ValheimUtil.Reply(args.Context, RaceManager.Instance.RemoveLastCheckpoint()
                            ? "Last checkpoint removed."
                            : "No checkpoints are registered.");
                        return;
                    }

                    if (subArgs[2].Equals("nearest", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryResolveSearchOrigin(ResolvePlayer(args), out var origin))
                        {
                            ValheimUtil.Reply(args.Context, "Could not resolve position for nearest checkpoint removal.");
                            return;
                        }

                        ValheimUtil.Reply(args.Context, RaceManager.Instance.RemoveNearestCheckpoint(origin)
                            ? "Nearest checkpoint removed."
                            : "No checkpoints are registered.");
                        return;
                    }

                    if (!int.TryParse(subArgs[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                    {
                        ValheimUtil.Reply(args.Context, "Usage: /run remove checkpoint <index|last|nearest>");
                        return;
                    }

                    ValheimUtil.Reply(args.Context, RaceManager.Instance.RemoveCheckpoint(index)
                        ? $"Checkpoint {index} removed."
                        : $"Checkpoint {index} was not found.");
                    break;
                default:
                    ValheimUtil.Reply(args.Context, "Usage: /run remove start|finish|checkpoint <index|last|nearest>");
                    break;
            }
        }

        private static void HandleClear(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            if (subArgs.Length < 2)
            {
                ValheimUtil.Reply(args.Context, "Usage: /run clear checkpoints|start|finish|track|records [all]");
                return;
            }

            switch (subArgs[1].ToLowerInvariant())
            {
                case "records":
                case "worldrecords":
                    if (subArgs.Length >= 3 && subArgs[2].Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        var allRemoved = RaceManager.Instance.ClearAllWorldRecords();
                        ValheimUtil.Reply(args.Context, $"Cleared {allRemoved} world record(s) for all tracks.");
                    }
                    else
                    {
                        var recordsRemoved = RaceManager.Instance.ClearTrackWorldRecords();
                        ValheimUtil.Reply(args.Context, recordsRemoved > 0
                            ? $"Cleared {recordsRemoved} world record(s) for {TrackIdentity.GetDisplayName(JsonStorage.Track)}."
                            : "No world records for this track.");
                    }

                    break;
                case "checkpoints":
                case "checkpoint":
                    var checkpointsRemoved = RaceManager.Instance.ClearCheckpoints();
                    ValheimUtil.Reply(args.Context, checkpointsRemoved > 0
                        ? $"Removed {checkpointsRemoved} checkpoint(s)."
                        : "No checkpoints are registered.");
                    break;
                case "start":
                    ValheimUtil.Reply(args.Context, RaceManager.Instance.ClearStartGate()
                        ? "Start gate removed."
                        : "No start gate is registered.");
                    break;
                case "finish":
                    ValheimUtil.Reply(args.Context, RaceManager.Instance.ClearFinishGate()
                        ? "Finish gate removed."
                        : "No finish gate is registered.");
                    break;
                case "track":
                    RaceManager.Instance.ClearStartGate();
                    RaceManager.Instance.ClearFinishGate();
                    var checkpointCount = RaceManager.Instance.ClearCheckpoints();
                    ValheimUtil.Reply(args.Context, $"Track cleared ({checkpointCount} checkpoint(s) removed).");
                    break;
                default:
                    ValheimUtil.Reply(args.Context, "Usage: /run clear checkpoints|start|finish|track|records [all]");
                    break;
            }
        }

        private static float ParseRegistrationWidth(string[] subArgs, int widthArgIndex)
        {
            var width = ModConfig.GateRegistrationWidth.Value;
            if (subArgs.Length <= widthArgIndex)
            {
                return width;
            }

            if (float.TryParse(subArgs[widthArgIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var customWidth))
            {
                return Mathf.Clamp(customWidth, 1f, 50f);
            }

            return width;
        }

        private static void HandleAutoDetect(Terminal.ConsoleEventArgs args)
        {
            var subArgs = NormalizeArgs(args.Args);
            var player = ResolvePlayer(args);
            if (!TryResolveSearchOrigin(player, out var origin))
            {
                ValheimUtil.Reply(args.Context, "Could not resolve player for auto-detect.");
                return;
            }

            var radius = ModConfig.AutoDetectSearchRadius.Value;
            var target = subArgs.Length > 1 ? subArgs[1].ToLowerInvariant() : "checkpoints";

            switch (target)
            {
                case "start":
                    var start = TriggerDetector.DetectGraustenGate(origin, radius);
                    if (start == null)
                    {
                        ValheimUtil.Reply(args.Context, "No Grausten gate found nearby.");
                        return;
                    }

                    RaceManager.Instance.RegisterStartGate(start);
                    ValheimUtil.Reply(args.Context, "Start gate auto-detected.");
                    break;
                case "finish":
                    var finish = TriggerDetector.DetectGraustenGate(origin, radius);
                    if (finish == null)
                    {
                        ValheimUtil.Reply(args.Context, "No Grausten gate found nearby.");
                        return;
                    }

                    RaceManager.Instance.RegisterFinishGate(finish);
                    ValheimUtil.Reply(args.Context, "Finish gate auto-detected.");
                    break;
                case "checkpoints":
                    var checkpoints = TriggerDetector.DetectTorchCheckpoints(
                        origin, radius,
                        ModConfig.AutoDetectTorchPairMinDistance.Value,
                        ModConfig.AutoDetectTorchPairMaxDistance.Value);
                    if (checkpoints.Count == 0)
                    {
                        ValheimUtil.Reply(args.Context, "No torch checkpoint pairs found nearby.");
                        return;
                    }

                    RaceManager.Instance.ReplaceCheckpoints(checkpoints);
                    ValheimUtil.Reply(args.Context, $"Auto-detected {checkpoints.Count} checkpoint pair(s).");
                    break;
                case "checkpoint":
                case "cp":
                case "nearest":
                case "here":
                    var nearestRadius = ModConfig.AutoDetectNearestSearchRadius.Value;
                    var nearest = TriggerDetector.DetectNearestTorchPair(
                        origin, nearestRadius,
                        ModConfig.AutoDetectTorchPairMinDistance.Value,
                        ModConfig.AutoDetectTorchPairMaxDistance.Value);
                    if (nearest == null)
                    {
                        ValheimUtil.Reply(args.Context,
                            $"No valid Standing Iron Torch pair within {nearestRadius:0.#}m. Need two torches {ModConfig.AutoDetectTorchPairMinDistance.Value:0.#}-{ModConfig.AutoDetectTorchPairMaxDistance.Value:0.#}m apart.");
                        return;
                    }

                    var addedIndex = RaceManager.Instance.RegisterCheckpoint(nearest);
                    ValheimUtil.Reply(args.Context,
                        $"Added checkpoint {addedIndex} from the two nearest Standing Iron Torches (later CPs renumbered).");
                    break;
                default:
                    ValheimUtil.Reply(args.Context,
                        "Usage: /run autodetect start|finish|checkpoints|checkpoint");
                    break;
            }
        }

        private static void RequireAdmin(Terminal.ConsoleEventArgs args, Action<Terminal.ConsoleEventArgs> action)
        {
            if (!ValheimUtil.IsServerAuthority())
            {
                ValheimUtil.Reply(args.Context, "RunningMan admin commands must run on the server.");
                return;
            }

            if (!ValheimUtil.IsAdmin(CommandContext.CurrentRpc))
            {
                var peer = ValheimUtil.ResolveCommandPeer();
                var identity = ValheimUtil.FormatAdminIdentity(peer, CommandContext.CurrentRpc);
                RunningManPlugin.Log.LogWarning($"RunningMan admin denied for {identity}.");
                ValheimUtil.Reply(args.Context,
                    "Admin privileges required. If this server uses crossplay, adminlist.txt needs the Platform ID from F2, e.g. Steam_7656119...");
                ValheimUtil.Reply(args.Context, $"Server sees: {identity}");
                return;
            }

            action(args);
        }

        private static bool TryResolveSearchOrigin(Player player, out Vector3 origin)
        {
            if (player != null)
            {
                origin = player.transform.position;
                return true;
            }

            if (CommandContext.TryGetOrigin(out origin, out _))
            {
                return true;
            }

            var peer = ValheimUtil.ResolveCommandPeer();
            if (peer != null && ValheimUtil.TryGetPeerWorldPosition(peer, out origin))
            {
                return true;
            }

            origin = Vector3.zero;
            return false;
        }

        private static Player ResolvePlayer(Terminal.ConsoleEventArgs args)
        {
            return ValheimUtil.ResolvePlayerForGearCheck();
        }

        private static void PrintHelp(Terminal context)
        {
            var help = new StringBuilder();
            help.AppendLine("RunningMan — press F6 for GUI");
            help.AppendLine("Player:");
            help.AppendLine("  /run join | leave | status | gearcheck | loadout | admincheck | pb | last");
            help.AppendLine("Admin:");
            help.AppendLine("  /run open | close | start | cancel | debug");
            help.AppendLine("  /run register start|finish|checkpoint [width]");
            help.AppendLine("  /run remove start|finish|checkpoint <index|last>");
            help.AppendLine("  /run clear checkpoints|start|finish|track");
            help.AppendLine("  /run autodetect start|finish|checkpoints|checkpoint");
            help.AppendLine("  /run edit [on|off]  (hammer + Use to move gate endpoints)");
            help.AppendLine("  /run setpoint start|finish a|b | checkpoint <n> a|b");
            help.AppendLine("  /run worldrecords | trackname <name> | gearset reset|save");
            help.AppendLine("  /run wrboard [add|remove|list|clear]  (look at a Hammer Sign)");
            help.AppendLine("  /run rulesboard [add|remove|list|clear]  (RULES Sign)");
            help.AppendLine("  /run clear records | clear records all");
            help.AppendLine("  /run leaderboard | reset <player> | export | reload");
            ValheimUtil.Reply(context, help.ToString());
        }
    }
}
