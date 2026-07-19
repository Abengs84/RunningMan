using RunningMan.Net;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// On each client, clamps local Run skill during an active marathon run.
    /// </summary>
    public sealed class RaceSkillNormalizer : MonoBehaviour
    {
        private float _savedRunLevel = -1f;
        private bool _normalized;

        public static RaceSkillNormalizer Instance { get; private set; }

        public static bool IsActive =>
            Instance != null && Instance._normalized && ModConfig.NormalizeRunSkill.Value;

        public static float TargetLevel =>
            Mathf.Clamp(ModConfig.NormalizedRunSkillLevel.Value, 0f, 100f);

        public static bool ShouldBlockRunSkillXp(Player player)
        {
            if (player == null || player != Player.m_localPlayer || !ModConfig.NormalizeRunSkill.Value)
            {
                return false;
            }

            return IsActive;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null || !ModConfig.NormalizeRunSkill.Value)
            {
                RestoreIfNeeded(null);
                return;
            }

            if (!ShouldNormalizeLocal())
            {
                RestoreIfNeeded(player);
                return;
            }

            ApplyNormalizedRun(player);
        }

        private static bool ShouldNormalizeLocal()
        {
            var state = RaceNetSync.ClientState;
            if (state == null)
            {
                return false;
            }

            // From GO! onward: active runner, or still registered waiting to cross start.
            if (state.Phase == (int)RaceEventPhase.Racing)
            {
                var runner = RaceNetSync.GetLocalRunner();
                if (runner != null && runner.StartUtcTicks > 0 && !runner.Disqualified)
                {
                    return true;
                }

                return RaceNetSync.IsLocalRegistered();
            }

            return false;
        }

        private void ApplyNormalizedRun(Player player)
        {
            var run = player.GetSkills()?.GetSkill(Skills.SkillType.Run);
            if (run == null)
            {
                return;
            }

            if (!_normalized)
            {
                _savedRunLevel = run.m_level;
                _normalized = true;
                RunningManPlugin.Log.LogInfo(
                    $"Run skill normalized: {_savedRunLevel:0.#} → {TargetLevel:0.#}");
            }

            run.m_level = TargetLevel;
            run.m_accumulator = 0f;
        }

        private void RestoreIfNeeded(Player player)
        {
            if (!_normalized || _savedRunLevel < 0f)
            {
                return;
            }

            player ??= Player.m_localPlayer;
            var run = player?.GetSkills()?.GetSkill(Skills.SkillType.Run);
            if (run != null)
            {
                run.m_level = _savedRunLevel;
                RunningManPlugin.Log.LogInfo($"Run skill restored to {_savedRunLevel:0.#}");
            }

            _normalized = false;
            _savedRunLevel = -1f;
        }
    }
}
