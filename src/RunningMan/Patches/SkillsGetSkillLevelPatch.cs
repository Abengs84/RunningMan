using HarmonyLib;

namespace RunningMan.Patches
{
    /// <summary>
    /// Forces Run skill reads to the race level while marathon normalization is active.
    /// Covers UI and movement even if m_level is overwritten by sync.
    /// </summary>
    [HarmonyPatch(typeof(Skills), nameof(Skills.GetSkillLevel))]
    public static class SkillsGetSkillLevelPatch
    {
        private static void Postfix(Skills.SkillType skillType, ref float __result)
        {
            if (skillType == Skills.SkillType.Run && RaceSkillNormalizer.IsActive)
            {
                __result = RaceSkillNormalizer.TargetLevel;
            }
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.GetSkillFactor))]
    public static class SkillsGetSkillFactorPatch
    {
        private static void Postfix(Skills __instance, Skills.SkillType skillType, ref float __result)
        {
            if (skillType != Skills.SkillType.Run || !RaceSkillNormalizer.IsActive || __instance == null)
            {
                return;
            }

            // Rebuild factor from the forced race level (Valheim: level / 100).
            __result = RaceSkillNormalizer.TargetLevel / 100f;
        }
    }
}
