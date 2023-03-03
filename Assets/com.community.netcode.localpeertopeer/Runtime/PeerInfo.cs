using System;

namespace Netcode.LocalPeerToPeer
{
    public enum PeerMode : byte
    {
        Undetermined,
        Server,
        Client,
    }

    public interface PeerInfo
    {
        Guid PeerID { get; }
        string DisplayName { get; }
        PeerMode StartMode { get; }
    }
}
