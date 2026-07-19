using RunningMan.Net;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Sends the local player's world position to the server while racing so dedicated
    /// servers can detect checkpoints without relying on stale peer.m_refPos.
    /// </summary>
    public sealed class RaceClientPositionReporter : MonoBehaviour
    {
        private float _nextSendTime;

        private void Update()
        {
            if (Player.m_localPlayer == null || ZNet.instance == null || ZNet.instance.IsServer())
            {
                return;
            }

            if (Time.time < _nextSendTime)
            {
                return;
            }

            var state = RaceNetSync.ClientState;
            if (state == null || state.Phase != (int)RaceEventPhase.Racing)
            {
                _nextSendTime = Time.time + 0.5f;
                return;
            }

            var local = RaceNetSync.GetLocalRunner();
            if (local == null || local.StartUtcTicks <= 0 || local.Finished || local.Disqualified)
            {
                _nextSendTime = Time.time + 0.5f;
                return;
            }

            _nextSendTime = Time.time + Mathf.Clamp(ModConfig.UpdateInterval.Value, 0.05f, 0.2f);
            var position = Player.m_localPlayer.transform.position;
            RaceNetSync.SendRunnerPosition(Player.m_localPlayer.GetPlayerID(), position);
        }
    }
}
