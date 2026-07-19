namespace RunningMan
{
    public enum RaceEventPhase
    {
        /// <summary>No event — before registration opens (or after cancel / empty close).</summary>
        Idle = 0,
        /// <summary>Registration is open; runners may join.</summary>
        Registration = 1,
        Countdown = 2,
        Racing = 3,
        /// <summary>Registration closed with runners waiting for countdown.</summary>
        Ready = 4
    }
}
