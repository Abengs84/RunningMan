using System;
using System.Collections.Generic;
using RunningMan.Storage;

namespace RunningMan
{
    /// <summary>
    /// Server snapshot broadcast to all clients for HUD, GUI, and debug rendering.
    /// </summary>
    [Serializable]
    public sealed class RaceStateSnapshot
    {
        public int Phase;
        public long CountdownEndUtcTicks;
        public bool DebugMode;
        public bool RegistrationOpen;
        public int TotalCheckpoints;
        public List<RegisteredParticipant> Registered = new List<RegisteredParticipant>();
        public List<RunnerSnapshot> Runners = new List<RunnerSnapshot>();
        public List<long> AdminPlayerIds = new List<long>();
        public string TrackName = string.Empty;
        public string TrackId = string.Empty;
        public List<WorldRecordEntry> WorldRecords = new List<WorldRecordEntry>();
        /// <summary>#1 WR cumulative checkpoint times (ms), for race-stats par display.</summary>
        public List<long> ParCheckpointTimesMs = new List<long>();
        public long ParTotalTimeMs;
        public AllowedGearRules AllowedGear;
    }

    [Serializable]
    public sealed class RegisteredParticipant
    {
        public long PlayerId;
        public string PlayerName;
    }

    [Serializable]
    public sealed class RunnerSnapshot
    {
        public long PlayerId;
        public string PlayerName;
        public long StartUtcTicks;
        public int NextCheckpointIndex;
        public bool Finished;
        public long FinishTimeMs;
        public int Place;
        public float PathProgress;
        public bool Disqualified;
        public string DisqualifiedReason = string.Empty;
        public bool FeatherCapeUnlocked;
        /// <summary>Cumulative time at the last completed checkpoint (0 if none yet).</summary>
        public long LastCheckpointTimeMs;
    }

    [Serializable]
    public sealed class WorldRecordEntry
    {
        public int Place;
        public string PlayerName;
        public string Time;
        public long TimeMs;
        public string Date;
    }
}
