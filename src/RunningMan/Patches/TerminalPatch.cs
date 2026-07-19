using HarmonyLib;

namespace RunningMan.Patches
{
    /// <summary>
    /// Registers /run commands once the terminal is initialized.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class TerminalPatch
    {
        private static void Postfix()
        {
            Commands.Register();
        }
    }
}
