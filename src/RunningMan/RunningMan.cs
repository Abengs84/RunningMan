using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RunningMan.Patches;
using RunningMan.Storage;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// BepInEx entry point for the RunningMan marathon tracker.
    /// Server-authoritative: race logic runs only on the dedicated server or host.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class RunningManPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.runningman.valheim";
        public const string PluginName = "RunningMan";
        public const string PluginVersion = "1.5.9";

        internal static RunningManPlugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Initialize(Config);
            JsonStorage.Initialize(Paths.ConfigPath);

            _harmony = Harmony.CreateAndPatchAll(typeof(GameStartPatch).Assembly, PluginGuid);
            Commands.Register();
            RaceManager.Initialize(this);
            gameObject.AddComponent<RaceGui>();
            gameObject.AddComponent<RaceDebugDrawer>();
            gameObject.AddComponent<RaceGateEditor>();
            gameObject.AddComponent<RaceSkillNormalizer>();
            gameObject.AddComponent<RaceClientGearMonitor>();
            gameObject.AddComponent<RaceClientPositionReporter>();
            gameObject.AddComponent<RaceWrBoardHud>();
            var soundMonitor = gameObject.AddComponent<RaceSoundMonitor>();
            RaceSoundPlayer.Initialize(soundMonitor);

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
