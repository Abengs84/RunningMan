using System.Collections.Generic;
using UnityEngine;

namespace RunningMan
{
    /// <summary>
    /// Tracks the invoking player while a /run command executes on the server.
    /// </summary>
    public static class CommandContext
    {
        [System.ThreadStatic]
        private static ZRpc _currentRpc;

        private static readonly List<string> PendingReplies = new List<string>();
        /// <summary>Peer uid of the player running the command (stable across RPC handler threads).</summary>
        public static long SenderPeerId { get; set; }

        /// <summary>True while handling RunningMan_RunCommand on the server.</summary>
        public static bool ExecutingFromRemote { get; set; }

        /// <summary>Client-reported position for register/autodetect when the server has no Player instance.</summary>
        public static bool HasOrigin { get; private set; }

        public static Vector3 OriginPosition { get; private set; }

        public static Vector3 OriginForward { get; private set; }

        public static void SetOrigin(Vector3 position, Vector3 forward)
        {
            HasOrigin = true;
            OriginPosition = position;
            OriginForward = forward;
        }

        public static bool TryGetOrigin(out Vector3 position, out Vector3 forward)
        {
            position = OriginPosition;
            forward = OriginForward;
            return HasOrigin;
        }

        public static void ClearOrigin()
        {
            HasOrigin = false;
            OriginPosition = Vector3.zero;
            OriginForward = Vector3.forward;
        }

        public static ZRpc CurrentRpc
        {
            get => _currentRpc;
            set => _currentRpc = value;
        }

        public static void Clear()
        {
            _currentRpc = null;
            SenderPeerId = 0;
            ClearOrigin();
            PendingReplies.Clear();
        }

        public static void QueueReply(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                PendingReplies.Add(message);
            }
        }

        public static IReadOnlyList<string> TakePendingReplies()
        {
            var replies = PendingReplies.ToArray();
            PendingReplies.Clear();
            return replies;
        }
    }
}