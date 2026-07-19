using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunningMan.Patches
{
    /// <summary>
    /// Blocks camera zoom from scroll wheel while the F6 panel is open.
    /// </summary>
    [HarmonyPatch]
    public static class UnityScrollAxisBlockPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) });
            yield return AccessTools.Method(typeof(Input), nameof(Input.GetAxisRaw), new[] { typeof(string) });
        }

        private static void Postfix(string axisName, ref float __result)
        {
            if (!RaceGui.IsOpen || string.IsNullOrEmpty(axisName))
            {
                return;
            }

            if (axisName.IndexOf("Scroll", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch]
    public static class ZInputScrollBlockPatch
    {
        private static bool Prepare()
        {
            return AccessTools.TypeByName("ZInput") != null;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var zInput = AccessTools.TypeByName("ZInput");
            if (zInput == null)
            {
                yield break;
            }

            foreach (var method in AccessTools.GetDeclaredMethods(zInput))
            {
                if (method.ReturnType != typeof(float))
                {
                    continue;
                }

                if (method.Name.IndexOf("Scroll", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    method.Name == "GetAxis" ||
                    method.Name == "GetAxisRaw")
                {
                    yield return method;
                }
            }
        }

        private static void Postfix(MethodBase __originalMethod, object[] __args, ref float __result)
        {
            if (!RaceGui.IsOpen || __originalMethod == null)
            {
                return;
            }

            if (__originalMethod.Name.IndexOf("Scroll", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                __result = 0f;
                return;
            }

            if ((__originalMethod.Name == "GetAxis" || __originalMethod.Name == "GetAxisRaw") &&
                __args != null &&
                __args.Length > 0 &&
                __args[0] is string axisName &&
                axisName.IndexOf("Scroll", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                __result = 0f;
            }
        }
    }

    /// <summary>
    /// Clears Unity input axes before the camera reads them (F6 open only).
    /// Skipped automatically on dedicated servers where GameCamera does not exist.
    /// </summary>
    [HarmonyPatch]
    public static class GameCameraScrollBlockPatch
    {
        private static MethodBase TargetMethod()
        {
            var gameCamera = AccessTools.TypeByName("GameCamera");
            return gameCamera == null ? null : AccessTools.Method(gameCamera, "UpdateCamera");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Prefix()
        {
            if (RaceGui.IsOpen)
            {
                Input.ResetInputAxes();
            }
        }
    }
}
