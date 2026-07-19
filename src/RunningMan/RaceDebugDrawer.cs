using RunningMan.Net;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Draws gate markers in screen space when debug mode is enabled.
    /// LineRenderers are unreliable in Valheim, so this uses OnGUI instead.
    /// </summary>
    public sealed class RaceDebugDrawer : MonoBehaviour
    {
        private static Texture2D _lineTexture;
        private static GUIStyle _labelStyle;
        private static readonly Color CheckpointColor = new Color(1f, 0.92f, 0.16f);

        private void OnEnable()
        {
            RaceNetSync.TrackUpdated += MarkDirty;
            RaceNetSync.StateUpdated += MarkDirty;
        }

        private void OnDisable()
        {
            RaceNetSync.TrackUpdated -= MarkDirty;
            RaceNetSync.StateUpdated -= MarkDirty;
        }

        private static void MarkDirty()
        {
        }

        private void OnGUI()
        {
            if (!RaceNetSync.IsDebugModeActive())
            {
                return;
            }

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var track = GetActiveTrack();
            if (track == null)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            if (track.HasStart)
            {
                DrawGate(camera, track.StartGate, Color.green, "START", RaceManager.GateKind.Start, 0);
                DrawStartingArea(camera, track.StartGate);
            }

            if (track.HasFinish)
            {
                DrawGate(camera, track.FinishGate, Color.red, "FINISH", RaceManager.GateKind.Finish, 0);
            }

            if (track.Checkpoints != null)
            {
                for (var i = 0; i < track.Checkpoints.Count; i++)
                {
                    var checkpoint = track.Checkpoints[i];
                    DrawGate(camera, checkpoint.Gate, CheckpointColor, $"CP{checkpoint.Index}",
                        RaceManager.GateKind.Checkpoint, checkpoint.Index);
                }
            }

            DrawRacePath(camera, track);
        }

        private static void DrawRacePath(Camera camera, TrackConfig track)
        {
            var path = TrackPath.Build(track);
            if (path.Points.Count < 2)
            {
                return;
            }

            var color = new Color(1f, 0.55f, 0.15f, 0.9f);
            for (var i = 0; i < path.Points.Count - 1; i++)
            {
                var a = path.Points[i] + Vector3.up * 2f;
                var b = path.Points[i + 1] + Vector3.up * 2f;
                DrawWorldLine(camera, a, b, color, 3f);
            }

            DrawWorldLabel(camera, path.Points[path.Points.Count / 2] + Vector3.up * 4f,
                $"PATH {path.TotalLength:0}m", color);
        }

        private static TrackConfig GetActiveTrack()
        {
            return RaceNetSync.GetActiveTrack();
        }

        private static void DrawGate(Camera camera, RaceGate gate, Color color, string label,
            RaceManager.GateKind kind, int checkpointIndex)
        {
            if (gate == null || !gate.IsConfigured())
            {
                return;
            }

            var pointA = gate.GetPointA() + Vector3.up * 1.5f;
            var pointB = gate.GetPointB() + Vector3.up * 1.5f;
            var midpoint = gate.GetMidpoint() + Vector3.up * 3f;

            DrawWorldLine(camera, pointA, pointB, color, RaceGateEditor.EditMode ? 7f : 5f);
            DrawWorldLabel(camera, midpoint, label, color);
            DrawEndpointDot(camera, pointA, color, kind, checkpointIndex, pointA: true);
            DrawEndpointDot(camera, pointB, color, kind, checkpointIndex, pointA: false);
        }

        private static void DrawEndpointDot(Camera camera, Vector3 world, Color color,
            RaceManager.GateKind kind, int checkpointIndex, bool pointA)
        {
            var size = RaceGateEditor.EditMode ? 12f : 8f;
            var drawColor = color;

            if (RaceGateEditor.TryGetCarried(out var carried) &&
                carried.Kind == kind &&
                carried.CheckpointIndex == checkpointIndex &&
                carried.PointA == pointA)
            {
                size = 22f;
                drawColor = Color.white;
                world = carried.Position + Vector3.up * 1.5f;
            }
            else if (RaceGateEditor.TryGetHighlight(out var highlight) &&
                     highlight.Kind == kind &&
                     highlight.CheckpointIndex == checkpointIndex &&
                     highlight.PointA == pointA)
            {
                size = 18f;
                drawColor = Color.cyan;
            }

            DrawWorldDot(camera, world, drawColor, size);
        }

        private static void DrawStartingArea(Camera camera, RaceGate startGate)
        {
            TriggerDetector.GetStartingAreaCorners(
                startGate,
                ModConfig.StartingAreaOffset.Value,
                ModConfig.StartingAreaDepth.Value,
                ModConfig.StartingAreaSidePadding.Value,
                out var c0, out var c1, out var c2, out var c3);

            var color = new Color(0.25f, 0.85f, 1f, 0.95f);
            DrawWorldLine(camera, c0, c1, color, 4f);
            DrawWorldLine(camera, c1, c2, color, 4f);
            DrawWorldLine(camera, c2, c3, color, 4f);
            DrawWorldLine(camera, c3, c0, color, 4f);
            DrawWorldLabel(camera, (c0 + c1 + c2 + c3) * 0.25f + Vector3.up * 1.5f, "START GRID", color);
        }

        private static void DrawWorldLine(Camera camera, Vector3 worldA, Vector3 worldB, Color color, float thickness)
        {
            var screenA = camera.WorldToScreenPoint(worldA);
            var screenB = camera.WorldToScreenPoint(worldB);
            if (screenA.z <= 0f || screenB.z <= 0f)
            {
                return;
            }

            screenA.y = Screen.height - screenA.y;
            screenB.y = Screen.height - screenB.y;
            DrawScreenLine(screenA, screenB, color, thickness);
        }

        private static void DrawWorldDot(Camera camera, Vector3 worldPoint, Color color, float size)
        {
            var screen = camera.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0f)
            {
                return;
            }

            screen.y = Screen.height - screen.y;
            var rect = new Rect(screen.x - size * 0.5f, screen.y - size * 0.5f, size, size);
            EnsureLineTexture();
            GUI.color = color;
            GUI.DrawTexture(rect, _lineTexture);
            GUI.color = Color.white;
        }

        private static void DrawWorldLabel(Camera camera, Vector3 worldPoint, string label, Color color)
        {
            var screen = camera.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0f)
            {
                return;
            }

            screen.y = Screen.height - screen.y;
            EnsureLabelStyle(color);

            var size = _labelStyle.CalcSize(new GUIContent(label));
            const float padding = 6f;
            var rect = new Rect(
                screen.x - size.x * 0.5f - padding,
                screen.y - size.y - padding,
                size.x + padding * 2f,
                size.y + padding * 2f);
            GUI.Label(rect, label, _labelStyle);
        }

        private static void EnsureLabelStyle(Color color)
        {
            if (_labelStyle != null && _labelStyle.normal.textColor == color)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerCenter,
                clipping = TextClipping.Overflow,
                wordWrap = false,
                normal = { textColor = color }
            };
        }

        private static void DrawScreenLine(Vector3 start, Vector3 end, Color color, float width)
        {
            EnsureLineTexture();
            GUI.color = color;
            var delta = end - start;
            var length = delta.magnitude;
            if (length < 0.01f)
            {
                GUI.color = Color.white;
                return;
            }

            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.DrawTexture(new Rect(start.x, start.y, length, width), _lineTexture);
            GUI.matrix = matrix;
            GUI.color = Color.white;
        }

        private static void EnsureLineTexture()
        {
            if (_lineTexture != null)
            {
                return;
            }

            _lineTexture = new Texture2D(1, 1);
            _lineTexture.SetPixel(0, 0, Color.white);
            _lineTexture.Apply();
        }
    }
}
