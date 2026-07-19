using HarmonyLib;

namespace RunningMan.Patches
{
    /// <summary>
    /// Captures the invoking player's RPC when chat commands are executed on the server.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "InternalCommand")]
    public static class ZNetInternalCommandPatch
    {
        private static void Prefix(ZRpc rpc)
        {
            CommandContext.CurrentRpc = rpc;
            CommandContext.SenderPeerId = 0;
            if (rpc != null && ZNet.instance != null)
            {
                var peer = ZNet.instance.GetPeer(rpc);
                if (peer != null)
                {
                    CommandContext.SenderPeerId = peer.m_uid;
                }
            }
        }

        private static void Postfix()
        {
            CommandContext.Clear();
        }
    }
}
