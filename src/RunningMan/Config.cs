using BepInEx.Configuration;
using UnityEngine;

namespace RunningMan
{
    public static class ModConfig
    {
        public static ConfigEntry<bool> EnableBroadcasts { get; private set; }
        public static ConfigEntry<bool> EnableHud { get; private set; }
        public static ConfigEntry<bool> DebugMode { get; private set; }
        public static ConfigEntry<KeyCode> GuiHotkey { get; private set; }
        public static ConfigEntry<int> CountdownSeconds { get; private set; }
        public static ConfigEntry<bool> RequireRegistration { get; private set; }
        public static ConfigEntry<float> StartTriggerDistance { get; private set; }
        public static ConfigEntry<float> CheckpointDistance { get; private set; }
        public static ConfigEntry<float> GateEndPadding { get; private set; }
        public static ConfigEntry<float> CheckpointVerticalDistance { get; private set; }
        public static ConfigEntry<float> GateVerticalDistance { get; private set; }
        public static ConfigEntry<float> FinishTriggerDistance { get; private set; }
        public static ConfigEntry<bool> EnableLiveStandings { get; private set; }
        public static ConfigEntry<int> LiveStandingsLimit { get; private set; }
        public static ConfigEntry<float> UpdateInterval { get; private set; }
        public static ConfigEntry<float> GateRegistrationWidth { get; private set; }
        public static ConfigEntry<int> CountdownFontSize { get; private set; }
        public static ConfigEntry<int> CheckpointHudFontSize { get; private set; }
        public static ConfigEntry<string> SavePath { get; private set; }
        public static ConfigEntry<string> ExportFormat { get; private set; }
        public static ConfigEntry<int> LeaderboardLimit { get; private set; }
        public static ConfigEntry<int> WorldRecordsLimit { get; private set; }
        public static ConfigEntry<bool> EnableRaceSounds { get; private set; }
        public static ConfigEntry<float> RaceSoundVolume { get; private set; }
        public static ConfigEntry<string> RaceStartSounds { get; private set; }
        public static ConfigEntry<string> CheckpointSound { get; private set; }
        public static ConfigEntry<string> FirstPlaceFinishSounds { get; private set; }
        public static ConfigEntry<bool> UseCustomRaceSounds { get; private set; }
        public static ConfigEntry<string> CustomSoundsFolder { get; private set; }
        public static ConfigEntry<string> CustomStartSoundFile { get; private set; }
        public static ConfigEntry<string> CustomCheckpointSoundFile { get; private set; }
        public static ConfigEntry<string> CustomFinishSoundFile { get; private set; }
        public static ConfigEntry<string> CustomCountdownSoundFile { get; private set; }
        public static ConfigEntry<string> CustomFalseStartSoundFile { get; private set; }
        public static ConfigEntry<float> StartingAreaOffset { get; private set; }
        public static ConfigEntry<float> StartingAreaDepth { get; private set; }
        public static ConfigEntry<float> StartingAreaSidePadding { get; private set; }
        public static ConfigEntry<bool> RequireStartingArea { get; private set; }
        public static ConfigEntry<bool> DisqualifyOnFalseStart { get; private set; }
        public static ConfigEntry<float> AutoDetectTorchPairMinDistance { get; private set; }
        public static ConfigEntry<float> AutoDetectTorchPairMaxDistance { get; private set; }
        public static ConfigEntry<float> AutoDetectSearchRadius { get; private set; }
        public static ConfigEntry<float> AutoDetectNearestSearchRadius { get; private set; }
        public static ConfigEntry<float> EndpointSnapRadius { get; private set; }
        public static ConfigEntry<float> EndpointSnapAngle { get; private set; }
        public static ConfigEntry<KeyCode> RegisterCheckpointHotkey { get; private set; }
        public static ConfigEntry<KeyCode> AutoDetectNearestCheckpointHotkey { get; private set; }
        public static ConfigEntry<bool> NormalizeRunSkill { get; private set; }
        public static ConfigEntry<float> NormalizedRunSkillLevel { get; private set; }
        public static ConfigEntry<bool> EnableGearCheck { get; private set; }
        public static ConfigEntry<bool> DisqualifyOnGearViolation { get; private set; }
        public static ConfigEntry<int> RequiredAntiStingCount { get; private set; }
        public static ConfigEntry<int> RequiredRatatoskCount { get; private set; }
        public static ConfigEntry<float> TriggerSweepStepDistance { get; private set; }
        public static ConfigEntry<string> AllowedHandItems { get; private set; }
        public static ConfigEntry<bool> MountainBiomeUnlocksFeatherCape { get; private set; }

        public static ConfigFile ConfigFile { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            ConfigFile = config;
            EnableBroadcasts = config.Bind("General", "EnableBroadcasts", true,
                "Broadcast race events to all players in chat.");
            EnableHud = config.Bind("General", "EnableHud", true,
                "Show live race overlay with time and place.");
            EnableLiveStandings = config.Bind("General", "EnableLiveStandings", true,
                "Show a live ranking panel during races.");
            EnableRaceSounds = config.Bind("Audio", "EnableRaceSounds", true,
                "Play local sounds for race start, checkpoints, and 1st-place finishes.");
            RaceSoundVolume = config.Bind("Audio", "RaceSoundVolume", 1f,
                "Volume multiplier for RunningMan race sounds.");
            RaceStartSounds = config.Bind("Audio", "RaceStartSounds", "sfx_offering,sfx_demister_start",
                "Comma-separated Valheim SFX prefab names played when the race starts (GO!).");
            CheckpointSound = config.Bind("Audio", "CheckpointSound", "sfx_archery_target_hit",
                "Valheim SFX prefab name played when you reach a checkpoint.");
            FirstPlaceFinishSounds = config.Bind("Audio", "FirstPlaceFinishSounds",
                "sfx_coins_placed,sfx_coins_placed,sfx_boar_love",
                "Comma-separated Valheim SFX prefab names played when you finish in 1st place (fallback).");
            UseCustomRaceSounds = config.Bind("Audio", "UseCustomRaceSounds", true,
                "Prefer custom audio files from the plugin Sounds folder when present.");
            CustomSoundsFolder = config.Bind("Audio", "CustomSoundsFolder", "Sounds",
                "Subfolder next to RunningMan.dll (…/BepInEx/plugins/RunningMan/Sounds).");
            CustomStartSoundFile = config.Bind("Audio", "CustomStartSoundFile", "start.mp3",
                "Race start sound file name (mp3, wav, or ogg).");
            CustomCheckpointSoundFile = config.Bind("Audio", "CustomCheckpointSoundFile", "checkpoint.mp3",
                "Checkpoint sound file name (mp3, wav, or ogg).");
            CustomFinishSoundFile = config.Bind("Audio", "CustomFinishSoundFile", "finish.mp3",
                "1st-place finish sound file name (mp3, wav, or ogg).");
            CustomCountdownSoundFile = config.Bind("Audio", "CustomCountdownSoundFile", "countdown.mp3",
                "Countdown voice file played when an admin starts the race (e.g. 5-4-3-2-1).");
            CustomFalseStartSoundFile = config.Bind("Audio", "CustomFalseStartSoundFile", "false_start.mp3",
                "Played only for a player who false-starts (stops their countdown audio).");
            LiveStandingsLimit = config.Bind("General", "LiveStandingsLimit", 6,
                "Maximum runners shown in the live ranking panel.");
            DebugMode = config.Bind("General", "DebugMode", false,
                "Show start/finish/checkpoint gate markers in the world (synced from server).");
            GuiHotkey = config.Bind("General", "GuiHotkey", KeyCode.F6,
                "Hotkey to open the RunningMan panel.");
            RegisterCheckpointHotkey = config.Bind("General", "RegisterCheckpointHotkey", KeyCode.F7,
                "Hotkey to register a checkpoint at your position (admin). None disables.");
            AutoDetectNearestCheckpointHotkey = config.Bind("General", "AutoDetectNearestCheckpointHotkey", KeyCode.F8,
                "Hotkey to auto-detect 1 checkpoint from the two nearest Standing Iron Torches (admin). None disables.");
            NormalizeRunSkill = config.Bind("General", "NormalizeRunSkill", true,
                "Clamp every runner's Run skill to NormalizedRunSkillLevel during a race.");
            NormalizedRunSkillLevel = config.Bind("General", "NormalizedRunSkillLevel", 50f,
                "Run skill level used for all participants while a race is active (0-100).");
            CountdownSeconds = config.Bind("General", "CountdownSeconds", 5,
                "Seconds counted down before a registered race starts.");
            RequireRegistration = config.Bind("General", "RequireRegistration", true,
                "Only registered participants can race after the countdown.");

            StartTriggerDistance = config.Bind("Detection", "StartTriggerDistance", 6f,
                "Maximum horizontal distance from the start gate line to trigger a start.");
            RequireStartingArea = config.Bind("Detection", "RequireStartingArea", true,
                "Block countdown unless every registered runner is inside the starting grid.");
            StartingAreaOffset = config.Bind("Detection", "StartingAreaOffset", 1f,
                "How far before the start line (meters) the starting grid is centered.");
            StartingAreaDepth = config.Bind("Detection", "StartingAreaDepth", 4f,
                "Depth of the starting grid along the approach (meters).");
            StartingAreaSidePadding = config.Bind("Detection", "StartingAreaSidePadding", 1.5f,
                "Extra width beyond the start gate endpoints for the starting grid.");
            DisqualifyOnFalseStart = config.Bind("Detection", "DisqualifyOnFalseStart", true,
                "Remove a runner from the event if they cross the start line during countdown.");
            CheckpointDistance = config.Bind("Detection", "CheckpointDistance", 2.5f,
                "Maximum horizontal distance from a checkpoint gate line to register passage.");
            GateEndPadding = config.Bind("Detection", "GateEndPadding", 0f,
                "How far past gate endpoints (meters) still counts as inside. 0 = must pass between A and B.");
            CheckpointVerticalDistance = config.Bind("Detection", "CheckpointVerticalDistance", 15f,
                "Maximum vertical distance above/below a checkpoint gate line to register passage.");
            GateVerticalDistance = config.Bind("Detection", "GateVerticalDistance", 8f,
                "Maximum vertical distance above/below start/finish gate lines.");
            FinishTriggerDistance = config.Bind("Detection", "FinishTriggerDistance", 8f,
                "Maximum horizontal distance from the finish gate line to trigger completion.");
            UpdateInterval = config.Bind("Detection", "UpdateInterval", 0.05f,
                "Seconds between race position checks (lower = better for fast runners).");
            TriggerSweepStepDistance = config.Bind("Detection", "TriggerSweepStepDistance", 1.5f,
                "Sub-step size (meters) when checking movement between ticks so fast runners do not skip gates.");
            GateRegistrationWidth = config.Bind("Detection", "GateRegistrationWidth", 6f,
                "Default gate width when registering from player position.");
            CountdownFontSize = config.Bind("General", "CountdownFontSize", 72,
                "Font size for the on-screen race countdown.");
            CheckpointHudFontSize = config.Bind("General", "CheckpointHudFontSize", 11,
                "Font size for the checkpoint progress line on the race HUD.");

            SavePath = config.Bind("Storage", "SavePath", "RunningMan/",
                "Subfolder under BepInEx/config/ for race JSON files.");
            ExportFormat = config.Bind("Storage", "ExportFormat", "json",
                "Export format identifier.");
            LeaderboardLimit = config.Bind("Storage", "LeaderboardLimit", 10,
                "Number of entries shown in /run worldrecords.");
            WorldRecordsLimit = config.Bind("Storage", "WorldRecordsLimit", 5,
                "Number of world record entries shown on the HUD and WR bulletin Signs (default 5).");

            AutoDetectTorchPairMinDistance = config.Bind("AutoDetect", "TorchPairMinDistance", 3f,
                "Minimum spacing between paired Standing Iron Torches.");
            AutoDetectTorchPairMaxDistance = config.Bind("AutoDetect", "TorchPairMaxDistance", 25f,
                "Maximum spacing between paired Standing Iron Torches.");
            AutoDetectSearchRadius = config.Bind("AutoDetect", "SearchRadius", 500f,
                "Search radius for auto-detection commands.");
            AutoDetectNearestSearchRadius = config.Bind("AutoDetect", "NearestSearchRadius", 40f,
                "Search radius when auto-detecting a single checkpoint from the two nearest torches.");
            EndpointSnapRadius = config.Bind("AutoDetect", "EndpointSnapRadius", 14f,
                "How far endpoint edit mode looks for snap targets (Standing Iron Torches).");
            EndpointSnapAngle = config.Bind("AutoDetect", "EndpointSnapAngle", 22f,
                "Max look angle (degrees) for endpoint snap-to-torch.");

            EnableGearCheck = config.Bind("GearCheck", "EnableGearCheck", true,
                "Validate marathon loadout at start and during the race.");
            DisqualifyOnGearViolation = config.Bind("GearCheck", "DisqualifyOnViolation", true,
                "Remove runners who equip non-approved gear mid-race.");
            RequiredAntiStingCount = config.Bind("GearCheck", "RequiredAntiStingCount", 1,
                "Anti-Sting Concoctions (MeadBugRepellent) required in inventory at start.");
            RequiredRatatoskCount = config.Bind("GearCheck", "RequiredRatatoskCount", 2,
                "Tonic of Ratatosk (MeadHasty) required in inventory at start.");
            AllowedHandItems = config.Bind("GearCheck", "AllowedHandItems", "",
                "Unused (kept for compatibility). Hand items are not restricted during races.");
            MountainBiomeUnlocksFeatherCape = config.Bind("GearCheck", "MountainBiomeUnlocksFeatherCape", false,
                "Unused (kept for config compatibility). Feather Cape is always allowed; Troll Hide is not.");
        }
    }
}
