using System;
using System.Text;

namespace RunningMan.Storage
{
    /// <summary>
    /// Server-persisted, client-synced marathon gear rules editable from F6 admin.
    /// </summary>
    [Serializable]
    public sealed class AllowedGearRules
    {
        public string Helmet = GearRules.HelmetTroll;
        public string Chest = GearRules.ChestTroll;
        public string Legs = GearRules.LegsTroll;
        /// <summary>Allowed cape prefab. Empty = no cape allowed. Non-empty = that cape or no cape.</summary>
        public string Cape = GearRules.CapeFeather;
        public string AllowedHandItems = string.Empty;
        public string AntiStingPrefab = GearRules.AntiSting;
        public string RatatoskPrefab = GearRules.TonicRatatosk;
        public int RequiredAntiSting = 1;
        public int RequiredRatatosk = 2;

        public string SaladPrefab = GearRules.Salad;
        public string BloodPuddingPrefab = GearRules.BloodPudding;
        public string MushroomOmelettePrefab = GearRules.MushroomOmelette;
        public int RequiredSalad = 2;
        public int RequiredBloodPudding = 2;
        public int RequiredMushroomOmelette = 2;

        public static AllowedGearRules CreateDefaults()
        {
            return new AllowedGearRules
            {
                Helmet = GearRules.HelmetTroll,
                Chest = GearRules.ChestTroll,
                Legs = GearRules.LegsTroll,
                Cape = GearRules.CapeFeather,
                AllowedHandItems = ModConfig.AllowedHandItems?.Value ?? string.Empty,
                AntiStingPrefab = GearRules.AntiSting,
                RatatoskPrefab = GearRules.TonicRatatosk,
                RequiredAntiSting = ModConfig.RequiredAntiStingCount?.Value ?? 1,
                RequiredRatatosk = ModConfig.RequiredRatatoskCount?.Value ?? 2,
                SaladPrefab = GearRules.Salad,
                BloodPuddingPrefab = GearRules.BloodPudding,
                MushroomOmelettePrefab = GearRules.MushroomOmelette,
                RequiredSalad = 2,
                RequiredBloodPudding = 2,
                RequiredMushroomOmelette = 2
            };
        }

        public AllowedGearRules Clone()
        {
            return new AllowedGearRules
            {
                Helmet = Helmet ?? string.Empty,
                Chest = Chest ?? string.Empty,
                Legs = Legs ?? string.Empty,
                Cape = Cape ?? string.Empty,
                AllowedHandItems = AllowedHandItems ?? string.Empty,
                AntiStingPrefab = AntiStingPrefab ?? string.Empty,
                RatatoskPrefab = RatatoskPrefab ?? string.Empty,
                RequiredAntiSting = RequiredAntiSting,
                RequiredRatatosk = RequiredRatatosk,
                SaladPrefab = SaladPrefab ?? string.Empty,
                BloodPuddingPrefab = BloodPuddingPrefab ?? string.Empty,
                MushroomOmelettePrefab = MushroomOmelettePrefab ?? string.Empty,
                RequiredSalad = RequiredSalad,
                RequiredBloodPudding = RequiredBloodPudding,
                RequiredMushroomOmelette = RequiredMushroomOmelette
            };
        }

        public string Format()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Allowed gear:");
            sb.AppendLine($"  Helmet: {DisplaySlot(Helmet)}");
            sb.AppendLine($"  Chest: {DisplaySlot(Chest)}");
            sb.AppendLine($"  Legs: {DisplaySlot(Legs)}");
            sb.AppendLine($"  Cape: {(string.IsNullOrWhiteSpace(Cape) ? "(none allowed)" : Cape + " (or no cape)")}");
            sb.AppendLine($"  Hands: any (not checked)");
            sb.AppendLine($"  Meads: {RequiredAntiSting}x {AntiStingPrefab}, {RequiredRatatosk}x {RatatoskPrefab}");
            sb.AppendLine(
                $"  Food: {RequiredSalad}x {SaladPrefab}, {RequiredBloodPudding}x {BloodPuddingPrefab}, {RequiredMushroomOmelette}x {MushroomOmelettePrefab}");
            return sb.ToString().TrimEnd();
        }

        private static string DisplaySlot(string prefab)
        {
            return string.IsNullOrWhiteSpace(prefab) ? "(must be empty)" : prefab;
        }
    }
}
