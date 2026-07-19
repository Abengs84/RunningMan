using System;
using RunningMan.Net;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Interactive gate endpoint editor: look at an endpoint with the hammer, Use to pick up / place.
    /// </summary>
    public sealed class RaceGateEditor : MonoBehaviour
    {
        public static RaceGateEditor Instance { get; private set; }
        public static bool EditMode { get; private set; }

        public struct EndpointRef
        {
            public RaceManager.GateKind Kind;
            public int CheckpointIndex;
            public bool PointA;
            public Vector3 Position;
            public string Label;
        }

        private EndpointRef? _carrying;
        private EndpointRef? _highlight;
        private string _status = string.Empty;
        private GUIStyle _hudStyle;

        public static void SetEditMode(bool enabled)
        {
            EditMode = enabled;
            if (Instance != null)
            {
                Instance._carrying = null;
                Instance._highlight = null;
                Instance._status = enabled
                    ? "Gate edit ON — equip Hammer, look at A/B dots, Use (E) to pick up / place."
                    : string.Empty;
            }

            if (enabled && !RaceNetSync.IsDebugModeActive())
            {
                ValheimUtil.RunCommand("debug on");
            }
        }

        public static void ToggleEditMode()
        {
            SetEditMode(!EditMode);
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!EditMode || Player.m_localPlayer == null)
            {
                _highlight = null;
                return;
            }

            if (_carrying.HasValue)
            {
                UpdateCarriedEndpoint();
                if (WasUsePressed() && !RaceGui.IsOpen)
                {
                    PlaceCarriedEndpoint();
                }

                return;
            }

            // Don't steal Use while the F6 panel is open.
            if (RaceGui.IsOpen)
            {
                _highlight = null;
                return;
            }

            _highlight = FindLookedEndpoint();
            if (_highlight.HasValue && IsHoldingHammer() && WasUsePressed())
            {
                _carrying = _highlight;
                _status = $"Picked up {_highlight.Value.Label}. Move, then Use (E) to place.";
            }
        }

        private void OnGUI()
        {
            if (!EditMode || string.IsNullOrEmpty(_status))
            {
                return;
            }

            EnsureHudStyle();
            var width = 560f;
            var rect = new Rect(Screen.width * 0.5f - width * 0.5f, 24f, width, 36f);
            GUI.Label(rect, _status, _hudStyle);
        }

        public static bool TryGetHighlight(out EndpointRef endpoint)
        {
            if (Instance?._highlight != null)
            {
                endpoint = Instance._highlight.Value;
                return true;
            }

            endpoint = default;
            return false;
        }

        public static bool TryGetCarried(out EndpointRef endpoint)
        {
            if (Instance?._carrying != null)
            {
                endpoint = Instance._carrying.Value;
                return true;
            }

            endpoint = default;
            return false;
        }

        public static Vector3 GetPlacePosition()
        {
            var player = Player.m_localPlayer;
            var camera = Camera.main;
            if (player == null)
            {
                return Vector3.zero;
            }

            var lookDirection = camera != null ? camera.transform.forward : player.transform.forward;

            // 1) Snap to nearby Standing Iron Torches (and related pieces) under the crosshair.
            if (TriggerDetector.TryFindLookSnapTarget(
                    player.transform.position,
                    lookDirection,
                    ModConfig.EndpointSnapRadius.Value,
                    ModConfig.EndpointSnapAngle.Value,
                    out var snapPoint,
                    out _))
            {
                return snapPoint;
            }

            // 2) Raycast to a Piece root (true object snap).
            if (TryGetLookPiece(out var piecePoint))
            {
                return piecePoint;
            }

            // 3) Project look onto ground (avoids floating endpoints high in trees/sky).
            if (TryGetLookGroundPoint(out var groundPoint))
            {
                return groundPoint;
            }

            return player.transform.position;
        }

        private static bool TryGetLookPiece(out Vector3 point)
        {
            point = Vector3.zero;
            var camera = Camera.main;
            var player = Player.m_localPlayer;
            if (camera == null || player == null)
            {
                return false;
            }

            var ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            var hits = Physics.RaycastAll(ray, 40f);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.collider.transform.IsChildOf(player.transform) ||
                    hit.collider.GetComponentInParent<Player>() == player)
                {
                    continue;
                }

                var piece = hit.collider.GetComponentInParent<Piece>();
                if (piece != null)
                {
                    point = piece.transform.position;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetLookGroundPoint(out Vector3 point)
        {
            point = Vector3.zero;
            var camera = Camera.main;
            var player = Player.m_localPlayer;
            if (camera == null || player == null)
            {
                return false;
            }

            var ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            var hits = Physics.RaycastAll(ray, 50f);
            if (hits != null && hits.Length > 0)
            {
                System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
                foreach (var hit in hits)
                {
                    if (hit.collider == null)
                    {
                        continue;
                    }

                    if (hit.collider.transform.IsChildOf(player.transform) ||
                        hit.collider.GetComponentInParent<Player>() == player)
                    {
                        continue;
                    }

                    // Tree canopies / tall props put endpoints in the sky — use ground under the hit.
                    point = SnapToGround(hit.point, player.transform.position.y);
                    return true;
                }
            }

            // Looking into open air: intersect a horizontal plane at the player's feet, then ground-snap.
            var planeY = player.transform.position.y;
            var direction = ray.direction;
            if (Mathf.Abs(direction.y) < 0.001f)
            {
                direction.y = -0.001f;
            }

            var t = (planeY - ray.origin.y) / direction.y;
            if (t < 0.5f || t > 45f)
            {
                var flat = Flatten(camera.transform.forward);
                point = SnapToGround(player.transform.position + flat * 6f, planeY);
                return true;
            }

            point = SnapToGround(ray.origin + direction * t, planeY);
            return true;
        }

        private static Vector3 SnapToGround(Vector3 approximate, float fallbackY)
        {
            if (ZoneSystem.instance != null &&
                ZoneSystem.instance.GetGroundHeight(approximate, out var groundY))
            {
                return new Vector3(approximate.x, groundY, approximate.z);
            }

            return new Vector3(approximate.x, fallbackY, approximate.z);
        }

        private static Vector3 Flatten(Vector3 vector)
        {
            vector.y = 0f;
            if (vector.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return vector.normalized;
        }

        private void UpdateCarriedEndpoint()
        {
            if (!_carrying.HasValue)
            {
                return;
            }

            var carried = _carrying.Value;
            var position = GetPlacePosition();
            carried.Position = position;
            _carrying = carried;
            ApplyOptimisticPreview(carried);

            var player = Player.m_localPlayer;
            var camera = Camera.main;
            if (player != null &&
                TriggerDetector.TryFindLookSnapTarget(
                    player.transform.position,
                    camera != null ? camera.transform.forward : player.transform.forward,
                    ModConfig.EndpointSnapRadius.Value,
                    ModConfig.EndpointSnapAngle.Value,
                    out _,
                    out var hint))
            {
                _status = $"Carrying {carried.Label} — snapping to {hint}. Use (E) to place.";
            }
            else
            {
                _status = $"Carrying {carried.Label} — ground place. Aim at a torch to snap. Use (E) to place.";
            }
        }

        public static Vector3? PendingPlacePosition { get; set; }

        private void PlaceCarriedEndpoint()
        {
            if (!_carrying.HasValue)
            {
                return;
            }

            var carried = _carrying.Value;
            var position = GetPlacePosition();
            PendingPlacePosition = position;
            var command = BuildSetpointCommand(carried, position);
            _status = $"Placed {carried.Label}.";
            _carrying = null;
            ValheimUtil.RunCommand(command);
        }

        private static string BuildSetpointCommand(EndpointRef endpoint, Vector3 position)
        {
            // Position is sent via CommandContext origin from CaptureClientOrigin.
            _ = position;
            var end = endpoint.PointA ? "a" : "b";
            switch (endpoint.Kind)
            {
                case RaceManager.GateKind.Start:
                    return $"setpoint start {end}";
                case RaceManager.GateKind.Finish:
                    return $"setpoint finish {end}";
                default:
                    return $"setpoint checkpoint {endpoint.CheckpointIndex} {end}";
            }
        }

        private static void ApplyOptimisticPreview(EndpointRef endpoint)
        {
            var track = RaceNetSync.GetActiveTrack();
            if (track == null)
            {
                return;
            }

            RaceGate gate = null;
            switch (endpoint.Kind)
            {
                case RaceManager.GateKind.Start:
                    gate = track.StartGate;
                    break;
                case RaceManager.GateKind.Finish:
                    gate = track.FinishGate;
                    break;
                case RaceManager.GateKind.Checkpoint:
                    gate = track.Checkpoints?.Find(item => item.Index == endpoint.CheckpointIndex)?.Gate;
                    break;
            }

            gate?.SetEndpoint(endpoint.PointA, endpoint.Position);
        }

        private EndpointRef? FindLookedEndpoint()
        {
            var camera = Camera.main;
            var track = RaceNetSync.GetActiveTrack();
            if (camera == null || track == null || Player.m_localPlayer == null)
            {
                return null;
            }

            var ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            EndpointRef? best = null;
            var bestScore = float.MaxValue;
            const float maxDistance = 12f;
            const float maxAngle = 18f;

            void Consider(RaceManager.GateKind kind, int index, RaceGate gate, string prefix)
            {
                if (gate == null || !gate.IsConfigured())
                {
                    return;
                }

                Score(kind, index, true, gate.GetPointA(), $"{prefix} A");
                Score(kind, index, false, gate.GetPointB(), $"{prefix} B");
            }

            void Score(RaceManager.GateKind kind, int index, bool pointA, Vector3 world, string label)
            {
                var toPoint = world + Vector3.up * 1.2f - ray.origin;
                var distance = toPoint.magnitude;
                if (distance > maxDistance || distance < 0.01f)
                {
                    return;
                }

                var angle = Vector3.Angle(ray.direction, toPoint);
                if (angle > maxAngle)
                {
                    return;
                }

                var score = angle * 2f + distance;
                if (score >= bestScore)
                {
                    return;
                }

                bestScore = score;
                best = new EndpointRef
                {
                    Kind = kind,
                    CheckpointIndex = index,
                    PointA = pointA,
                    Position = world,
                    Label = label
                };
            }

            if (track.HasStart)
            {
                Consider(RaceManager.GateKind.Start, 0, track.StartGate, "START");
            }

            if (track.HasFinish)
            {
                Consider(RaceManager.GateKind.Finish, 0, track.FinishGate, "FINISH");
            }

            if (track.Checkpoints != null)
            {
                foreach (var checkpoint in track.Checkpoints)
                {
                    Consider(RaceManager.GateKind.Checkpoint, checkpoint.Index, checkpoint.Gate, $"CP{checkpoint.Index}");
                }
            }

            return best;
        }

        private static bool IsHoldingHammer()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }

            var right = player.GetRightItem();
            if (right?.m_shared != null && right.m_shared.m_buildPieces != null)
            {
                return true;
            }

            var name = right?.m_dropPrefab != null ? right.m_dropPrefab.name : right?.m_shared?.m_name;
            return !string.IsNullOrEmpty(name) &&
                   name.IndexOf("Hammer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool WasUsePressed()
        {
            try
            {
                if (ZInput.GetButtonDown("Use"))
                {
                    return true;
                }
            }
            catch
            {
                // Fall through to keyboard.
            }

            return Input.GetKeyDown(KeyCode.E);
        }

        private static string FormatLabel(RaceManager.GateKind kind, int checkpointIndex, bool pointA)
        {
            var end = pointA ? "A" : "B";
            switch (kind)
            {
                case RaceManager.GateKind.Start:
                    return $"START {end}";
                case RaceManager.GateKind.Finish:
                    return $"FINISH {end}";
                default:
                    return $"CP{checkpointIndex} {end}";
            }
        }

        private void EnsureHudStyle()
        {
            if (_hudStyle != null)
            {
                return;
            }

            _hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                normal = { textColor = new Color(1f, 0.92f, 0.35f) }
            };
        }
    }
}
