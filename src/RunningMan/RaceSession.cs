using System;
using System.Collections.Generic;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Active race state for a single player.
    /// </summary>
    public sealed class RaceSession
    {
        public long PlayerId { get; }
        public string PlayerName { get; }
        public DateTime StartUtc { get; private set; }
        public int NextCheckpointIndex { get; private set; } = 1;
        public List<long> CheckpointTimesMs { get; } = new List<long>();
        public List<long> SplitTimesMs { get; } = new List<long>();
        public Vector3 LastPosition { get; set; }
        public bool HasPreviousPosition { get; set; }

        public bool Finished { get; set; }
        public long FinishTimeMs { get; set; }
        public bool FeatherCapeUnlocked { get; set; }
        public bool Disqualified { get; set; }
        public string DisqualifiedReason { get; set; } = string.Empty;
        public bool RunSkillNormalized { get; set; }
        public float? SavedRunSkillLevel { get; set; }
        public int LastMissedCheckpointNotified { get; set; }

        public RaceSession(long playerId, string playerName, DateTime startUtc)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            StartUtc = startUtc;
        }

        public bool IsRunning => StartUtc != default;

        public long ElapsedMs(DateTime nowUtc)
        {
            if (!IsRunning)
            {
                return 0;
            }

            return (long)(nowUtc - StartUtc).TotalMilliseconds;
        }

        public void RecordCheckpoint(DateTime nowUtc)
        {
            var elapsed = ElapsedMs(nowUtc);
            CheckpointTimesMs.Add(elapsed);

            var previousCheckpointTotal = CheckpointTimesMs.Count > 1
                ? CheckpointTimesMs[CheckpointTimesMs.Count - 2]
                : 0L;
            SplitTimesMs.Add(elapsed - previousCheckpointTotal);

            NextCheckpointIndex++;
        }

        public RaceRecord ToCompletedRecord(DateTime finishUtc, int totalCheckpoints)
        {
            var totalMs = (long)(finishUtc - StartUtc).TotalMilliseconds;
            return new RaceRecord
            {
                Player = PlayerName,
                PlayerId = PlayerId.ToString(),
                Date = finishUtc.ToString("yyyy-MM-dd"),
                FinishedAt = finishUtc.ToString("o"),
                TotalTimeMs = totalMs,
                TotalTime = TimeFormatter.FormatDurationMs(totalMs),
                CheckpointTimesMs = new List<long>(CheckpointTimesMs),
                CheckpointTimes = FormatTimes(CheckpointTimesMs),
                SplitTimesMs = new List<long>(SplitTimesMs),
                SplitTimes = FormatTimes(SplitTimesMs),
                CheckpointCount = totalCheckpoints,
                TrackId = TrackIdentity.GetId(JsonStorage.Track)
            };
        }

        private static List<string> FormatTimes(List<long> timesMs)
        {
            var formatted = new List<string>(timesMs.Count);
            foreach (var ms in timesMs)
            {
                formatted.Add(TimeFormatter.FormatDurationMs(ms));
            }

            return formatted;
        }
    }

    /// <summary>
    /// Completed race record stored in JSON.
    /// </summary>
    [Serializable]
    public sealed class RaceRecord
    {
        public string Player;
        public string PlayerId;
        public string Date;
        public string FinishedAt;
        public long TotalTimeMs;
        public string TotalTime;
        public List<long> CheckpointTimesMs = new List<long>();
        public List<string> CheckpointTimes = new List<string>();
        public List<long> SplitTimesMs = new List<long>();
        public List<string> SplitTimes = new List<string>();
        public int CheckpointCount;
        public string TrackId = string.Empty;
    }
}
