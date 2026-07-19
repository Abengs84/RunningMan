using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunningMan.Patches
{
    /// <summary>
    /// Renders RunningMan world records on marked vanilla Signs and blocks rewriting them.
    /// </summary>
    [HarmonyPatch(typeof(Sign), nameof(Sign.UpdateText))]
    public static class SignUpdateTextWrBoardPatch
    {
        private static void Postfix(Sign __instance)
        {
            if (__instance == null || !RaceWrBoards.IsMarkedSign(__instance))
            {
                return;
            }

            RaceWrBoards.ApplySignText(__instance);
        }
    }

    [HarmonyPatch]
    public static class SignInteractWrBoardPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Sign), "Interact");
        }

        private static bool Prefix(Sign __instance, ref bool __result)
        {
            if (__instance == null || !RaceWrBoards.IsMarkedSign(__instance))
            {
                return true;
            }

            // Block the write-on-sign UI; WR text is managed by RunningMan.
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Sign), nameof(Sign.GetHoverText))]
    public static class SignHoverTextWrBoardPatch
    {
        private static void Postfix(Sign __instance, ref string __result)
        {
            if (__instance == null || !RaceWrBoards.TryGetBulletinKind(__instance, out var kind))
            {
                return;
            }

            __result = RaceWrBoards.GetHoverLabel(kind);
        }
    }

    [HarmonyPatch(typeof(Sign), nameof(Sign.SetText))]
    public static class SignSetTextWrBoardPatch
    {
        private static bool Prefix(Sign __instance)
        {
            // Prevent players/admins from overwriting WR boards via the normal sign UI.
            return __instance == null || !RaceWrBoards.IsMarkedSign(__instance);
        }
    }
}
