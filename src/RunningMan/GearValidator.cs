using System;
using System.Collections.Generic;
using System.Text;
using RunningMan.Net;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Default marathon prefab name constants (also used to seed AllowedGearRules).
    /// </summary>
    public static class GearRules
    {
        public const string HelmetTroll = "HelmetTrollLeather";
        public const string ChestTroll = "ArmorTrollLeatherChest";
        public const string LegsTroll = "ArmorTrollLeatherLegs";
        public const string CapeFeather = "CapeFeather";
        public const string AntiSting = "MeadBugRepellent";
        public const string TonicRatatosk = "MeadHasty";
        public const string Salad = "Salad";
        public const string BloodPudding = "BloodPudding";
        public const string MushroomOmelette = "MushroomOmelette";
    }

    /// <summary>
    /// Validates participant equipment at race start and during the run against AllowedGearRules.
    /// </summary>
    public static class GearValidator
    {
        public sealed class GearCheckResult
        {
            public bool IsValid;
            public readonly List<string> Issues = new List<string>();

            public string Format(bool multiline = true)
            {
                if (IsValid)
                {
                    return "Gear check passed.";
                }

                if (multiline)
                {
                    return "Gear check failed:\n- " + string.Join("\n- ", Issues);
                }

                return string.Join("; ", Issues);
            }
        }

        public static AllowedGearRules GetActiveRules()
        {
            if (ValheimUtil.IsServerAuthority())
            {
                return JsonStorage.AllowedGear ?? AllowedGearRules.CreateDefaults();
            }

            return RaceNetSync.ClientAllowedGear ?? AllowedGearRules.CreateDefaults();
        }

        /// <summary>
        /// Full check before the race starts (armor, cape, consumables).
        /// </summary>
        public static GearCheckResult CheckStartGear(Player player)
        {
            var result = new GearCheckResult { IsValid = true };
            if (player == null || !ModConfig.EnableGearCheck.Value)
            {
                return result;
            }

            var rules = GetActiveRules();
            ValidateArmor(player, rules, result);
            // Hands are not restricted — missclicks (hammer/weapon) must not DQ or block checkpoints.
            ValidateConsumables(player, rules, result, atStart: true);
            result.IsValid = result.Issues.Count == 0;
            return result;
        }

        /// <summary>
        /// Lighter check while running (armor/cape rules; consumables may be used up).
        /// </summary>
        public static GearCheckResult CheckRuntimeGear(Player player, bool ignored = true)
        {
            _ = ignored;
            var result = new GearCheckResult { IsValid = true };
            if (player == null || !ModConfig.EnableGearCheck.Value)
            {
                return result;
            }

            var rules = GetActiveRules();
            ValidateArmor(player, rules, result);
            result.IsValid = result.Issues.Count == 0;
            return result;
        }

        private static void ValidateArmor(Player player, AllowedGearRules rules, GearCheckResult result)
        {
            RequireSlot(player.m_helmetItem, rules.Helmet, "helmet", result);
            RequireSlot(player.m_chestItem, rules.Chest, "chest", result);
            RequireSlot(player.m_legItem, rules.Legs, "legs", result);
            ValidateCape(player.m_shoulderItem, rules.Cape, result);
        }

        private static void ValidateCape(ItemDrop.ItemData cape, string allowedCape, GearCheckResult result)
        {
            if (cape == null)
            {
                return;
            }

            var name = GetPrefabName(cape);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(allowedCape))
            {
                result.Issues.Add($"Disallowed cape: {GetDisplayName(cape)} (no cape allowed).");
                return;
            }

            if (string.Equals(name, allowedCape.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            result.Issues.Add(
                $"Disallowed cape: {GetDisplayName(cape)} (allowed: {allowedCape.Trim()} only, or no cape).");
        }

        private static void RequireSlot(ItemDrop.ItemData item, string requiredPrefab, string slot,
            GearCheckResult result)
        {
            if (string.IsNullOrWhiteSpace(requiredPrefab))
            {
                if (item != null)
                {
                    result.Issues.Add($"Slot {slot} must be empty (have {GetDisplayName(item)}).");
                }

                return;
            }

            if (item == null)
            {
                result.Issues.Add($"Missing required {slot}: {requiredPrefab.Trim()}.");
                return;
            }

            var name = GetPrefabName(item);
            if (!string.Equals(name, requiredPrefab.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(
                    $"Wrong {slot}: {GetDisplayName(item)} (required {requiredPrefab.Trim()}).");
            }
        }

        private static void ValidateConsumables(Player player, AllowedGearRules rules, GearCheckResult result,
            bool atStart)
        {
            if (!atStart)
            {
                return;
            }

            var inventory = player.GetInventory();
            if (inventory == null)
            {
                result.Issues.Add("Could not read inventory.");
                return;
            }

            RequireConsumableCount(inventory, rules.AntiStingPrefab, rules.RequiredAntiSting, "Anti-Sting", result);
            RequireConsumableCount(inventory, rules.RatatoskPrefab, rules.RequiredRatatosk, "Tonic of Ratatosk",
                result);
            RequireConsumableCount(inventory, rules.SaladPrefab, rules.RequiredSalad, "Salad", result);
            RequireConsumableCount(inventory, rules.BloodPuddingPrefab, rules.RequiredBloodPudding, "Blood Pudding",
                result);
            RequireConsumableCount(inventory, rules.MushroomOmelettePrefab, rules.RequiredMushroomOmelette,
                "Mushroom Omelette", result);
        }

        private static void RequireConsumableCount(Inventory inventory, string prefab, int required, string label,
            GearCheckResult result)
        {
            if (required <= 0 || string.IsNullOrWhiteSpace(prefab))
            {
                return;
            }

            var have = CountPrefabInInventory(inventory, prefab.Trim());
            if (have < required)
            {
                result.Issues.Add($"Need {required} {label} ({prefab.Trim()}) (have {have}).");
            }
        }

        private static int CountPrefabInInventory(Inventory inventory, string prefabName)
        {
            if (inventory == null || string.IsNullOrEmpty(prefabName))
            {
                return 0;
            }

            if (ObjectDB.instance != null)
            {
                var go = ObjectDB.instance.GetItemPrefab(prefabName);
                var sharedName = go != null
                    ? go.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name
                    : null;
                if (!string.IsNullOrEmpty(sharedName))
                {
                    return inventory.CountItems(sharedName, -1, true);
                }
            }

            var count = 0;
            foreach (var item in inventory.GetAllItems())
            {
                if (item == null)
                {
                    continue;
                }

                if (string.Equals(GetPrefabName(item), prefabName, StringComparison.OrdinalIgnoreCase))
                {
                    count += Math.Max(1, item.m_stack);
                }
            }

            return count;
        }

        public static string GetPrefabName(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (item.m_dropPrefab != null)
            {
                return item.m_dropPrefab.name;
            }

            return item.m_shared?.m_name ?? string.Empty;
        }

        private static string GetDisplayName(ItemDrop.ItemData item)
        {
            var prefab = GetPrefabName(item);
            if (!string.IsNullOrEmpty(prefab))
            {
                return prefab;
            }

            return item?.m_shared?.m_name ?? "unknown item";
        }

        public static bool IsConsumableItem(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
            {
                return false;
            }

            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
            {
                return true;
            }

            if (item.m_shared.m_food > 0.01f)
            {
                return true;
            }

            return item.m_shared.m_consumeStatusEffect != null;
        }

        public static bool IsAllowedRaceConsumable(ItemDrop.ItemData item)
        {
            if (!IsConsumableItem(item))
            {
                return true;
            }

            var rules = GetActiveRules();
            var name = GetPrefabName(item);
            return IsAllowedRaceConsumablePrefab(name, rules);
        }

        public static bool IsAllowedRaceConsumablePrefab(string prefabName, AllowedGearRules rules = null)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                return false;
            }

            rules ??= GetActiveRules();
            return string.Equals(prefabName, rules.AntiStingPrefab, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(prefabName, rules.RatatoskPrefab, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(prefabName, rules.SaladPrefab, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(prefabName, rules.BloodPuddingPrefab, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(prefabName, rules.MushroomOmelettePrefab, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFoodItem(ItemDrop.ItemData item)
        {
            return item?.m_shared != null && item.m_shared.m_food > 0.01f;
        }

        public static string FormatRequiredLoadout()
        {
            return GetActiveRules().Format();
        }

        public static AllowedGearRules CaptureFromPlayer(Player player)
        {
            var rules = GetActiveRules().Clone();
            if (player == null)
            {
                return rules;
            }

            rules.Helmet = GetPrefabName(player.m_helmetItem);
            rules.Chest = GetPrefabName(player.m_chestItem);
            rules.Legs = GetPrefabName(player.m_legItem);
            rules.Cape = GetPrefabName(player.m_shoulderItem);

            var handNames = new List<string>();
            AddUniqueHandPrefab(handNames, GetPrefabName(player.m_rightItem));
            AddUniqueHandPrefab(handNames, GetPrefabName(player.m_leftItem));
            rules.AllowedHandItems = string.Join(",", handNames);

            var inventory = player.GetInventory();
            if (inventory != null)
            {
                if (!string.IsNullOrWhiteSpace(rules.AntiStingPrefab))
                {
                    rules.RequiredAntiSting = CountPrefabInInventory(inventory, rules.AntiStingPrefab);
                }

                if (!string.IsNullOrWhiteSpace(rules.RatatoskPrefab))
                {
                    rules.RequiredRatatosk = CountPrefabInInventory(inventory, rules.RatatoskPrefab);
                }

                if (!string.IsNullOrWhiteSpace(rules.SaladPrefab))
                {
                    rules.RequiredSalad = CountPrefabInInventory(inventory, rules.SaladPrefab);
                }

                if (!string.IsNullOrWhiteSpace(rules.BloodPuddingPrefab))
                {
                    rules.RequiredBloodPudding = CountPrefabInInventory(inventory, rules.BloodPuddingPrefab);
                }

                if (!string.IsNullOrWhiteSpace(rules.MushroomOmelettePrefab))
                {
                    rules.RequiredMushroomOmelette = CountPrefabInInventory(inventory, rules.MushroomOmelettePrefab);
                }
            }

            return rules;
        }

        private static void AddUniqueHandPrefab(List<string> names, string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
            {
                return;
            }

            foreach (var existing in names)
            {
                if (string.Equals(existing, prefab, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            names.Add(prefab);
        }
    }
}
