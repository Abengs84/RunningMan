using HarmonyLib;

namespace RunningMan.Patches
{
    /// <summary>
    /// Ensures /run commands are registered when the terminal wakes up.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "Awake")]
    public static class TerminalAwakePatch
    {
        private static void Postfix()
        {
            Commands.Register();
        }
    }
}
