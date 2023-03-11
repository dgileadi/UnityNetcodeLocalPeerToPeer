using System;
using UnityEngine;

namespace Netcode.LocalPeerToPeer
{
    public enum PeerMode : byte
    {
        /// <summary>
        /// A peer that can become either a server or a client
        /// </summary>
        PeerToPeer,

        /// <summary>
        /// A server to clients
        /// </summary>
        Server,

        /// <summary>
        /// A client of a server
        /// </summary>
        Client,
    }

    public interface PeerInfo
    {
        /// <summary>
        /// The ID of this peer
        /// </summary>
        Guid PeerID { get; }

        /// <summary>
        /// This peer's display name
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// The mode this peer started
        /// </summary>
        PeerMode StartMode { get; }

        /// <summary>
        /// The platform like iOS or Android this peer is running on
        /// </summary>
        RuntimePlatform Platform { get; }
    }
}
