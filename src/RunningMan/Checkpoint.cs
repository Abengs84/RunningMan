using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// A race gate defined by two endpoints and an optional forward direction.
    /// Used for start lines, finish lines, and checkpoint pairs.
    /// </summary>
    [Serializable]
    public sealed class RaceGate
    {
        public Vector3Data PointA = new Vector3Data();
        public Vector3Data PointB = new Vector3Data();
        public Vector3Data Forward = new Vector3Data();

        public RaceGate()
        {
        }

        public RaceGate(Vector3 pointA, Vector3 pointB, Vector3 forward)
        {
            PointA = ValheimUtil.ToData(pointA);
            PointB = ValheimUtil.ToData(pointB);
            Forward = ValheimUtil.ToData(forward);
        }

        public Vector3 GetPointA() => ValheimUtil.FromData(PointA);
        public Vector3 GetPointB() => ValheimUtil.FromData(PointB);
        public Vector3 GetForward() => ValheimUtil.FromData(Forward);

        public Vector3 GetMidpoint()
        {
            return (GetPointA() + GetPointB()) * 0.5f;
        }

        public bool IsConfigured()
        {
            return PointA != null && PointB != null &&
                   (GetPointA() - GetPointB()).sqrMagnitude > 0.01f;
        }

        /// <summary>
        /// Moves one endpoint and rebuilds forward from the gate line (XZ).
        /// </summary>
        public void SetEndpoint(bool pointA, Vector3 worldPosition, Vector3? preferredForward = null)
        {
            if (pointA)
            {
                PointA = ValheimUtil.ToData(worldPosition);
            }
            else
            {
                PointB = ValheimUtil.ToData(worldPosition);
            }

            RecomputeForward(preferredForward);
        }

        public void RecomputeForward(Vector3? preferredForward = null)
        {
            var pointA = GetPointA();
            var pointB = GetPointB();
            var along = pointB - pointA;
            along.y = 0f;
            if (along.sqrMagnitude < 0.0001f)
            {
                var fallback = preferredForward ?? GetForward();
                fallback.y = 0f;
                if (fallback.sqrMagnitude < 0.0001f)
                {
                    fallback = Vector3.forward;
                }

                Forward = ValheimUtil.ToData(fallback.normalized);
                return;
            }

            var forward = Vector3.Cross(Vector3.up, along.normalized);
            if (preferredForward.HasValue)
            {
                var prefer = preferredForward.Value;
                prefer.y = 0f;
                if (prefer.sqrMagnitude > 0.0001f && Vector3.Dot(forward, prefer) < 0f)
                {
                    forward = -forward;
                }
            }
            else
            {
                var current = GetForward();
                current.y = 0f;
                if (current.sqrMagnitude > 0.0001f && Vector3.Dot(forward, current) < 0f)
                {
                    forward = -forward;
                }
            }

            Forward = ValheimUtil.ToData(forward.normalized);
        }

        /// <summary>
        /// Creates a gate centered on a position, spanning left/right relative to facing.
        /// </summary>
        public static RaceGate FromPositionAndForward(Vector3 position, Vector3 forward, float width)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var half = width * 0.5f;
            return new RaceGate(position - right * half, position + right * half, forward);
        }

        /// <summary>
        /// Creates a gate centered on a position, spanning left/right relative to player facing.
        /// </summary>
        public static RaceGate FromPlayerPosition(Player player, float width)
        {
            var position = player.transform.position;
            var forward = player.transform.forward;
            return FromPositionAndForward(position, forward, width);
        }

        /// <summary>
        /// Best-effort gate from a connected peer when no Player instance exists on the server.
        /// </summary>
        public static RaceGate FromPeerPosition(ZNetPeer peer, float width)
        {
            if (peer != null && ValheimUtil.TryGetPeerWorldPosition(peer, out var position))
            {
                return FromPositionAndForward(position, Vector3.forward, width);
            }

            return FromPositionAndForward(Vector3.zero, Vector3.forward, width);
        }
    }

    /// <summary>
    /// Ordered checkpoint gate with a stable index.
    /// </summary>
    [Serializable]
    public sealed class Checkpoint
    {
        public int Index;
        public RaceGate Gate = new RaceGate();

        public Checkpoint()
        {
        }

        public Checkpoint(int index, RaceGate gate)
        {
            Index = index;
            Gate = gate;
        }
    }

    /// <summary>
    /// Persisted track layout including start, finish, and ordered checkpoints.
    /// </summary>
    [Serializable]
    public sealed class TrackConfig
    {
        public string Name = string.Empty;
        public RaceGate StartGate = new RaceGate();
        public RaceGate FinishGate = new RaceGate();
        public List<Checkpoint> Checkpoints = new List<Checkpoint>();

        public bool HasStart => StartGate != null && StartGate.IsConfigured();
        public bool HasFinish => FinishGate != null && FinishGate.IsConfigured();
        public int CheckpointCount => Checkpoints?.Count ?? 0;
    }
}
