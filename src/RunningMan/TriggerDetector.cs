using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Detects when a player crosses a gate line between two positions.
    /// Supports horizontal and vertical tolerance for elevated or wide gates.
    /// </summary>
    public static class TriggerDetector
    {
        /// <summary>
        /// Returns true when the player crossed the gate forward since the previous position.
        /// </summary>
        public static bool DidCrossForward(Vector3 previousPosition, Vector3 currentPosition, RaceGate gate,
            float maxHorizontalDistance, float maxVerticalDistance = 8f)
        {
            if (gate == null || !gate.IsConfigured())
            {
                return false;
            }

            if (!IsWithinGateVolume(currentPosition, gate, maxHorizontalDistance, maxVerticalDistance))
            {
                return false;
            }

            var pointA = gate.GetPointA();
            var pointB = gate.GetPointB();
            var forward = GetGateForward(gate, pointA, pointB);
            var midpoint = gate.GetMidpoint();
            var prevSide = Vector3.Dot(Flatten(previousPosition - midpoint), forward);
            var currSide = Vector3.Dot(Flatten(currentPosition - midpoint), forward);

            return prevSide <= 0f && currSide > 0f;
        }

        /// <summary>
        /// Returns true when the player crossed the gate line in either direction.
        /// </summary>
        public static bool DidCrossLine(Vector3 previousPosition, Vector3 currentPosition, RaceGate gate,
            float maxHorizontalDistance, float maxVerticalDistance = 8f)
        {
            if (gate == null || !gate.IsConfigured())
            {
                return false;
            }

            if (!IsWithinGateVolume(currentPosition, gate, maxHorizontalDistance, maxVerticalDistance))
            {
                return false;
            }

            var pointA = gate.GetPointA();
            var pointB = gate.GetPointB();
            var forward = GetGateForward(gate, pointA, pointB);
            var midpoint = gate.GetMidpoint();
            var prevSide = Vector3.Dot(Flatten(previousPosition - midpoint), forward);
            var currSide = Vector3.Dot(Flatten(currentPosition - midpoint), forward);
            if (Mathf.Approximately(prevSide, 0f) || Mathf.Approximately(currSide, 0f))
            {
                return false;
            }

            return prevSide * currSide < 0f;
        }

        /// <summary>
        /// Returns true when the player entered the gate trigger volume this tick.
        /// Useful for elevated checkpoints where plane crossing is hard to hit.
        /// </summary>
        public static bool DidEnterGateVolume(Vector3 previousPosition, Vector3 currentPosition, RaceGate gate,
            float maxHorizontalDistance, float maxVerticalDistance)
        {
            if (gate == null || !gate.IsConfigured())
            {
                return false;
            }

            var wasInside = IsWithinGateVolume(previousPosition, gate, maxHorizontalDistance, maxVerticalDistance);
            var isInside = IsWithinGateVolume(currentPosition, gate, maxHorizontalDistance, maxVerticalDistance);
            return !wasInside && isInside;
        }

        /// <summary>
        /// Checkpoint/finish helper — crossing, volume entry, swept path, or standing inside the zone.
        /// </summary>
        public static bool DidTriggerGate(Vector3 previousPosition, Vector3 currentPosition, RaceGate gate,
            float maxHorizontalDistance, float maxVerticalDistance)
        {
            if (gate == null || !gate.IsConfigured())
            {
                return false;
            }

            // Prefer real passage: crossing / entering. "Already inside" alone is too easy to miss
            // on dedicated servers when positions jump, and too easy to false-trigger near start.
            if (DidCrossForward(previousPosition, currentPosition, gate, maxHorizontalDistance, maxVerticalDistance) ||
                DidCrossLine(previousPosition, currentPosition, gate, maxHorizontalDistance, maxVerticalDistance) ||
                DidEnterGateVolume(previousPosition, currentPosition, gate, maxHorizontalDistance, maxVerticalDistance) ||
                DidCrossGatePlane(previousPosition, currentPosition, gate, maxHorizontalDistance, maxVerticalDistance) ||
                DidTriggerGateAlongPath(previousPosition, currentPosition, gate, maxHorizontalDistance,
                    maxVerticalDistance))
            {
                return true;
            }

            // Fallback: currently inside and we just arrived from outside the padded volume.
            var wasInside = IsWithinGateVolume(previousPosition, gate, maxHorizontalDistance * 1.5f,
                maxVerticalDistance);
            var isInside = IsWithinGateVolume(currentPosition, gate, maxHorizontalDistance, maxVerticalDistance);
            return isInside && !wasInside;
        }

        /// <summary>
        /// Detects gate-plane crossing along the movement segment even when neither endpoint is inside the volume.
        /// </summary>
        public static bool DidCrossGatePlane(Vector3 previousPosition, Vector3 currentPosition, RaceGate gate,
            float maxHorizontalDistance, float maxVerticalDistance)
        {
            if (gate == null || !gate.IsConfigured())
            {
                return false;
            }

            var pointA = gate.GetPointA();
            var pointB = gate.GetPointB();
            var forward = GetGateForward(gate, pointA, pointB);
            var midpoint = gate.GetMidpoint();
            var prevSide = Vector3.Dot(Flatten(previousPosition - midpoint), forward);
            var currSide = Vector3.Dot(Flatten(currentPosition - midpoint), forward);

            if (Mathf.Approximately(prevSide, 0f))
            {
                return IsWithinGateVolume(previousPosition, gate, maxHorizontalDistance, maxVerticalDistance);
            }

            if (Mathf.Approximately(currSide, 0f))
            {
                return IsWithinGateVolume(currentPosition, gate, maxHorizontalDistance, maxVerticalDistance);
            }

            if (prevSide * currSide > 0f)
            {
                return SegmentIntersectsGateVolume(previousPosition, currentPosition, gate, maxHorizontalDistance,
                    maxVerticalDistance);
            }

            var t = prevSide / (prevSide - currSide);
            if (t < 0f || t > 1f)
            {
                return false;
            }

            var crossing = Vector3.Lerp(previousPosition, currentPosition, t);
            return IsWithinGateVolume(crossing, gate, maxHorizontalDistance, maxVerticalDistance);
        }

        public static bool SegmentIntersectsGateVolume(Vector3 start, Vector3 end, RaceGate gate,
            float maxHorizontalDistance, float maxVerticalDistance)
        {
            var step = Mathf.Max(0.5f, ModConfig.TriggerSweepStepDistance.Value);
            var distance = Vector3.Distance(Flatten(start), Flatten(end));
            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / step));
            for (var i = 0; i <= steps; i++)
            {
                var point = Vector3.Lerp(start, end, i / (float)steps);
                if (IsWithinGateVolume(point, gate, maxHorizontalDistance, maxVerticalDistance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DidTriggerGateAlongPath(Vector3 previousPosition, Vector3 currentPosition, RaceGate gate,
            float maxHorizontalDistance, float maxVerticalDistance)
        {
            var step = Mathf.Max(0.5f, ModConfig.TriggerSweepStepDistance.Value);
            var distance = Vector3.Distance(Flatten(previousPosition), Flatten(currentPosition));
            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / step));
            for (var i = 1; i <= steps; i++)
            {
                var t0 = (i - 1f) / steps;
                var t1 = i / (float)steps;
                var segmentStart = Vector3.Lerp(previousPosition, currentPosition, t0);
                var segmentEnd = Vector3.Lerp(previousPosition, currentPosition, t1);
                if (DidCrossForward(segmentStart, segmentEnd, gate, maxHorizontalDistance, maxVerticalDistance) ||
                    DidCrossLine(segmentStart, segmentEnd, gate, maxHorizontalDistance, maxVerticalDistance) ||
                    DidEnterGateVolume(segmentStart, segmentEnd, gate, maxHorizontalDistance, maxVerticalDistance) ||
                    DidCrossGatePlane(segmentStart, segmentEnd, gate, maxHorizontalDistance, maxVerticalDistance))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsWithinGateVolume(Vector3 point, RaceGate gate, float maxHorizontalDistance,
            float maxVerticalDistance)
        {
            if (gate == null || !gate.IsConfigured())
            {
                return false;
            }

            var pointA = gate.GetPointA();
            var pointB = gate.GetPointB();
            var flatPoint = Flatten(point);
            var flatA = Flatten(pointA);
            var flatB = Flatten(pointB);
            var ab = flatB - flatA;
            var lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.0001f)
            {
                return Vector3.Distance(flatPoint, flatA) <= maxHorizontalDistance &&
                       Mathf.Abs(point.y - pointA.y) <= maxVerticalDistance;
            }

            // Unclamped t: past the endpoints (outside the gate width) must not count.
            var t = Vector3.Dot(flatPoint - flatA, ab) / lengthSq;
            var endPadding = Mathf.Max(0f, ModConfig.GateEndPadding.Value) / Mathf.Sqrt(lengthSq);
            if (t < -endPadding || t > 1f + endPadding)
            {
                return false;
            }

            t = Mathf.Clamp01(t);
            var projection = flatA + ab * t;
            if (Vector3.Distance(flatPoint, projection) > maxHorizontalDistance)
            {
                return false;
            }

            var closest = Vector3.Lerp(pointA, pointB, t);
            return Mathf.Abs(point.y - closest.y) <= maxVerticalDistance;
        }

        /// <summary>
        /// Horizontal distance from a point to a line segment.
        /// </summary>
        public static float DistancePointToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
        {
            var flatPoint = Flatten(point);
            var flatA = Flatten(a);
            var flatB = Flatten(b);
            var ab = flatB - flatA;
            var lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.0001f)
            {
                return Vector3.Distance(flatPoint, flatA);
            }

            var t = Mathf.Clamp01(Vector3.Dot(flatPoint - flatA, ab) / lengthSq);
            var projection = flatA + ab * t;
            return Vector3.Distance(flatPoint, projection);
        }

        private static Vector3 GetGateForward(RaceGate gate, Vector3 pointA, Vector3 pointB)
        {
            var forward = gate.GetForward();
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                var gateDir = pointB - pointA;
                gateDir.y = 0f;
                forward = Vector3.Cross(Vector3.up, gateDir.normalized);
            }
            else
            {
                forward.Normalize();
            }

            return forward;
        }

        private static Vector3 Flatten(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        /// <summary>
        /// Waiting grid just before the start line (default ~1m on the approach side).
        /// </summary>
        public static bool IsInStartingArea(Vector3 position, RaceGate startGate,
            float offsetMeters, float depthMeters, float sidePadding, float maxVertical)
        {
            if (startGate == null || !startGate.IsConfigured())
            {
                return false;
            }

            var pointA = startGate.GetPointA();
            var pointB = startGate.GetPointB();
            var forward = GetGateForward(startGate, pointA, pointB);
            var midpoint = startGate.GetMidpoint();
            var alongGate = Flatten(pointB - pointA);
            var halfWidth = alongGate.magnitude * 0.5f + Mathf.Max(0f, sidePadding);
            if (alongGate.sqrMagnitude > 0.0001f)
            {
                alongGate.Normalize();
            }
            else
            {
                alongGate = Vector3.Cross(Vector3.up, forward);
            }

            var delta = Flatten(position - midpoint);
            var forwardDist = Vector3.Dot(delta, forward);
            var sideDist = Mathf.Abs(Vector3.Dot(delta, alongGate));
            var vertical = Mathf.Abs(position.y - midpoint.y);

            // Approach side: negative forward. Grid centered offsetMeters before the line.
            var offset = Mathf.Max(0.1f, offsetMeters);
            var halfDepth = Mathf.Max(0.5f, depthMeters) * 0.5f;
            var minForward = -offset - halfDepth;
            var maxForward = Mathf.Min(0.35f, -offset + halfDepth);

            return forwardDist >= minForward &&
                   forwardDist <= maxForward &&
                   sideDist <= halfWidth &&
                   vertical <= maxVertical;
        }

        /// <summary>
        /// True when the player has moved past the start line into the race side (false start).
        /// </summary>
        public static bool HasFalseStarted(Vector3 previousPosition, Vector3 currentPosition, RaceGate startGate,
            float maxHorizontalDistance, float maxVerticalDistance)
        {
            if (startGate == null || !startGate.IsConfigured())
            {
                return false;
            }

            if (DidCrossForward(previousPosition, currentPosition, startGate, maxHorizontalDistance, maxVerticalDistance))
            {
                return true;
            }

            var pointA = startGate.GetPointA();
            var pointB = startGate.GetPointB();
            var forward = GetGateForward(startGate, pointA, pointB);
            var midpoint = startGate.GetMidpoint();
            var currSide = Vector3.Dot(Flatten(currentPosition - midpoint), forward);
            if (currSide <= 0.75f)
            {
                return false;
            }

            return DistancePointToSegmentXZ(currentPosition, pointA, pointB) <=
                   maxHorizontalDistance + 2f;
        }

        public static void GetStartingAreaCorners(RaceGate startGate, float offsetMeters, float depthMeters,
            float sidePadding, out Vector3 c0, out Vector3 c1, out Vector3 c2, out Vector3 c3)
        {
            c0 = c1 = c2 = c3 = Vector3.zero;
            if (startGate == null || !startGate.IsConfigured())
            {
                return;
            }

            var pointA = startGate.GetPointA();
            var pointB = startGate.GetPointB();
            var forward = GetGateForward(startGate, pointA, pointB);
            var midpoint = startGate.GetMidpoint();
            var alongGate = Flatten(pointB - pointA);
            var halfWidth = alongGate.magnitude * 0.5f + Mathf.Max(0f, sidePadding);
            if (alongGate.sqrMagnitude > 0.0001f)
            {
                alongGate.Normalize();
            }
            else
            {
                alongGate = Vector3.Cross(Vector3.up, forward);
            }

            var offset = Mathf.Max(0.1f, offsetMeters);
            var halfDepth = Mathf.Max(0.5f, depthMeters) * 0.5f;
            var near = -offset + halfDepth;
            var far = -offset - halfDepth;
            near = Mathf.Min(0.35f, near);

            var left = alongGate * halfWidth;
            var right = -alongGate * halfWidth;
            var y = Vector3.up * 0.2f;
            c0 = midpoint + forward * near + left + y;
            c1 = midpoint + forward * near + right + y;
            c2 = midpoint + forward * far + right + y;
            c3 = midpoint + forward * far + left + y;
        }

        /// <summary>
        /// Finds paired Standing Iron Torch ZDOs and converts them to checkpoint gates.
        /// </summary>
        public static List<Checkpoint> DetectTorchCheckpoints(Vector3 origin, float searchRadius,
            float minPairDistance, float maxPairDistance)
        {
            var torches = FindPrefabPositions("piece_groundtorch", origin, searchRadius);
            var pairs = new List<Checkpoint>();
            var used = new HashSet<int>();

            for (var i = 0; i < torches.Count; i++)
            {
                if (used.Contains(i))
                {
                    continue;
                }

                var bestIndex = -1;
                var bestDistance = float.MaxValue;
                for (var j = i + 1; j < torches.Count; j++)
                {
                    if (used.Contains(j))
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(torches[i], torches[j]);
                    if (distance < minPairDistance || distance > maxPairDistance)
                    {
                        continue;
                    }

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = j;
                    }
                }

                if (bestIndex < 0)
                {
                    continue;
                }

                used.Add(i);
                used.Add(bestIndex);

                var a = torches[i];
                var b = torches[bestIndex];
                var forward = Vector3.Cross(Vector3.up, (b - a).normalized);
                pairs.Add(new Checkpoint(pairs.Count + 1, new RaceGate(a, b, forward)));
            }

            pairs.Sort((left, right) =>
            {
                var leftDist = Vector3.Distance(origin, left.Gate.GetMidpoint());
                var rightDist = Vector3.Distance(origin, right.Gate.GetMidpoint());
                return leftDist.CompareTo(rightDist);
            });

            for (var index = 0; index < pairs.Count; index++)
            {
                pairs[index].Index = index + 1;
            }

            return pairs;
        }

        /// <summary>
        /// Picks the two Standing Iron Torches closest to the player and builds one checkpoint gate.
        /// </summary>
        public static RaceGate DetectNearestTorchPair(Vector3 origin, float searchRadius,
            float minPairDistance, float maxPairDistance)
        {
            var torches = FindPrefabPositions("piece_groundtorch", origin, searchRadius);
            if (torches.Count < 2)
            {
                return null;
            }

            torches.Sort((left, right) =>
                Vector3.Distance(origin, left).CompareTo(Vector3.Distance(origin, right)));

            // Prefer the two nearest torches when their spacing is valid.
            var nearestA = torches[0];
            var nearestB = torches[1];
            var nearestSpacing = Vector3.Distance(nearestA, nearestB);
            if (nearestSpacing >= minPairDistance && nearestSpacing <= maxPairDistance)
            {
                var forward = Vector3.Cross(Vector3.up, (nearestB - nearestA).normalized);
                return new RaceGate(nearestA, nearestB, forward);
            }

            // Otherwise choose the valid pair with the smallest combined distance to the player.
            RaceGate bestGate = null;
            var bestScore = float.MaxValue;
            for (var i = 0; i < torches.Count; i++)
            {
                for (var j = i + 1; j < torches.Count; j++)
                {
                    var spacing = Vector3.Distance(torches[i], torches[j]);
                    if (spacing < minPairDistance || spacing > maxPairDistance)
                    {
                        continue;
                    }

                    var score = Vector3.Distance(origin, torches[i]) + Vector3.Distance(origin, torches[j]);
                    if (score >= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    var forward = Vector3.Cross(Vector3.up, (torches[j] - torches[i]).normalized);
                    bestGate = new RaceGate(torches[i], torches[j], forward);
                }
            }

            return bestGate;
        }

        /// <summary>
        /// Finds nearby snap targets (Standing Iron Torches first) scored by look angle + distance.
        /// </summary>
        public static bool TryFindLookSnapTarget(Vector3 origin, Vector3 lookDirection, float searchRadius,
            float maxAngleDegrees, out Vector3 position, out string prefabHint)
        {
            position = Vector3.zero;
            prefabHint = null;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                lookDirection = Vector3.forward;
            }
            else
            {
                lookDirection.Normalize();
            }

            // Prefer iron torches; also allow a few common gate props.
            var candidates = new List<Vector3>();
            candidates.AddRange(FindPrefabPositions("piece_groundtorch", origin, searchRadius));
            var torchCount = candidates.Count;
            candidates.AddRange(FindPrefabPositions("piece_groundtorch_wood", origin, searchRadius));
            candidates.AddRange(FindPrefabPositions("piece_groundtorch_green", origin, searchRadius));
            candidates.AddRange(FindPrefabPositions("piece_groundtorch_blue", origin, searchRadius));

            Vector3? best = null;
            var bestScore = float.MaxValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var toPoint = candidate - origin;
                toPoint.y = 0f;
                var distance = toPoint.magnitude;
                if (distance < 0.05f || distance > searchRadius)
                {
                    continue;
                }

                var angle = Vector3.Angle(lookDirection, toPoint);
                if (angle > maxAngleDegrees)
                {
                    continue;
                }

                // Slightly prefer true iron torches over alternate prefabs.
                var preference = i < torchCount ? 0f : 1.5f;
                var score = angle * 1.5f + distance + preference;
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                best = candidate;
                prefabHint = i < torchCount ? "Standing Iron Torch" : "torch";
            }

            if (!best.HasValue)
            {
                return false;
            }

            position = best.Value;
            return true;
        }

        /// <summary>
        /// Attempts to locate a Grausten start/finish arch cluster near the given origin.
        /// </summary>
        public static RaceGate DetectGraustenGate(Vector3 origin, float searchRadius)
        {
            var archPieces = new List<Vector3>();
            archPieces.AddRange(FindPrefabPositions("Piece_grausten_pillar_arch", origin, searchRadius));
            archPieces.AddRange(FindPrefabPositions("piece_grausten_pillar_arch", origin, searchRadius));
            archPieces.AddRange(FindPrefabPositions("Piece_grausten_pillarbeam_medium", origin, searchRadius));
            archPieces.AddRange(FindPrefabPositions("piece_grausten_pillarbeam_medium", origin, searchRadius));

            if (archPieces.Count < 2)
            {
                return null;
            }

            var bestA = archPieces[0];
            var bestB = archPieces[1];
            var bestDistance = 0f;
            for (var i = 0; i < archPieces.Count; i++)
            {
                for (var j = i + 1; j < archPieces.Count; j++)
                {
                    var distance = Vector3.Distance(archPieces[i], archPieces[j]);
                    if (distance > bestDistance)
                    {
                        bestDistance = distance;
                        bestA = archPieces[i];
                        bestB = archPieces[j];
                    }
                }
            }

            var forward = Vector3.Cross(Vector3.up, (bestB - bestA).normalized);
            return new RaceGate(bestA, bestB, forward);
        }

        public static List<Vector3> FindPrefabPositions(string prefabName, Vector3 origin, float radius)
        {
            var results = new List<Vector3>();
            if (ZNetScene.instance == null || ZDOMan.instance == null)
            {
                return results;
            }

            var prefabHash = prefabName.GetStableHashCode();
            var radiusSq = radius * radius;

            foreach (var entry in ZDOMan.instance.m_objectsByID)
            {
                var zdo = entry.Value;
                if (zdo == null || !zdo.IsValid())
                {
                    continue;
                }

                if (zdo.GetPrefab() != prefabHash)
                {
                    continue;
                }

                var position = zdo.GetPosition();
                if ((position - origin).sqrMagnitude <= radiusSq)
                {
                    results.Add(position);
                }
            }

            return results;
        }
    }
}
