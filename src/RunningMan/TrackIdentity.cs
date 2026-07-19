using System.Globalization;
using System.Text;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Stable identity for a track layout so world records stay tied to the course.
    /// </summary>
    public static class TrackIdentity
    {
        public static string GetId(TrackConfig track)
        {
            if (track == null || !track.HasStart)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendGate(builder, track.StartGate);
            AppendGate(builder, track.FinishGate);
            if (track.Checkpoints != null)
            {
                foreach (var checkpoint in track.Checkpoints)
                {
                    AppendGate(builder, checkpoint?.Gate);
                }
            }

            return builder.ToString().GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
        }

        public static string GetDisplayName(TrackConfig track)
        {
            if (track == null)
            {
                return "Unconfigured track";
            }

            if (!string.IsNullOrWhiteSpace(track.Name))
            {
                return track.Name.Trim();
            }

            var id = GetId(track);
            return string.IsNullOrEmpty(id) ? "Unconfigured track" : $"Track {id}";
        }

        private static void AppendGate(StringBuilder builder, RaceGate gate)
        {
            if (gate == null || !gate.IsConfigured())
            {
                return;
            }

            AppendPoint(builder, gate.GetPointA());
            AppendPoint(builder, gate.GetPointB());
            AppendPoint(builder, gate.GetForward());
        }

        private static void AppendPoint(StringBuilder builder, Vector3 point)
        {
            builder.Append(Mathf.RoundToInt(point.x * 10f).ToString(CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Mathf.RoundToInt(point.y * 10f).ToString(CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Mathf.RoundToInt(point.z * 10f).ToString(CultureInfo.InvariantCulture));
            builder.Append(';');
        }
    }
}
