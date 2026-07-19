using HarmonyLib;

namespace RunningMan.Patches
{
    /// <summary>
    /// Registers network RPC handlers when the game starts.
    /// </summary>
    [HarmonyPatch(typeof(Game), "Start")]
    public static class GameStartPatch
    {
        private static void Postfix()
        {
            Commands.Register();
            Net.RaceNetSync.Register();
        }
    }
}
