using System.Collections.Generic;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Auto-built race path from start → checkpoints (in order) → finish midpoints.
    /// Used for live standings progress / overtake detection between gates.
    /// </summary>
    public static class TrackPath
    {
        public sealed class PathData
        {
            public readonly List<Vector3> Points = new List<Vector3>();
            public readonly List<float> Cumulative = new List<float>();
            public float TotalLength;
        }

        public static PathData Build(TrackConfig track)
        {
            var path = new PathData();
            if (track == null)
            {
                return path;
            }

            if (track.HasStart)
            {
                AddPoint(path, track.StartGate.GetMidpoint());
            }

            if (track.Checkpoints != null)
            {
                var ordered = new List<Checkpoint>(track.Checkpoints);
                ordered.Sort((left, right) => left.Index.CompareTo(right.Index));
                foreach (var checkpoint in ordered)
                {
                    if (checkpoint?.Gate != null && checkpoint.Gate.IsConfigured())
                    {
                        AddPoint(path, checkpoint.Gate.GetMidpoint());
                    }
                }
            }

            if (track.HasFinish)
            {
                AddPoint(path, track.FinishGate.GetMidpoint());
            }

            return path;
        }

        /// <summary>
        /// Meters of course progress. Higher = further along the race.
        /// </summary>
        public static float GetProgress(TrackConfig track, Vector3 worldPosition, int nextCheckpointIndex,
            bool finished)
        {
            var path = Build(track);
            if (path.Points.Count < 2)
            {
                return 0f;
            }

            if (finished)
            {
                return path.TotalLength;
            }

            // Waypoint 0 = start, 1 = CP1, ..., N = CPN, last = finish.
            // nextCheckpointIndex 1 → segment start→CP1 (from 0 to 1)
            // nextCheckpointIndex > checkpointCount → segment lastCP→finish
            var fromIndex = Mathf.Clamp(nextCheckpointIndex - 1, 0, path.Points.Count - 2);
            var toIndex = fromIndex + 1;
            var baseProgress = path.Cumulative[fromIndex];
            var alongSegment = ProjectOntoSegmentXZ(worldPosition, path.Points[fromIndex], path.Points[toIndex]);
            return baseProgress + alongSegment;
        }

        /// <summary>
        /// 1-based checkpoint index where a new gate should be inserted along the race path.
        /// Picks the closest path segment (Start→CPs→Finish) so mid-course placements
        /// land between neighbors instead of always appending.
        /// </summary>
        public static int FindCheckpointInsertIndex(TrackConfig track, Vector3 position)
        {
            var existing = track?.Checkpoints?.Count ?? 0;
            if (existing == 0)
            {
                return 1;
            }

            var path = Build(track);
            if (path.Points.Count < 2)
            {
                return existing + 1;
            }

            var bestSegment = 0;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < path.Points.Count - 1; i++)
            {
                var distance = DistanceToSegmentXZ(position, path.Points[i], path.Points[i + 1]);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestSegment = i;
            }

            // With start: path[0]=start, then CP1..CPn, then finish.
            // Segment k maps to insert index k+1 (seg 0 = before/at CP1, last = append).
            // Without start: path starts at CP1 — segment k maps to insert index k+2 for
            // between-CP segments, or append on the final stretch to finish.
            if (track.HasStart)
            {
                return Mathf.Clamp(bestSegment + 1, 1, existing + 1);
            }

            if (track.HasFinish && bestSegment >= existing - 1)
            {
                return existing + 1;
            }

            return Mathf.Clamp(bestSegment + 2, 1, existing + 1);
        }

        private static void AddPoint(PathData path, Vector3 point)
        {
            if (path.Points.Count == 0)
            {
                path.Points.Add(point);
                path.Cumulative.Add(0f);
                path.TotalLength = 0f;
                return;
            }

            var previous = path.Points[path.Points.Count - 1];
            var flat = point - previous;
            flat.y = 0f;
            var distance = flat.magnitude;
            path.Points.Add(point);
            path.TotalLength += distance;
            path.Cumulative.Add(path.TotalLength);
        }

        private static float DistanceToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
        {
            var flatPoint = point;
            flatPoint.y = 0f;
            var flatA = a;
            flatA.y = 0f;
            var flatB = b;
            flatB.y = 0f;
            var ab = flatB - flatA;
            var lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.0001f)
            {
                return Vector3.Distance(flatPoint, flatA);
            }

            var t = Mathf.Clamp01(Vector3.Dot(flatPoint - flatA, ab) / lengthSq);
            var projected = flatA + ab * t;
            return Vector3.Distance(flatPoint, projected);
        }

        private static float ProjectOntoSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
        {
            var flatPoint = point;
            flatPoint.y = 0f;
            var flatA = a;
            flatA.y = 0f;
            var flatB = b;
            flatB.y = 0f;
            var ab = flatB - flatA;
            var lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.0001f)
            {
                return 0f;
            }

            var t = Mathf.Clamp01(Vector3.Dot(flatPoint - flatA, ab) / lengthSq);
            return Mathf.Sqrt(lengthSq) * t;
        }
    }
}
