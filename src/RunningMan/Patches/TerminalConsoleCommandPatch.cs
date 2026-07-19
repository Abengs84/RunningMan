using HarmonyLib;

namespace RunningMan.Patches
{
    /// <summary>
    /// Some server-side command paths do not pass through ZNet.InternalCommand.
    /// Capture the local player's RPC before RunAction when hosting in-game.
    /// </summary>
    [HarmonyPatch(typeof(Terminal.ConsoleCommand), "RunAction")]
    public static class TerminalConsoleCommandPatch
    {
        private static void Prefix(Terminal.ConsoleCommand __instance)
        {
            if (CommandContext.CurrentRpc != null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            if (!__instance.IsNetwork || Player.m_localPlayer == null)
            {
                return;
            }

            var peer = ZNet.instance.GetPeer(Player.m_localPlayer.GetPlayerID());
            if (peer?.m_rpc != null)
            {
                CommandContext.CurrentRpc = peer.m_rpc;
                CommandContext.SenderPeerId = peer.m_uid;
            }
        }
    }
}
