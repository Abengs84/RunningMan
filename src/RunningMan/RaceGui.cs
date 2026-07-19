using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RunningMan.Net;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// F6 GUI panel for race status, registration, admin controls, and live standings.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class RaceGui : MonoBehaviour
    {
        private static RaceGui _instance;
        public static bool IsOpen => _instance != null && _instance._visible;

        private bool _visible;
        private Vector2 _scroll;
        private Vector2 _logScroll;
        private Rect _windowRect = new Rect(40f, 40f, 480f, 680f);
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _countdownStyle;
        private GUIStyle _checkpointStyle;
        private GUIStyle _hudTitleStyle;
        private GUIStyle _hudLineStyle;
        private GUIStyle _hudSubLineStyle;
        private GUIStyle _infoBodyStyle;
        private GUIStyle _finishBannerStyle;
        private GUIStyle _finishPlaceStyle;
        private GUIStyle _finishSubStyle;
        private GUIStyle _standingsTitleStyle;
        private GUIStyle _standingsLineStyle;
        private GUIStyle _standingsLocalLineStyle;
        private GUIStyle _standingsDisqualifiedLineStyle;
        private GUIStyle _f6DisqualifiedLabelStyle;
        private GUIStyle _parAheadStyle;
        private GUIStyle _parBehindStyle;
        private static Texture2D _hudBackground;
        private float _finishBannerUntil;
        private string _finishBannerPlace = string.Empty;
        private string _finishBannerText = string.Empty;
        private bool _localWasFinished;
        private float _nextClientSyncTime;
        private string _trackNameDraft = string.Empty;
        private bool _allowedGearDraftReady;
        private string _gearHelmetDraft = string.Empty;
        private string _gearChestDraft = string.Empty;
        private string _gearLegsDraft = string.Empty;
        private string _gearCapeDraft = string.Empty;
        private string _gearHandsDraft = string.Empty;
        private string _gearAntiStingDraft = "1";
        private string _gearRatatoskDraft = "2";
        private string _gearSaladDraft = "2";
        private string _gearBloodPuddingDraft = "2";
        private string _gearOmeletteDraft = "2";
        private float _infoPanelUntil;
        private string _infoPanelTitle = string.Empty;
        private string _infoPanelBody = string.Empty;

        public static void ShowInfoPanel(string title, string body, float seconds = 8f)
        {
            if (_instance == null)
            {
                RaceGuiLog.Add(body);
                return;
            }

            _instance._infoPanelTitle = title ?? "RunningMan";
            _instance._infoPanelBody = body ?? string.Empty;
            _instance._infoPanelUntil = Time.time + Mathf.Max(2f, seconds);
        }

        /// <summary>
        /// Valheim center HUD only (big yellow text) — no brown panel.
        /// </summary>
        public static void ShowYellowHud(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
            }
            else
            {
                RaceGuiLog.Add(message);
            }
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (!_visible)
            {
                GUI.FocusControl(null);
                return;
            }

            const float width = 480f;
            const float height = 680f;
            _windowRect = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _allowedGearDraftReady = false;
            RaceNetSync.RequestStateRefresh();
        }

        private void Awake()
        {
            _instance = this;
            RaceGuiLog.Updated += OnLogUpdated;
        }

        private void OnDestroy()
        {
            RaceGuiLog.Updated -= OnLogUpdated;
        }

        private void OnLogUpdated()
        {
            _logScroll = Vector2.zero;
        }

        private void Update()
        {
            if (Player.m_localPlayer == null)
            {
                return;
            }

            if (Input.GetKeyDown(ModConfig.GuiHotkey.Value))
            {
                SetVisible(!_visible);
            }

            HandleTrackHotkeys();

            if (ZNet.instance != null && !ZNet.instance.IsServer() && Time.time >= _nextClientSyncTime)
            {
                _nextClientSyncTime = Time.time + 1.5f;
                RaceNetSync.RequestStateRefresh();
            }
        }

        private void LateUpdate()
        {
            if (!_visible)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // Belt-and-suspenders against camera zoom while dragging F6 UI.
            Input.ResetInputAxes();
        }

        private void OnGUI()
        {
            if (_visible)
            {
                HandleGuiCloseInput();
                ConsumeScrollWheel();
            }

            DrawLiveHud();

            if (_visible)
            {
                EnsureStyles();
                _windowRect = GUI.Window(987654, _windowRect, DrawWindow, "RunningMan (F6 — Esc to close)");
            }

            // Draw after F6 so the banner is never covered by the admin window.
            if (Time.time < _infoPanelUntil)
            {
                EnsureStyles();
                DrawInfoPanel();
            }
        }

        private static void HandleGuiCloseInput()
        {
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.KeyDown || currentEvent.keyCode != KeyCode.Escape)
            {
                return;
            }

            GUI.FocusControl(null);
            if (_instance != null)
            {
                _instance.SetVisible(false);
            }

            currentEvent.Use();
        }

        private static void ConsumeScrollWheel()
        {
            if (Event.current.type == EventType.ScrollWheel)
            {
                Event.current.Use();
            }
        }

        private void DrawLiveHud()
        {
            if (!ModConfig.EnableHud.Value)
            {
                return;
            }

            var state = RaceNetSync.ClientState;
            if (state == null)
            {
                return;
            }

            EnsureStyles();
            var layoutMode = _visible;
            var local = RaceNetSync.GetLocalRunner();
            var bannerVisible = HudPanelLayout.ShouldDrawPanel(HudPanelId.RaceBanner);

            var showingFinish = Time.time < _finishBannerUntil;
            if (showingFinish &&
                state.Phase != (int)RaceEventPhase.Racing &&
                state.Phase != (int)RaceEventPhase.Countdown)
            {
                // Allow the timed post-finish banner to finish, but never re-show after Idle reconnect.
            }
            else if (state.Phase != (int)RaceEventPhase.Racing &&
                     state.Phase != (int)RaceEventPhase.Countdown)
            {
                showingFinish = false;
                _finishBannerUntil = 0f;
            }
            var showingCountdown = RaceNetSync.IsCountdownActive() ||
                                   (state.Phase == (int)RaceEventPhase.Countdown && state.CountdownEndUtcTicks > 0);

            if (bannerVisible)
            {
                if (showingFinish)
                {
                    DrawRaceBanner(finishMode: true);
                }
                else if (showingCountdown)
                {
                    var remaining = RaceNetSync.IsCountdownActive()
                        ? RaceNetSync.GetCountdownRemainingSeconds()
                        : Math.Max(0,
                            (int)Math.Ceiling(
                                (new DateTime(state.CountdownEndUtcTicks, DateTimeKind.Utc) - DateTime.UtcNow)
                                .TotalSeconds));
                    DrawRaceBanner(finishMode: false, remainingSeconds: remaining);
                }
                else if (layoutMode)
                {
                    DrawRaceBanner(finishMode: false, remainingSeconds: 3);
                }
            }

            var showStandings = (layoutMode || (ModConfig.EnableLiveStandings.Value && state.Phase == (int)RaceEventPhase.Racing))
                                && HudPanelLayout.ShouldDrawPanel(HudPanelId.Standings);
            if (showStandings)
            {
                DrawLiveStandings(state, layoutMode);
            }

            var showRunner = (layoutMode || IsActiveRaceRunnerHud(state, local))
                               && HudPanelLayout.ShouldDrawPanel(HudPanelId.Runner);
            if (showRunner)
            {
                if (local != null && local.StartUtcTicks > 0 &&
                    state.Phase == (int)RaceEventPhase.Racing)
                {
                    if (local.Finished && !_localWasFinished)
                    {
                        _finishBannerPlace = TimeFormatter.FormatOrdinal(local.Place);
                        _finishBannerText = TimeFormatter.FormatDurationMs(local.FinishTimeMs);
                        _finishBannerUntil = Time.time + 8f;
                    }

                    _localWasFinished = local.Finished;
                }
                else if (state.Phase != (int)RaceEventPhase.Racing)
                {
                    // Keep a timed finish banner if it was already showing; do not re-arm from stale state.
                    if (Time.time >= _finishBannerUntil)
                    {
                        _localWasFinished = false;
                    }
                }

                DrawRunnerOverlay(local, layoutMode);
            }
            else
            {
                if (state.Phase != (int)RaceEventPhase.Racing && Time.time >= _finishBannerUntil)
                {
                    _finishBannerUntil = 0f;
                }

                _localWasFinished = false;
            }

            // Registration is independent of Race stats so both can show at once while waiting.
            var showRegistered = (layoutMode ||
                                 (RaceNetSync.IsLocalRegistered() &&
                                  (state.Phase == (int)RaceEventPhase.Registration ||
                                   state.Phase == (int)RaceEventPhase.Ready)))
                                 && HudPanelLayout.ShouldDrawPanel(HudPanelId.Registered);
            if (showRegistered)
            {
                DrawRegisteredOverlay(state, layoutMode);
            }
        }

        private static bool IsActiveRaceRunnerHud(RaceStateSnapshot state, RunnerSnapshot local)
        {
            if (state == null || local == null || local.StartUtcTicks <= 0)
            {
                return false;
            }

            // Only during an active race — not after Idle reconnect with leftover runner rows.
            return state.Phase == (int)RaceEventPhase.Racing ||
                   state.Phase == (int)RaceEventPhase.Countdown;
        }

        private void DrawRaceBanner(bool finishMode, int remainingSeconds = 0)
        {
            Rect defaultRect;
            if (finishMode)
            {
                var placeFontSize = ModConfig.CountdownFontSize.Value + 12;
                var placeWidth = placeFontSize * 2.5f;
                var placeHeight = placeFontSize * 1.6f;
                var subHeight = 36f;
                var width = placeWidth + 48f;
                var height = placeHeight + subHeight + 24f;
                defaultRect = new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height * 0.32f - height * 0.5f,
                    width, height);
            }
            else
            {
                var fontSize = ModConfig.CountdownFontSize.Value;
                var width = fontSize * 3f;
                var height = fontSize * 1.8f;
                defaultRect = new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height * 0.28f - height * 0.5f,
                    width, height);
            }

            if (!HudPanelLayout.Resolve(HudPanelId.RaceBanner, defaultRect, _visible, "Race banner", out var panelRect))
            {
                return;
            }

            var contentRect = HudPanelLayout.ContentRect(panelRect, _visible);
            var previousDepth = GUI.depth;
            GUI.depth = -1000;

            if (finishMode)
            {
                var placeFontSize = ModConfig.CountdownFontSize.Value + 12;
                var placeWidth = placeFontSize * 2.5f;
                var placeHeight = placeFontSize * 1.6f;
                var placeRect = new Rect(contentRect.x + (contentRect.width - placeWidth) * 0.5f, contentRect.y + 4f,
                    placeWidth, placeHeight);
                var subRect = new Rect(contentRect.x + (contentRect.width - 420f) * 0.5f, placeRect.yMax + 4f, 420f, 36f);
                GUI.Label(placeRect, string.IsNullOrEmpty(_finishBannerPlace) ? "1st" : _finishBannerPlace,
                    _finishPlaceStyle);
                GUI.Label(subRect,
                    $"Finished in {(string.IsNullOrEmpty(_finishBannerText) ? "12:34" : _finishBannerText)}",
                    _finishSubStyle);
            }
            else
            {
                var label = remainingSeconds > 0 ? remainingSeconds.ToString() : "GO!";
                GUI.Label(contentRect, label, _countdownStyle);
            }

            GUI.depth = previousDepth;
        }

        private void DrawLiveStandings(RaceStateSnapshot state, bool layoutMode)
        {
            if (state.Runners == null || state.Runners.Count == 0)
            {
                return;
            }

            var runners = state.Runners
                ?.Where(r => r.StartUtcTicks > 0)
                .OrderBy(r => r.Place)
                .Take(ModConfig.LiveStandingsLimit.Value)
                .ToList();
            if ((runners == null || runners.Count == 0) && !layoutMode)
            {
                return;
            }

            if (runners == null || runners.Count == 0)
            {
                runners = new System.Collections.Generic.List<RunnerSnapshot>
                {
                    new RunnerSnapshot
                    {
                        Place = 1,
                        PlayerName = "Preview",
                        NextCheckpointIndex = 1,
                        FinishTimeMs = 0
                    }
                };
            }

            var localId = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            var lineHeight = 22f;
            var width = 280f;
            var height = 36f + runners.Count * lineHeight;
            var defaultRect = new Rect(24f, Screen.height * 0.22f, width, height);
            if (!HudPanelLayout.Resolve(HudPanelId.Standings, defaultRect, _visible, "Live standings", out var panelRect))
            {
                return;
            }

            var rect = HudPanelLayout.ContentRect(panelRect, _visible);
            var previousDepth = GUI.depth;
            GUI.depth = -997;
            DrawHudPanel(rect, new Color(0f, 0f, 0f, 0.62f));

            var y = rect.y + 10f;
            GUI.Label(new Rect(rect.x + 12f, y, width - 24f, 24f), "Live standings", _standingsTitleStyle);
            y += 26f;

            foreach (var runner in runners)
            {
                GUIStyle style;
                if (runner.Disqualified)
                {
                    style = _standingsDisqualifiedLineStyle;
                }
                else
                {
                    style = runner.PlayerId == localId ? _standingsLocalLineStyle : _standingsLineStyle;
                }

                string suffix;
                if (runner.Disqualified)
                {
                    suffix = "  DQ";
                }
                else if (runner.Finished)
                {
                    suffix = "  FIN";
                }
                else
                {
                    suffix = string.Empty;
                }

                GUI.Label(new Rect(rect.x + 12f, y, width - 24f, lineHeight),
                    $"{TimeFormatter.FormatOrdinal(runner.Place)}  {runner.PlayerName}{suffix}", style);
                y += lineHeight;
            }

            GUI.depth = previousDepth;
        }

        private void DrawRunnerOverlay(RunnerSnapshot runner, bool layoutMode)
        {
            var disqualified = runner != null && runner.Disqualified;
            var elapsedMs = runner != null && runner.StartUtcTicks > 0
                ? runner.Finished || disqualified
                    ? runner.FinishTimeMs
                    : (long)(DateTime.UtcNow - new DateTime(runner.StartUtcTicks, DateTimeKind.Utc)).TotalMilliseconds
                : 0L;
            var width = 400f;
            var totalCheckpoints = RaceNetSync.GetTotalCheckpoints();
            var checkpointLabel = runner != null && runner.StartUtcTicks > 0
                ? FormatCheckpointLabel(runner, totalCheckpoints)
                : "Checkpoints: 0 / 0 completed (preview)";
            var place = runner?.Place > 0 ? runner.Place : 1;
            var parLabel = string.Empty;
            var parDelta = 0L;
            var hasPar = TryFormatPar(runner, elapsedMs, out parLabel, out parDelta);
            var height = 96f;
            if (!string.IsNullOrEmpty(checkpointLabel))
            {
                height += 22f;
            }

            if (hasPar)
            {
                height += 22f;
            }

            var defaultRect = new Rect(Screen.width - width - 28f, Screen.height * 0.56f, width, height);
            if (!HudPanelLayout.Resolve(HudPanelId.Runner, defaultRect, _visible, "Race stats", out var panelRect))
            {
                return;
            }

            var rect = HudPanelLayout.ContentRect(panelRect, _visible);
            var previousDepth = GUI.depth;
            GUI.depth = -998;
            DrawHudPanel(rect, new Color(0f, 0f, 0f, 0.62f));

            var y = rect.y + 12f;
            GUI.Label(new Rect(rect.x + 16f, y, width - 32f, 28f), "RunningMan", _hudTitleStyle);
            y += 30f;
            var placeLabel = disqualified
                ? $"DQ   |   Time {TimeFormatter.FormatDurationMs(elapsedMs)}"
                : $"Place {place}   |   Time {TimeFormatter.FormatDurationMs(elapsedMs)}";
            GUI.Label(new Rect(rect.x + 16f, y, width - 32f, 24f), placeLabel,
                disqualified ? _standingsDisqualifiedLineStyle : _hudLineStyle);
            y += 26f;
            if (!string.IsNullOrEmpty(checkpointLabel))
            {
                GUI.Label(new Rect(rect.x + 16f, y, width - 32f, 22f), checkpointLabel, _hudSubLineStyle);
                y += 24f;
            }

            if (hasPar)
            {
                EnsureStyles();
                var parStyle = parDelta < 0 ? _parAheadStyle : parDelta > 0 ? _parBehindStyle : _hudSubLineStyle;
                GUI.Label(new Rect(rect.x + 16f, y, width - 32f, 22f), parLabel, parStyle);
                y += 24f;
            }

            var status = disqualified
                ? TruncateHud(
                    string.IsNullOrWhiteSpace(runner?.DisqualifiedReason)
                        ? "DISQUALIFIED"
                        : $"DQ: {runner.DisqualifiedReason}",
                    48)
                : runner != null && runner.Finished ? "Race complete!" :
                layoutMode && (runner == null || runner.StartUtcTicks <= 0) ? "Preview panel" : "Keep running!";
            GUI.Label(new Rect(rect.x + 16f, y, width - 32f, 22f), status,
                disqualified ? _standingsDisqualifiedLineStyle : _hudSubLineStyle);
            GUI.depth = previousDepth;
        }

        private static string TruncateHud(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, Math.Max(0, maxChars - 1)) + "…";
        }

        private static bool TryFormatPar(RunnerSnapshot runner, long elapsedMs, out string label, out long deltaMs)
        {
            label = string.Empty;
            deltaMs = 0;
            var state = RaceNetSync.ClientState;
            if (runner == null || state == null || state.ParTotalTimeMs <= 0)
            {
                return false;
            }

            if (runner.Disqualified || runner.StartUtcTicks <= 0)
            {
                return false;
            }

            long wrMs;
            long selfMs;
            string scope;
            if (runner.Finished)
            {
                wrMs = state.ParTotalTimeMs;
                selfMs = runner.FinishTimeMs > 0 ? runner.FinishTimeMs : elapsedMs;
                scope = "finish";
            }
            else
            {
                var completed = Math.Max(0, runner.NextCheckpointIndex - 1);
                if (completed <= 0 ||
                    state.ParCheckpointTimesMs == null ||
                    completed > state.ParCheckpointTimesMs.Count)
                {
                    return false;
                }

                wrMs = state.ParCheckpointTimesMs[completed - 1];
                selfMs = runner.LastCheckpointTimeMs > 0 ? runner.LastCheckpointTimeMs : elapsedMs;
                scope = $"CP{completed}";
            }

            if (wrMs <= 0 || selfMs <= 0)
            {
                return false;
            }

            deltaMs = selfMs - wrMs;
            label = $"WR {scope} {TimeFormatter.FormatSignedDeltaMs(deltaMs)}";
            return true;
        }

        private static void DrawHudPanel(Rect rect, Color color)
        {
            EnsureHudBackground();
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _hudBackground);
            GUI.color = previous;
        }

        private static void EnsureHudBackground()
        {
            if (_hudBackground != null)
            {
                return;
            }

            _hudBackground = new Texture2D(1, 1);
            _hudBackground.SetPixel(0, 0, Color.white);
            _hudBackground.Apply();
        }

        private static string FormatCheckpointLabel(RunnerSnapshot runner, int totalCheckpoints)
        {
            if (runner.Finished)
            {
                return string.Empty;
            }

            if (totalCheckpoints <= 0)
            {
                return "Head to the FINISH line!";
            }

            var completed = Math.Max(0, runner.NextCheckpointIndex - 1);
            if (completed >= totalCheckpoints)
            {
                return "All checkpoints done — head to FINISH!";
            }

            return $"Checkpoints: {completed} / {totalCheckpoints} completed";
        }

        private void DrawRegisteredOverlay(RaceStateSnapshot state, bool layoutMode)
        {
            var registered = state?.Registered;
            var names = new List<string>();
            if (registered != null)
            {
                foreach (var participant in registered)
                {
                    if (!string.IsNullOrWhiteSpace(participant.PlayerName))
                    {
                        names.Add(participant.PlayerName);
                    }
                }
            }

            if (names.Count == 0 && layoutMode)
            {
                names.Add("Preview Runner");
            }

            var lineHeight = 20f;
            var width = 340f;
            var height = 44f + Math.Max(1, names.Count) * lineHeight;
            // Sit above race stats by default so the two panels do not stack identically.
            var defaultRect = new Rect(Screen.width - width - 28f, Screen.height * 0.40f, width, height);
            if (!HudPanelLayout.Resolve(HudPanelId.Registered, defaultRect, _visible, "Registration", out var panelRect))
            {
                return;
            }

            var rect = HudPanelLayout.ContentRect(panelRect, _visible);
            DrawHudPanel(rect, new Color(0f, 0f, 0f, 0.62f));

            var y = rect.y + 10f;
            GUI.Label(new Rect(rect.x + 12f, y, width - 24f, 22f),
                $"Registered ({names.Count}) — waiting to start", _standingsTitleStyle);
            y += 24f;

            if (names.Count == 0)
            {
                GUI.Label(new Rect(rect.x + 12f, y, width - 24f, lineHeight), "Nobody registered yet.", _standingsLineStyle);
            }
            else
            {
                foreach (var name in names)
                {
                    GUI.Label(new Rect(rect.x + 12f, y, width - 24f, lineHeight), $"• {name}", _standingsLineStyle);
                    y += lineHeight;
                }
            }
        }

        private void DrawInfoPanel()
        {
            EnsureStyles();
            var body = _infoPanelBody ?? string.Empty;
            var width = Mathf.Clamp(Screen.width * 0.55f, 520f, 900f);
            var contentStyle = _infoBodyStyle ?? _hudSubLineStyle;
            var titleStyle = _standingsTitleStyle;
            var bodyContent = new GUIContent(body);
            var titleContent = new GUIContent(_infoPanelTitle ?? "RunningMan");
            var titleHeight = titleStyle.CalcHeight(titleContent, width - 40f);
            var bodyHeight = contentStyle.CalcHeight(bodyContent, width - 40f);
            var height = Mathf.Clamp(28f + titleHeight + bodyHeight + 28f, 100f, Screen.height * 0.7f);
            var rect = new Rect(
                Screen.width * 0.5f - width * 0.5f,
                Screen.height * 0.42f - height * 0.5f,
                width,
                height);
            var previousDepth = GUI.depth;
            GUI.depth = -2000;
            DrawHudPanel(rect, new Color(0.08f, 0.05f, 0.02f, 0.94f));
            GUI.Label(new Rect(rect.x + 20f, rect.y + 14f, width - 40f, titleHeight), titleContent, titleStyle);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 18f + titleHeight, width - 40f, bodyHeight), bodyContent,
                contentStyle);
            GUI.depth = previousDepth;
        }

        private void DrawWindow(int id)
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                GUI.FocusControl(null);
                SetVisible(false);
                Event.current.Use();
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            var state = RaceNetSync.ClientState;
            var phase = (RaceEventPhase)(state?.Phase ?? 0);
            var isAdmin = ValheimUtil.IsLocalPlayerAdmin();

            if (isAdmin)
            {
                GUILayout.Label($"Event phase: {FormatPhase(phase)}", _headerStyle);

                if (phase == RaceEventPhase.Countdown && state != null)
                {
                    var remaining = Math.Max(0,
                        (int)Math.Ceiling((new DateTime(state.CountdownEndUtcTicks, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds));
                    GUILayout.Label($"Countdown: {remaining}", _countdownStyle);
                }

                GUILayout.Space(8f);
                DrawPlayerSection(state, phase, includeRefreshStatus: true);
                GUILayout.Space(8f);
                DrawStandings(state);
                GUILayout.Space(8f);
                DrawAdminSection(phase);
                GUILayout.Space(8f);
                DrawTrackSection();
                GUILayout.Space(8f);
                DrawHudPanelsSection();
                GUILayout.Space(8f);
                DrawLogSection();
                GUILayout.Space(8f);
                DrawHelpSection();
            }
            else
            {
                DrawPlayerSection(state, phase, includeRefreshStatus: false);
                GUILayout.Space(8f);
                if (GUILayout.Button("Admin check"))
                {
                    SendCommand("admincheck");
                }

                GUILayout.Space(8f);
                DrawHudPanelsSection();
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private static string FormatPhase(RaceEventPhase phase)
        {
            switch (phase)
            {
                case RaceEventPhase.Ready:
                    return "Ready (waiting to start)";
                case RaceEventPhase.Registration:
                    return "Registration";
                case RaceEventPhase.Countdown:
                    return "Countdown";
                case RaceEventPhase.Racing:
                    return "Racing";
                default:
                    return "Idle";
            }
        }

        private void DrawPlayerSection(RaceStateSnapshot state, RaceEventPhase phase, bool includeRefreshStatus)
        {
            GUILayout.Label("Player", _headerStyle);

            var registered = RaceNetSync.IsLocalRegistered();
            var registrationOpen = state?.RegistrationOpen == true;
            GUILayout.Label(registrationOpen
                ? "Registration is open."
                : "Registration is closed.");
            GUILayout.Label(registered ? "You are registered." : "You are not registered.");

            if (GUILayout.Button("Check my gear"))
            {
                SendCommand("gearcheck");
            }

            GUI.enabled = RaceNetSync.CanJoinRace();
            if (GUILayout.Button("Join race"))
            {
                SendCommand("join");
            }

            GUI.enabled = registered && phase != RaceEventPhase.Racing && phase != RaceEventPhase.Countdown;
            if (GUILayout.Button("Leave race"))
            {
                SendCommand("leave");
            }

            GUI.enabled = true;
            if (includeRefreshStatus && GUILayout.Button("Refresh status"))
            {
                SendCommand("status");
            }
        }

        private void DrawStandings(RaceStateSnapshot state)
        {
            GUILayout.Space(4f);
            GUILayout.Label("Standings", _headerStyle);
            if (state?.Runners == null || state.Runners.Count == 0)
            {
                GUILayout.Label("No active runners.");
                return;
            }

            foreach (var runner in state.Runners)
            {
                var time = TimeFormatter.FormatDurationMs(runner.FinishTimeMs);
                string status;
                if (runner.Disqualified)
                {
                    status = "disqualified";
                }
                else
                {
                    status = runner.Finished ? "finished" : $"CP {runner.NextCheckpointIndex}";
                }

                var line = $"{runner.Place}. {runner.PlayerName} — {time} ({status})";
                if (runner.Disqualified)
                {
                    GUILayout.Label(line, _f6DisqualifiedLabelStyle);
                }
                else
                {
                    GUILayout.Label(line, _labelStyle);
                }
            }

            if (state.Registered != null && state.Registered.Count > 0)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Registered:", _headerStyle);
                var names = new StringBuilder();
                for (var i = 0; i < state.Registered.Count; i++)
                {
                    if (i > 0)
                    {
                        names.Append(", ");
                    }

                    names.Append(state.Registered[i].PlayerName);
                }

                GUILayout.Label(names.ToString());
            }
        }

        private void DrawAdminSection(RaceEventPhase phase)
        {
            GUILayout.Label("Event admin", _headerStyle);

            if (GUILayout.Button("Open registration"))
            {
                SendCommand("open");
            }

            if (GUILayout.Button("Close registration"))
            {
                SendCommand("close");
            }

            var eventActive = phase == RaceEventPhase.Racing || phase == RaceEventPhase.Countdown;
            if (GUILayout.Button(eventActive ? "Cancel event" : "Start countdown"))
            {
                SendCommand(eventActive ? "cancel" : "start");
            }

            if (GUILayout.Button("Show world records"))
            {
                SendCommand("worldrecords");
            }

            if (ValheimUtil.IsLocalPlayerAdmin())
            {
                if (GUILayout.Button("Clear track world records"))
                {
                    SendCommand("clear records");
                }

                if (GUILayout.Button("Clear ALL world records"))
                {
                    SendCommand("clear records all");
                }
            }

            if (GUILayout.Button("Admin check"))
            {
                SendCommand("admincheck");
            }

            DrawAllowedGearSection();
        }

        private void DrawAllowedGearSection()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Allowed gear", _headerStyle);
            EnsureAllowedGearDraft();

            GUILayout.Label("Helmet", _labelStyle);
            _gearHelmetDraft = GUILayout.TextField(_gearHelmetDraft ?? string.Empty);
            GUILayout.Label("Chest", _labelStyle);
            _gearChestDraft = GUILayout.TextField(_gearChestDraft ?? string.Empty);
            GUILayout.Label("Legs", _labelStyle);
            _gearLegsDraft = GUILayout.TextField(_gearLegsDraft ?? string.Empty);
            GUILayout.Label("Cape (empty = no cape)", _labelStyle);
            _gearCapeDraft = GUILayout.TextField(_gearCapeDraft ?? string.Empty);
            GUILayout.Label("Allowed hands (ignored — any weapon/tool OK)", _labelStyle);
            _gearHandsDraft = GUILayout.TextField(_gearHandsDraft ?? string.Empty);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Anti-Sting", _labelStyle, GUILayout.Width(90f));
            _gearAntiStingDraft = GUILayout.TextField(_gearAntiStingDraft ?? "0", GUILayout.Width(48f));
            GUILayout.Label("Ratatosk", _labelStyle, GUILayout.Width(70f));
            _gearRatatoskDraft = GUILayout.TextField(_gearRatatoskDraft ?? "0", GUILayout.Width(48f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Salad", _labelStyle, GUILayout.Width(90f));
            _gearSaladDraft = GUILayout.TextField(_gearSaladDraft ?? "0", GUILayout.Width(48f));
            GUILayout.Label("BloodPud", _labelStyle, GUILayout.Width(70f));
            _gearBloodPuddingDraft = GUILayout.TextField(_gearBloodPuddingDraft ?? "0", GUILayout.Width(48f));
            GUILayout.Label("Omelette", _labelStyle, GUILayout.Width(60f));
            _gearOmeletteDraft = GUILayout.TextField(_gearOmeletteDraft ?? "0", GUILayout.Width(48f));
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Use my loadout"))
            {
                ApplyLocalLoadoutToDraft();
            }

            if (GUILayout.Button("Save allowed gear"))
            {
                SendCommand(BuildGearSetSaveCommand());
            }

            if (GUILayout.Button("Reset allowed gear to defaults"))
            {
                SendCommand("gearset reset");
                _allowedGearDraftReady = false;
            }
        }

        private void EnsureAllowedGearDraft()
        {
            if (_allowedGearDraftReady)
            {
                return;
            }

            var rules = GearValidator.GetActiveRules();
            ApplyRulesToDraft(rules);
            _allowedGearDraftReady = true;
        }

        private void ApplyRulesToDraft(AllowedGearRules rules)
        {
            rules ??= AllowedGearRules.CreateDefaults();
            _gearHelmetDraft = rules.Helmet ?? string.Empty;
            _gearChestDraft = rules.Chest ?? string.Empty;
            _gearLegsDraft = rules.Legs ?? string.Empty;
            _gearCapeDraft = rules.Cape ?? string.Empty;
            _gearHandsDraft = rules.AllowedHandItems ?? string.Empty;
            _gearAntiStingDraft = rules.RequiredAntiSting.ToString();
            _gearRatatoskDraft = rules.RequiredRatatosk.ToString();
            _gearSaladDraft = rules.RequiredSalad.ToString();
            _gearBloodPuddingDraft = rules.RequiredBloodPudding.ToString();
            _gearOmeletteDraft = rules.RequiredMushroomOmelette.ToString();
        }

        private void ApplyLocalLoadoutToDraft()
        {
            var captured = GearValidator.CaptureFromPlayer(Player.m_localPlayer);
            ApplyRulesToDraft(captured);
            RaceGuiLog.Add("Allowed gear draft filled from your current loadout.");
        }

        private string BuildGearSetSaveCommand()
        {
            var rules = GearValidator.GetActiveRules().Clone();
            rules.Helmet = SanitizeGearField(_gearHelmetDraft);
            rules.Chest = SanitizeGearField(_gearChestDraft);
            rules.Legs = SanitizeGearField(_gearLegsDraft);
            rules.Cape = SanitizeGearField(_gearCapeDraft);
            rules.AllowedHandItems = SanitizeGearField(_gearHandsDraft);
            if (!int.TryParse(_gearAntiStingDraft, out var anti))
            {
                anti = rules.RequiredAntiSting;
            }

            if (!int.TryParse(_gearRatatoskDraft, out var ratatosk))
            {
                ratatosk = rules.RequiredRatatosk;
            }

            if (!int.TryParse(_gearSaladDraft, out var salad))
            {
                salad = rules.RequiredSalad;
            }

            if (!int.TryParse(_gearBloodPuddingDraft, out var blood))
            {
                blood = rules.RequiredBloodPudding;
            }

            if (!int.TryParse(_gearOmeletteDraft, out var omelette))
            {
                omelette = rules.RequiredMushroomOmelette;
            }

            rules.RequiredAntiSting = Math.Max(0, anti);
            rules.RequiredRatatosk = Math.Max(0, ratatosk);
            rules.RequiredSalad = Math.Max(0, salad);
            rules.RequiredBloodPudding = Math.Max(0, blood);
            rules.RequiredMushroomOmelette = Math.Max(0, omelette);
            return "gearset save " + EncodeGearSetPayload(rules);
        }

        private static string SanitizeGearField(string value)
        {
            return (value ?? string.Empty).Replace("§", string.Empty).Trim();
        }

        internal static string EncodeGearSetPayload(AllowedGearRules rules)
        {
            rules ??= AllowedGearRules.CreateDefaults();
            // Legacy frost-mead slots kept empty/"0" for payload compatibility.
            return string.Join("§",
                SanitizeGearField(rules.Helmet),
                SanitizeGearField(rules.Chest),
                SanitizeGearField(rules.Legs),
                SanitizeGearField(rules.Cape),
                SanitizeGearField(rules.AllowedHandItems),
                SanitizeGearField(rules.AntiStingPrefab),
                SanitizeGearField(rules.RatatoskPrefab),
                string.Empty,
                Math.Max(0, rules.RequiredAntiSting).ToString(),
                Math.Max(0, rules.RequiredRatatosk).ToString(),
                "0",
                SanitizeGearField(rules.SaladPrefab),
                SanitizeGearField(rules.BloodPuddingPrefab),
                SanitizeGearField(rules.MushroomOmelettePrefab),
                Math.Max(0, rules.RequiredSalad).ToString(),
                Math.Max(0, rules.RequiredBloodPudding).ToString(),
                Math.Max(0, rules.RequiredMushroomOmelette).ToString());
        }

        private void DrawTrackSection()
        {
            GUILayout.Label("Track setup", _headerStyle);

            var state = RaceNetSync.ClientState;
            var currentName = state?.TrackName;
            if (string.IsNullOrWhiteSpace(currentName))
            {
                currentName = TrackIdentity.GetDisplayName(RaceNetSync.GetActiveTrack());
            }

            GUILayout.Label($"Track: {currentName}", _labelStyle);
            if (string.IsNullOrEmpty(_trackNameDraft))
            {
                _trackNameDraft = currentName ?? string.Empty;
            }

            _trackNameDraft = GUILayout.TextField(_trackNameDraft);
            if (GUILayout.Button("Save track name"))
            {
                SendCommand($"trackname {_trackNameDraft}");
            }

            var debugOn = RaceNetSync.IsDebugModeActive();
            if (GUILayout.Button(debugOn ? "Debug markers: ON" : "Debug markers: OFF"))
            {
                SendCommand(debugOn ? "debug off" : "debug on");
            }

            GUILayout.Space(4f);
            GUILayout.Label("Move endpoints: equip Hammer, look at A/B dots, Use (E).", _labelStyle);
            if (GUILayout.Button(RaceGateEditor.EditMode ? "Endpoint edit: ON" : "Endpoint edit: OFF"))
            {
                RaceGateEditor.ToggleEditMode();
            }

            GUILayout.Space(4f);
            GUILayout.Label("Bulletins: place Hammer Signs, look at them, then mark.", _labelStyle);
            if (GUILayout.Button("Mark WR Sign (look at Sign)"))
            {
                SendCommand("wrboard add");
            }

            if (GUILayout.Button("Mark RULES Sign (look at Sign)"))
            {
                SendCommand("rulesboard add");
            }

            if (GUILayout.Button("Remove nearest WR Sign"))
            {
                SendCommand("wrboard remove");
            }

            if (GUILayout.Button("Remove nearest RULES Sign"))
            {
                SendCommand("rulesboard remove");
            }

            GUILayout.Space(4f);
            GUILayout.Label("Register gates (stand at gate, face forward):", _labelStyle);
            if (GUILayout.Button("Register START here"))
            {
                SendCommand("register start");
            }

            if (GUILayout.Button("Register FINISH here"))
            {
                SendCommand("register finish");
            }

            if (GUILayout.Button("Register CHECKPOINT here"))
            {
                SendCommand("register checkpoint");
            }

            GUILayout.Label($"Hotkey: {ModConfig.RegisterCheckpointHotkey.Value} — inserts by track order (between neighbors)", _labelStyle);

            GUILayout.Space(4f);
            GUILayout.Label("Remove gates:", _labelStyle);
            if (GUILayout.Button("Remove last checkpoint"))
            {
                SendCommand("remove checkpoint last");
            }

            if (GUILayout.Button("Remove nearest checkpoint"))
            {
                SendCommand("remove checkpoint nearest");
            }

            if (GUILayout.Button("Clear all checkpoints"))
            {
                SendCommand("clear checkpoints");
            }

            if (GUILayout.Button("Clear start gate"))
            {
                SendCommand("clear start");
            }

            if (GUILayout.Button("Clear finish gate"))
            {
                SendCommand("clear finish");
            }

            GUILayout.Space(4f);
            GUILayout.Label("Register with custom width: /run register checkpoint 10", _labelStyle);
            GUILayout.Space(4f);
            GUILayout.Label("Auto-detect nearby gates:", _labelStyle);
            if (GUILayout.Button("Auto-detect START"))
            {
                SendCommand("autodetect start");
            }

            if (GUILayout.Button("Auto-detect FINISH"))
            {
                SendCommand("autodetect finish");
            }

            if (GUILayout.Button("Auto-detect CHECKPOINTS"))
            {
                SendCommand("autodetect checkpoints");
            }

            if (GUILayout.Button("Auto-detect 1 checkpoint here"))
            {
                SendCommand("autodetect checkpoint");
            }

            GUILayout.Label($"Hotkey: {ModConfig.AutoDetectNearestCheckpointHotkey.Value}", _labelStyle);
        }

        private void DrawLogSection()
        {
            GUILayout.Label("Command log", _headerStyle);
            if (GUILayout.Button("Clear log"))
            {
                RaceGuiLog.Clear();
            }

            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.Height(120f));
            if (RaceGuiLog.Lines.Count == 0)
            {
                GUILayout.Label("Button and /run command replies appear here.", _labelStyle);
            }
            else
            {
                foreach (var line in RaceGuiLog.Lines)
                {
                    GUILayout.Label(line, _labelStyle);
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawHudPanelsSection()
        {
            GUILayout.Label("HUD panels", _headerStyle);
            GUILayout.Label("Toggle overlays or use X on a panel title bar while F6 is open.", _labelStyle);

            foreach (var panelId in HudPanelLayout.ToggleablePanels())
            {
                var visible = HudPanelLayout.IsPanelVisible(panelId);
                var next = GUILayout.Toggle(visible, HudPanelLayout.GetPanelTitle(panelId));
                if (next != visible)
                {
                    HudPanelLayout.SetPanelVisible(panelId, next);
                }
            }

            if (GUILayout.Button("Show all HUD panels"))
            {
                HudPanelLayout.ShowAllPanels();
            }

            if (GUILayout.Button("Reset HUD positions"))
            {
                HudPanelLayout.ResetAll();
            }
        }

        private void DrawHelpSection()
        {
            EnsureStyles();
            GUILayout.Label("Player: /run status | join | leave | gearcheck | pb | last", _labelStyle);
            GUILayout.Label("Admin: register, autodetect, debug, worldrecords, clear records (server validates admin)", _labelStyle);
            GUILayout.Space(4f);
            GUILayout.Label("HUD: open F6, drag title bars to move overlays, X to hide, re-enable above.", _labelStyle);
        }

        private static void SendCommand(string args)
        {
            RaceGuiLog.Add($"> run {args}");
            ApplyOptimisticCommand(args);
            ValheimUtil.RunCommand(args);
        }

        private static void HandleTrackHotkeys()
        {
            if (IsTextInputBlockingHotkeys())
            {
                return;
            }

            var registerKey = ModConfig.RegisterCheckpointHotkey.Value;
            if (registerKey != KeyCode.None && Input.GetKeyDown(registerKey))
            {
                SendCommand("register checkpoint");
                return;
            }

            var autoKey = ModConfig.AutoDetectNearestCheckpointHotkey.Value;
            if (autoKey != KeyCode.None && Input.GetKeyDown(autoKey))
            {
                SendCommand("autodetect checkpoint");
            }
        }

        private static bool IsTextInputBlockingHotkeys()
        {
            if (Console.IsVisible())
            {
                return true;
            }

            try
            {
                if (Chat.instance != null && Chat.instance.HasFocus())
                {
                    return true;
                }
            }
            catch
            {
                // Ignore API differences.
            }

            try
            {
                if (TextInput.IsVisible())
                {
                    return true;
                }
            }
            catch
            {
                // Ignore API differences.
            }

            if (IsOpen && GUIUtility.keyboardControl != 0)
            {
                return true;
            }

            return false;
        }

        private static void ApplyOptimisticCommand(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
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
                case "open":
                    RaceNetSync.ApplyOptimisticRegistrationOpened();
                    break;
                case "close":
                    RaceNetSync.ApplyOptimisticRegistrationClosed();
                    break;
                case "start":
                    // Wait for server confirmation — do not play countdown until StartCountdown succeeds.
                    break;
                case "cancel":
                    RaceNetSync.ApplyOptimisticCancel();
                    if (_instance != null)
                    {
                        _instance._finishBannerUntil = 0f;
                        _instance._localWasFinished = false;
                    }
                    break;
                case "clear":
                case "remove":
                    RaceNetSync.ApplyOptimisticTrackCommand(parts);
                    break;
                case "debug":
                    ApplyOptimisticDebug(parts);
                    break;
            }
        }

        private static void ApplyOptimisticDebug(string[] parts)
        {
            if (parts.Length < 2)
            {
                return;
            }

            switch (parts[1].ToLowerInvariant())
            {
                case "on":
                case "true":
                case "1":
                    RaceNetSync.ApplyOptimisticDebug(true);
                    break;
                case "off":
                case "false":
                case "0":
                    RaceNetSync.ApplyOptimisticDebug(false);
                    break;
            }
        }

        private void EnsureStyles()
        {
            if (_headerStyle != null)
            {
                return;
            }

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(4, 4, 4, 4),
                clipping = TextClipping.Overflow,
                normal = { textColor = Color.white }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                clipping = TextClipping.Overflow,
                normal = { textColor = Color.white },
                wordWrap = true
            };

            _countdownStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ModConfig.CountdownFontSize.Value,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                normal = { textColor = Color.yellow }
            };

            _checkpointStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ModConfig.CheckpointHudFontSize.Value,
                clipping = TextClipping.Overflow,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };

            _hudTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 4, 0),
                normal = { textColor = Color.white }
            };

            _hudLineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Overflow,
                normal = { textColor = Color.white }
            };

            _hudSubLineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 2, 0),
                normal = { textColor = new Color(0.92f, 0.92f, 0.92f) }
            };

            _infoBodyStyle = new GUIStyle(_hudSubLineStyle)
            {
                fontSize = 16,
                wordWrap = true,
                clipping = TextClipping.Clip,
                richText = false
            };

            _finishBannerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                normal = { textColor = Color.green }
            };

            _finishPlaceStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ModConfig.CountdownFontSize.Value + 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                normal = { textColor = new Color(1f, 0.92f, 0.2f) }
            };

            _finishSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                normal = { textColor = Color.white }
            };

            _standingsTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Overflow,
                normal = { textColor = Color.white }
            };

            _standingsLineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Overflow,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };

            _standingsLocalLineStyle = new GUIStyle(_standingsLineStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.55f, 0.95f, 1f) }
            };

            _standingsDisqualifiedLineStyle = new GUIStyle(_standingsLineStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.35f, 0.35f) }
            };

            _f6DisqualifiedLabelStyle = new GUIStyle(_labelStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.35f, 0.35f) }
            };

            _parAheadStyle = new GUIStyle(_hudSubLineStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.45f, 0.95f, 0.5f) }
            };

            _parBehindStyle = new GUIStyle(_hudSubLineStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.55f, 0.35f) }
            };
        }
    }
}
