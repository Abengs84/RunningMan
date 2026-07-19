using HarmonyLib;

namespace RunningMan.Patches
{
    /// <summary>
    /// Prevents Run skill XP gain while marathon normalization is active.
    /// </summary>
    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    public static class SkillsRaiseSkillPatch
    {
        private static void Prefix(Skills __instance, Skills.SkillType skillType, ref float factor)
        {
            if (skillType != Skills.SkillType.Run || factor <= 0f)
            {
                return;
            }

            if (__instance?.m_player != null && RaceSkillNormalizer.ShouldBlockRunSkillXp(__instance.m_player))
            {
                factor = 0f;
            }
        }
    }
}
