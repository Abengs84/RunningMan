using HarmonyLib;
using UnityEngine;

namespace RunningMan.Patches
{
    /// <summary>
    /// Release the mouse and block player movement while the F6 panel is open.
    /// </summary>
    [HarmonyPatch(typeof(Player), "TakeInput")]
    public static class PlayerTakeInputPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (RaceGui.IsOpen)
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    public static class PlayerControllerTakeInputPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (RaceGui.IsOpen)
            {
                __result = false;
            }
        }
    }
}
