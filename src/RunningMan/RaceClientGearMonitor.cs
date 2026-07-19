using System;
using RunningMan.Net;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Client-side mid-race gear checks. Dedicated servers never have remote Player inventory,
    /// so each client validates its own loadout and reports violations to the server.
    /// </summary>
    public sealed class RaceClientGearMonitor : MonoBehaviour
    {
        private float _nextCheckTime;
        private bool _reportedViolation;

        private void Update()
        {
            if (!ModConfig.EnableGearCheck.Value || Player.m_localPlayer == null)
            {
                return;
            }

            if (Time.time < _nextCheckTime)
            {
                return;
            }

            _nextCheckTime = Time.time + Mathf.Max(0.25f, ModConfig.UpdateInterval.Value);

            var local = RaceNetSync.GetLocalRunner();
            if (local == null || local.StartUtcTicks <= 0 || local.Finished || local.Disqualified)
            {
                _reportedViolation = false;
                return;
            }

            if (RaceNetSync.ClientState == null ||
                RaceNetSync.ClientState.Phase != (int)RaceEventPhase.Racing)
            {
                return;
            }

            if (_reportedViolation)
            {
                return;
            }

            var gear = GearValidator.CheckRuntimeGear(Player.m_localPlayer);
            if (gear.IsValid)
            {
                return;
            }

            _reportedViolation = true;
            var reason = gear.Issues.Count > 0 ? gear.Issues[0] : "illegal gear change";

            // Freeze local HUD immediately; server confirmation follows via state sync.
            local.Disqualified = true;
            local.DisqualifiedReason = reason;
            if (local.FinishTimeMs <= 0)
            {
                local.FinishTimeMs = (long)(DateTime.UtcNow -
                    new DateTime(local.StartUtcTicks, DateTimeKind.Utc)).TotalMilliseconds;
            }

            RaceGui.ShowYellowHud($"DISQUALIFIED: {reason}");
            RaceGui.ShowInfoPanel("Disqualified", reason, 12f);
            RaceNetSync.SendGearViolation(reason);
        }
    }
}
