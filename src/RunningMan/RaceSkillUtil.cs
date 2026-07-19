using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Saves and restores Run skill levels for fair marathon pacing.
    /// </summary>
    public static class RaceSkillUtil
    {
        public static bool TrySaveAndSetRunLevel(Player player, RaceSession session, float targetLevel)
        {
            if (player == null || session == null || !ModConfig.NormalizeRunSkill.Value)
            {
                return false;
            }

            var run = GetRunSkill(player);
            if (run == null)
            {
                return false;
            }

            if (!session.RunSkillNormalized)
            {
                session.SavedRunSkillLevel = run.m_level;
                session.RunSkillNormalized = true;
            }

            run.m_level = Mathf.Clamp(targetLevel, 0f, 100f);
            return true;
        }

        public static void EnforceRunLevel(Player player, float targetLevel)
        {
            if (player == null || !ModConfig.NormalizeRunSkill.Value)
            {
                return;
            }

            var run = GetRunSkill(player);
            if (run == null)
            {
                return;
            }

            var clamped = Mathf.Clamp(targetLevel, 0f, 100f);
            if (Mathf.Abs(run.m_level - clamped) > 0.05f)
            {
                run.m_level = clamped;
            }
        }

        public static void RestoreRunLevel(Player player, RaceSession session)
        {
            if (player == null || session == null || !session.RunSkillNormalized ||
                !session.SavedRunSkillLevel.HasValue)
            {
                return;
            }

            var run = GetRunSkill(player);
            if (run != null)
            {
                run.m_level = session.SavedRunSkillLevel.Value;
            }

            session.RunSkillNormalized = false;
            session.SavedRunSkillLevel = null;
        }

        private static Skills.Skill GetRunSkill(Player player)
        {
            var skills = player?.GetSkills();
            if (skills == null)
            {
                return null;
            }

            return skills.GetSkill(Skills.SkillType.Run);
        }
    }
}
