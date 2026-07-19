using System.Collections.Generic;
using System.Linq;
using RunningMan.Storage;

namespace RunningMan
{
    /// <summary>
    /// Per-track world record storage and queries.
    /// </summary>
    public static class Leaderboard
    {
        public static void RecordCompletedRun(RaceRecord record)
        {
            var database = JsonStorage.Database;
            if (string.IsNullOrEmpty(record.TrackId))
            {
                record.TrackId = TrackIdentity.GetId(JsonStorage.Track);
            }

            database.Runs.Add(record);

            var playerKey = record.PlayerId;
            database.LastRuns[playerKey] = record;

            if (!database.PersonalBests.TryGetValue(playerKey, out var currentBest) ||
                record.TotalTimeMs < currentBest.TotalTimeMs)
            {
                database.PersonalBests[playerKey] = record;
            }

            JsonStorage.SaveDatabase();
        }

        public static RaceRecord GetPersonalBest(long playerId)
        {
            JsonStorage.Database.PersonalBests.TryGetValue(playerId.ToString(), out var record);
            return record;
        }

        public static RaceRecord GetLastRun(long playerId)
        {
            JsonStorage.Database.LastRuns.TryGetValue(playerId.ToString(), out var record);
            return record;
        }

        public static IReadOnlyList<RaceRecord> GetWorldRecords(TrackConfig track, int limit)
        {
            var trackId = TrackIdentity.GetId(track);
            if (string.IsNullOrEmpty(trackId))
            {
                return new List<RaceRecord>();
            }

            return JsonStorage.Database.Runs
                .Where(record => string.Equals(record.TrackId, trackId, System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(record => record.TotalTimeMs)
                .Take(limit)
                .ToList();
        }

        public static int ClearRecordsForTrackId(string trackId)
        {
            if (string.IsNullOrEmpty(trackId))
            {
                return 0;
            }

            var database = JsonStorage.Database;
            var removed = database.Runs.RemoveAll(record =>
                string.Equals(record.TrackId, trackId, System.StringComparison.OrdinalIgnoreCase));
            PruneDerivedRecords(database, trackId);
            JsonStorage.SaveDatabase();
            return removed;
        }

        public static int ClearWorldRecords(TrackConfig track)
        {
            var trackId = TrackIdentity.GetId(track);
            return ClearRecordsForTrackId(trackId);
        }

        public static int ClearAllWorldRecords()
        {
            var database = JsonStorage.Database;
            var removed = database.Runs.Count;
            database.Runs.Clear();
            database.PersonalBests.Clear();
            database.LastRuns.Clear();
            JsonStorage.SaveDatabase();
            return removed;
        }

        public static string FormatWorldRecords(TrackConfig track, int limit)
        {
            var trackName = TrackIdentity.GetDisplayName(track);
            var runs = GetWorldRecords(track, limit);
            if (runs.Count == 0)
            {
                return $"No world records for {trackName} yet.";
            }

            var lines = new List<string> { $"{trackName} — World Records:" };
            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                lines.Add($"{i + 1}. {run.Player} - {run.TotalTime} ({run.Date})");
            }

            return string.Join("\n", lines);
        }

        public static List<WorldRecordEntry> BuildWorldRecordEntries(TrackConfig track, int limit)
        {
            var entries = new List<WorldRecordEntry>();
            var runs = GetWorldRecords(track, limit);
            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                entries.Add(new WorldRecordEntry
                {
                    Place = i + 1,
                    PlayerName = run.Player,
                    Time = run.TotalTime,
                    TimeMs = run.TotalTimeMs,
                    Date = run.Date
                });
            }

            return entries;
        }

        public static string FormatPersonalBest(RaceRecord record)
        {
            if (record == null)
            {
                return "No personal best recorded yet.";
            }

            return $"{record.Player} PB: {record.TotalTime} on {record.Date}";
        }

        public static string FormatLastRun(RaceRecord record)
        {
            if (record == null)
            {
                return "No completed runs recorded yet.";
            }

            return $"{record.Player} last run: {record.TotalTime} on {record.Date}";
        }

        private static void PruneDerivedRecords(RaceDatabase database, string clearedTrackId)
        {
            _ = clearedTrackId;
            var remainingByPlayer = database.Runs
                .GroupBy(record => record.PlayerId)
                .ToDictionary(group => group.Key, group => group.OrderBy(record => record.TotalTimeMs).ToList());

            var personalBests = new Dictionary<string, RaceRecord>(System.StringComparer.Ordinal);
            var lastRuns = new Dictionary<string, RaceRecord>(System.StringComparer.Ordinal);
            foreach (var pair in remainingByPlayer)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                personalBests[pair.Key] = pair.Value[0];
                lastRuns[pair.Key] = pair.Value[pair.Value.Count - 1];
            }

            database.PersonalBests = personalBests;
            database.LastRuns = lastRuns;
        }
    }
}
