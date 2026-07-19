using RunningMan.Net;
using UnityEngine;

namespace RunningMan.Patches
{
    /// <summary>
    /// Detects illegal food/mead use during an active race (client + server).
    /// Dedicated servers never see remote Player UseItem, so clients report via RPC.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Humanoid), "UseItem")]
    public static class HumanoidUseItemPatch
    {
        private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, bool fromInventoryGui)
        {
            _ = fromInventoryGui;
            if (!ModConfig.EnableGearCheck.Value || __instance == null || item == null)
            {
                return;
            }

            if (!(__instance is Player player))
            {
                return;
            }

            if (!GearValidator.IsConsumableItem(item))
            {
                return;
            }

            if (GearValidator.IsAllowedRaceConsumable(item))
            {
                return;
            }

            var label = GearValidator.GetPrefabName(item);
            if (string.IsNullOrEmpty(label))
            {
                label = GearValidator.IsFoodItem(item) ? "food" : "consumable";
            }

            var reason = GearValidator.IsFoodItem(item)
                ? $"illegal food: {label}"
                : $"illegal consumable: {label}";

            if (ValheimUtil.IsServerAuthority() && RaceManager.Instance != null)
            {
                RaceManager.Instance.TryFlagIllegalConsumable(player, item);
                return;
            }

            // Dedicated-server client: only the local player can report their own eat.
            if (player != Player.m_localPlayer)
            {
                return;
            }

            var state = RaceNetSync.ClientState;
            if (state == null || state.Phase != (int)RaceEventPhase.Racing)
            {
                return;
            }

            var local = RaceNetSync.GetLocalRunner();
            if (local == null || local.StartUtcTicks <= 0 || local.Finished || local.Disqualified)
            {
                return;
            }

            local.Disqualified = true;
            local.DisqualifiedReason = reason;
            RaceGui.ShowYellowHud($"DISQUALIFIED: {reason}");
            RaceGui.ShowInfoPanel("Disqualified", reason, 12f);
            RaceNetSync.SendGearViolation(reason);
        }
    }
}
