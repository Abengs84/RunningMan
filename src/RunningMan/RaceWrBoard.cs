using System;
using System.Collections.Generic;
using System.Text;
using RunningMan.Net;
using RunningMan.Storage;
using TMPro;
using UnityEngine;

namespace RunningMan
{
    public enum RaceBulletinKind
    {
        Records,
        Rules
    }

    /// <summary>
    /// Bulletin boards use vanilla Signs: place with Hammer, mark with /run wrboard or /run rulesboard.
    /// </summary>
    public static class RaceWrBoards
    {
        public const float MatchDistance = 2.5f;
        public const float RefreshInterval = 1f;

        private static readonly HashSet<int> ConfiguredLayouts = new HashSet<int>();
        private static readonly Dictionary<int, Vector2> BaselineTextSizes = new Dictionary<int, Vector2>();
        private static readonly Color SignTextColor = Color.white;

        public static List<Vector3Data> GetBoardPositions(RaceBulletinKind kind)
        {
            if (ValheimUtil.IsServerAuthority())
            {
                return kind == RaceBulletinKind.Rules ? JsonStorage.RulesBoards : JsonStorage.WrBoards;
            }

            return kind == RaceBulletinKind.Rules
                ? RaceNetSync.ClientRulesBoards
                : RaceNetSync.ClientWrBoards;
        }

        public static bool TryGetBulletinKind(Sign sign, out RaceBulletinKind kind)
        {
            kind = RaceBulletinKind.Records;
            if (sign == null)
            {
                return false;
            }

            var pos = sign.transform.position;
            var hasRecords = TryFindBoardIndex(RaceBulletinKind.Records, pos, MatchDistance, out var recordsIndex);
            var hasRules = TryFindBoardIndex(RaceBulletinKind.Rules, pos, MatchDistance, out var rulesIndex);

            if (!hasRecords && !hasRules)
            {
                return false;
            }

            if (hasRecords && !hasRules)
            {
                kind = RaceBulletinKind.Records;
                return true;
            }

            if (hasRules && !hasRecords)
            {
                kind = RaceBulletinKind.Rules;
                return true;
            }

            // Prefer whichever mark is closer if both somehow match.
            var recordsDist = Vector3.Distance(pos,
                ValheimUtil.FromData(GetBoardPositions(RaceBulletinKind.Records)[recordsIndex]));
            var rulesDist = Vector3.Distance(pos,
                ValheimUtil.FromData(GetBoardPositions(RaceBulletinKind.Rules)[rulesIndex]));
            kind = rulesDist < recordsDist ? RaceBulletinKind.Rules : RaceBulletinKind.Records;
            return true;
        }

        public static bool IsMarkedSign(Sign sign)
        {
            return TryGetBulletinKind(sign, out _);
        }

        public static bool TryFindBoardIndex(RaceBulletinKind kind, Vector3 position, float maxDistance, out int index)
        {
            index = -1;
            var boards = GetBoardPositions(kind);
            if (boards == null || boards.Count == 0)
            {
                return false;
            }

            var bestDist = float.MaxValue;
            for (var i = 0; i < boards.Count; i++)
            {
                var board = boards[i];
                if (board == null)
                {
                    continue;
                }

                var dist = Vector3.Distance(position, ValheimUtil.FromData(board));
                if (dist <= maxDistance && dist < bestDist)
                {
                    bestDist = dist;
                    index = i;
                }
            }

            return index >= 0;
        }

        public static string BuildSignText(RaceBulletinKind kind)
        {
            return kind == RaceBulletinKind.Rules ? BuildRulesSignText() : BuildRecordsSignText();
        }

        public static string BuildRecordsSignText()
        {
            var builder = new StringBuilder(180);
            var state = RaceNetSync.ClientState;
            var trackName = state?.TrackName;
            if (string.IsNullOrWhiteSpace(trackName))
            {
                trackName = TrackIdentity.GetDisplayName(RaceNetSync.GetActiveTrack());
            }

            if (string.IsNullOrWhiteSpace(trackName))
            {
                trackName = "Marathon";
            }

            builder.Append("<color=#FFFFFF>");
            builder.Append(Truncate(trackName.Trim(), 18));
            builder.Append('\n');

            var lengthLabel = FormatTrackLength();
            if (!string.IsNullOrEmpty(lengthLabel))
            {
                builder.Append(lengthLabel);
                builder.Append('\n');
            }

            var records = state?.WorldRecords;
            if (records == null || records.Count == 0)
            {
                builder.Append("No records yet");
                builder.Append("</color>");
                return builder.ToString();
            }

            var limit = Math.Min(records.Count, ModConfig.WorldRecordsLimit?.Value ?? 5);
            for (var i = 0; i < limit; i++)
            {
                var record = records[i];
                var name = Truncate(record.PlayerName ?? "?", 10);
                var time = record.Time ?? "?";
                builder.Append(record.Place);
                builder.Append('.');
                builder.Append(name);
                builder.Append(' ');
                builder.Append(time);
                if (i < limit - 1)
                {
                    builder.Append('\n');
                }
            }

            builder.Append("</color>");
            return builder.ToString();
        }

        public static string BuildRulesSignText()
        {
            var rules = GearValidator.GetActiveRules() ?? AllowedGearRules.CreateDefaults();
            var builder = new StringBuilder(220);
            builder.Append("<color=#FFFFFF>");
            builder.Append("RULES\n");
            builder.Append(ShortArmorLine(rules));
            builder.Append('\n');
            builder.Append(ShortCapeLine(rules));
            builder.Append('\n');
            builder.Append(ShortConsumableLine(rules.RequiredAntiSting, "Anti-Sting"));
            builder.Append('\n');
            builder.Append(ShortConsumableLine(rules.RequiredRatatosk, "Ratatosk"));
            builder.Append('\n');
            builder.Append(ShortConsumableLine(rules.RequiredSalad, "Salad"));
            builder.Append('\n');
            builder.Append(ShortConsumableLine(rules.RequiredBloodPudding, "BloodPudding"));
            builder.Append('\n');
            builder.Append(ShortConsumableLine(rules.RequiredMushroomOmelette, "Omelette"));
            builder.Append('\n');
            builder.Append("No gear swaps\n");
            builder.Append("Pass between CP torches\n");
            builder.Append("Meads OK anytime\n");
            builder.Append("Only race foods\n");
            builder.Append("Any Forsaken power\n");
            builder.Append("Get Rested!");
            builder.Append("</color>");
            return builder.ToString();
        }

        public static void ApplySignText(Sign sign)
        {
            if (sign == null || sign.m_textWidget == null || !TryGetBulletinKind(sign, out var kind))
            {
                return;
            }

            ConfigureTextLayout(sign, kind);

            var text = BuildSignText(kind);
            if (sign.m_characterLimit < text.Length)
            {
                sign.m_characterLimit = Math.Max(500, text.Length);
            }

            if (!string.Equals(sign.m_textWidget.text, text, StringComparison.Ordinal))
            {
                sign.m_textWidget.text = text;
            }

            sign.m_currentText = text;
            sign.m_textWidget.color = SignTextColor;
        }

        public static string GetHoverLabel(RaceBulletinKind kind)
        {
            return kind == RaceBulletinKind.Rules
                ? "Sign\n<color=orange>RunningMan RULES</color>"
                : "Sign\n<color=orange>RunningMan world records</color>";
        }

        private static void ConfigureTextLayout(Sign sign, RaceBulletinKind kind)
        {
            var widget = sign.m_textWidget;
            if (widget == null)
            {
                return;
            }

            widget.color = SignTextColor;
            widget.enableAutoSizing = true;
            widget.fontSizeMin = 1.5f;
            widget.fontSizeMax = kind == RaceBulletinKind.Rules ? 9f : 10f;
            widget.textWrappingMode = TextWrappingModes.Normal;
            // Overflow so longer RULES lists are not clipped mid-board.
            widget.overflowMode = TextOverflowModes.Overflow;
            widget.alignment = TextAlignmentOptions.Center;
            widget.margin = Vector4.zero;

            var rt = widget.rectTransform;
            if (rt == null)
            {
                return;
            }

            var id = sign.GetInstanceID();
            if (!BaselineTextSizes.TryGetValue(id, out var baseline))
            {
                var width = rt.rect.width;
                var height = rt.rect.height;
                if (width <= 0.001f || height <= 0.001f)
                {
                    return;
                }

                baseline = new Vector2(width, height);
                BaselineTextSizes[id] = baseline;
            }

            // RULES needs a much larger face for gear + race tips.
            var widthMul = kind == RaceBulletinKind.Rules ? 3.4f : 1.55f;
            var heightMul = kind == RaceBulletinKind.Rules ? 6.2f : 1.45f;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, baseline.x * widthMul);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, baseline.y * heightMul);
            ConfiguredLayouts.Add(id);
        }

        public static bool TryGetLookedAtSign(out Sign sign, out Vector3 position)
        {
            sign = null;
            position = Vector3.zero;
            var player = Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }

            var ray = new Ray(player.GetEyePoint(), player.GetLookDir());
            var hits = Physics.RaycastAll(ray, 12f);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null)
                {
                    continue;
                }

                var found = hit.collider.GetComponentInParent<Sign>();
                if (found == null)
                {
                    continue;
                }

                sign = found;
                position = found.transform.position;
                return true;
            }

            return false;
        }

        private static string FormatTrackLength()
        {
            var path = TrackPath.Build(RaceNetSync.GetActiveTrack());
            if (path == null || path.TotalLength < 1f)
            {
                return string.Empty;
            }

            if (path.TotalLength >= 1000f)
            {
                return $"{path.TotalLength / 1000f:0.##} km";
            }

            return $"{Mathf.RoundToInt(path.TotalLength)} m";
        }

        private static string ShortArmorLine(AllowedGearRules rules)
        {
            if (IsTrollSet(rules))
            {
                return "Armor: Troll set";
            }

            return "Armor: see /run loadout";
        }

        private static string ShortCapeLine(AllowedGearRules rules)
        {
            if (string.IsNullOrWhiteSpace(rules.Cape))
            {
                return "Cape: none";
            }

            if (string.Equals(rules.Cape, GearRules.CapeFeather, StringComparison.OrdinalIgnoreCase))
            {
                return "Cape: Feather";
            }

            return "Cape: " + Truncate(rules.Cape, 14);
        }

        private static string ShortConsumableLine(int count, string label)
        {
            return $"{Math.Max(0, count)}x {label}";
        }

        private static bool IsTrollSet(AllowedGearRules rules)
        {
            return string.Equals(rules.Helmet, GearRules.HelmetTroll, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(rules.Chest, GearRules.ChestTroll, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(rules.Legs, GearRules.LegsTroll, StringComparison.OrdinalIgnoreCase);
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, Math.Max(1, maxChars - 1)) + "...";
        }
    }

    /// <summary>
    /// Keeps marked Signs' displayed text in sync with WR / rules content.
    /// </summary>
    public sealed class RaceWrBoardHud : MonoBehaviour
    {
        private float _nextRefresh;

        private void Update()
        {
            if (Time.time < _nextRefresh)
            {
                return;
            }

            _nextRefresh = Time.time + RaceWrBoards.RefreshInterval;
            RefreshNearbySigns();
        }

        private static void RefreshNearbySigns()
        {
            if (Player.m_localPlayer == null)
            {
                return;
            }

            var hasAny = (RaceWrBoards.GetBoardPositions(RaceBulletinKind.Records)?.Count ?? 0) > 0
                         || (RaceWrBoards.GetBoardPositions(RaceBulletinKind.Rules)?.Count ?? 0) > 0;
            if (!hasAny)
            {
                return;
            }

            var playerPos = Player.m_localPlayer.transform.position;
            var signs = UnityEngine.Object.FindObjectsByType<Sign>(FindObjectsSortMode.None);
            if (signs == null || signs.Length == 0)
            {
                return;
            }

            foreach (var sign in signs)
            {
                if (sign == null || Vector3.Distance(playerPos, sign.transform.position) > 64f)
                {
                    continue;
                }

                if (!RaceWrBoards.IsMarkedSign(sign))
                {
                    continue;
                }

                RaceWrBoards.ApplySignText(sign);
            }
        }
    }
}
