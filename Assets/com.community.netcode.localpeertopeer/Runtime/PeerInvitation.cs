using System;

namespace Netcode.LocalPeerToPeer
{
    public interface PeerInvitation : PeerInfo
    {
        /// <summary>
        /// The peer's current mode
        /// </summary>
        PeerMode Mode { get; }

        /// <summary>
        /// The number of peers connected to the peer, directly or via relay
        /// </summary>
        byte PeerCount { get; }

        /// <summary>
        /// The PeerID of the peer's server or <c>default(Guid)</c> if the peer
        /// doesn't have a server
        /// </summary>
        public Guid ServerPeerID { get; }

        /// <summary>
        /// Whether the peer has (or is) a server
        /// </summary>
        public bool HasServer => ServerPeerID != default;
    }
}
