using System.Collections.Generic;
using UnityEngine;

namespace RunningMan
{
    internal enum HudPanelId
    {
        Runner = 0,
        Standings = 1,
        /// <summary>Shared center banner for countdown and finish place.</summary>
        RaceBanner = 2,
        /// <summary>Deprecated — aliased to RaceBanner for saved prefs.</summary>
        Finish = 3,
        Registered = 4,
        /// <summary>Unused — kept so PlayerPrefs panel indices stay stable.</summary>
        WorldRecords = 5
    }

    /// <summary>
    /// Persists draggable HUD overlay positions and per-panel visibility.
    /// </summary>
    internal static class HudPanelLayout
    {
        private const float DragTitleHeight = 20f;
        private const int PanelCount = 6;
        private static readonly Rect[] Positions = new Rect[PanelCount];
        private static readonly bool[] HasPosition = new bool[PanelCount];
        private static readonly bool[] PanelVisible = new bool[PanelCount];
        private static int _draggingId = -1;
        private static GUIStyle _dragTitleStyle;
        private static GUIStyle _closeButtonStyle;
        private static bool _loaded;

        public static float TitleBarHeight => DragTitleHeight;

        public static HudPanelId Normalize(HudPanelId id)
        {
            return id == HudPanelId.Finish ? HudPanelId.RaceBanner : id;
        }

        public static bool ShouldDrawPanel(HudPanelId id, bool layoutMode = false)
        {
            _ = layoutMode;
            EnsureLoaded();
            return PanelVisible[(int)Normalize(id)];
        }

        public static bool IsPanelVisible(HudPanelId id)
        {
            EnsureLoaded();
            return PanelVisible[(int)Normalize(id)];
        }

        public static void SetPanelVisible(HudPanelId id, bool visible)
        {
            EnsureLoaded();
            var index = (int)Normalize(id);
            PanelVisible[index] = visible;
            PlayerPrefs.SetInt(PrefKey(index, "Visible"), visible ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void ShowAllPanels()
        {
            for (var i = 0; i < PanelCount; i++)
            {
                if ((HudPanelId)i == HudPanelId.Finish)
                {
                    continue;
                }

                SetPanelVisible((HudPanelId)i, true);
            }
        }

        public static string GetPanelTitle(HudPanelId id)
        {
            switch (Normalize(id))
            {
                case HudPanelId.Runner:
                    return "Race stats";
                case HudPanelId.Standings:
                    return "Live standings";
                case HudPanelId.RaceBanner:
                    return "Race banner";
                case HudPanelId.Registered:
                    return "Registration";
                default:
                    return id.ToString();
            }
        }

        public static IEnumerable<HudPanelId> ToggleablePanels()
        {
            yield return HudPanelId.Runner;
            yield return HudPanelId.Standings;
            yield return HudPanelId.RaceBanner;
            yield return HudPanelId.Registered;
        }

        /// <summary>
        /// Returns false when the panel was hidden via the title-bar close button.
        /// </summary>
        public static bool Resolve(HudPanelId id, Rect defaultRect, bool draggable, string title, out Rect panelRect)
        {
            EnsureLoaded();
            id = Normalize(id);
            var index = (int)id;
            var rect = HasPosition[index] ? Positions[index] : defaultRect;
            rect.width = defaultRect.width;
            rect.height = defaultRect.height + (draggable ? DragTitleHeight : 0f);

            if (!draggable)
            {
                Positions[index] = rect;
                panelRect = rect;
                return true;
            }

            EnsureDragStyle();
            var titleBar = new Rect(rect.x, rect.y, rect.width, DragTitleHeight);
            var closeWidth = 22f;
            var labelRect = new Rect(titleBar.x + 8f, titleBar.y, titleBar.width - closeWidth - 10f, titleBar.height);
            GUI.Label(labelRect, title + "  (drag)", _dragTitleStyle);

            var closeRect = new Rect(titleBar.xMax - closeWidth - 2f, titleBar.y + 1f, closeWidth, titleBar.height - 2f);
            if (GUI.Button(closeRect, "X", _closeButtonStyle))
            {
                SetPanelVisible(id, false);
                panelRect = rect;
                return false;
            }

            var currentEvent = Event.current;
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (titleBar.Contains(currentEvent.mousePosition) && !closeRect.Contains(currentEvent.mousePosition))
                    {
                        _draggingId = index;
                        currentEvent.Use();
                    }

                    break;
                case EventType.MouseDrag:
                    if (_draggingId == index)
                    {
                        rect.x += currentEvent.delta.x;
                        rect.y += currentEvent.delta.y;
                        currentEvent.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (_draggingId == index)
                    {
                        _draggingId = -1;
                        SavePosition(index, rect);
                        currentEvent.Use();
                    }

                    break;
            }

            Positions[index] = rect;
            HasPosition[index] = true;
            panelRect = rect;
            return true;
        }

        public static Rect ContentRect(Rect panelRect, bool draggable)
        {
            if (!draggable)
            {
                return panelRect;
            }

            return new Rect(panelRect.x, panelRect.y + DragTitleHeight, panelRect.width,
                panelRect.height - DragTitleHeight);
        }

        public static void ResetAll()
        {
            for (var i = 0; i < HasPosition.Length; i++)
            {
                HasPosition[i] = false;
                PanelVisible[i] = true;
                PlayerPrefs.DeleteKey(PrefKey(i, "X"));
                PlayerPrefs.DeleteKey(PrefKey(i, "Y"));
                PlayerPrefs.DeleteKey(PrefKey(i, "Visible"));
            }

            PlayerPrefs.Save();
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            for (var i = 0; i < HasPosition.Length; i++)
            {
                PanelVisible[i] = !PlayerPrefs.HasKey(PrefKey(i, "Visible")) || PlayerPrefs.GetInt(PrefKey(i, "Visible")) == 1;
                if (!PlayerPrefs.HasKey(PrefKey(i, "X")))
                {
                    continue;
                }

                Positions[i] = new Rect(
                    PlayerPrefs.GetFloat(PrefKey(i, "X")),
                    PlayerPrefs.GetFloat(PrefKey(i, "Y")),
                    0f,
                    0f);
                HasPosition[i] = true;
            }
        }

        private static void SavePosition(int index, Rect rect)
        {
            PlayerPrefs.SetFloat(PrefKey(index, "X"), rect.x);
            PlayerPrefs.SetFloat(PrefKey(index, "Y"), rect.y);
            PlayerPrefs.Save();
        }

        private static string PrefKey(int index, string suffix)
        {
            return $"RunningMan.Hud.{index}.{suffix}";
        }

        private static void EnsureDragStyle()
        {
            if (_dragTitleStyle != null)
            {
                return;
            }

            _dragTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 2, 2),
                normal = { textColor = new Color(0.75f, 0.85f, 1f) }
            };

            _closeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }
    }
}
