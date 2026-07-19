using System;
using HarmonyLib;
using RunningMan.Net;
using RunningMan.Storage;

namespace RunningMan.Patches
{
    /// <summary>
    /// Sends track and race state to a client when it finishes connecting.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    public static class ZNetPeerInfoPatch
    {
        private static void Postfix(ZRpc rpc)
        {
            if (!ValheimUtil.IsServerAuthority() || RaceManager.Instance == null || rpc == null)
            {
                return;
            }

            var uid = ZNet.instance?.GetPeer(rpc)?.m_uid ?? 0;
            if (uid == 0)
            {
                return;
            }

            RaceNetSync.SendTrackToPeer(uid, JsonStorage.Track);
            RaceNetSync.SendBulletinsToPeer(uid);
            RaceNetSync.SendStateToPeer(uid, RaceManager.Instance.BuildClientSnapshot(DateTime.UtcNow));
            RaceNetSync.SendAdminStatusToPeer(uid);
        }
    }
}
