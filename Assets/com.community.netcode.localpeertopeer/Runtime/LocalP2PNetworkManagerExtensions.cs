using System;
using UnityEngine;
using Unity.Netcode;

namespace Netcode.LocalPeerToPeer
{

    public static class LocalP2PNetworkManagerExtensions
    {
        public static Guid GetPeerID(this NetworkManager networkManager)
        {
            var p2pTransport = networkManager.NetworkConfig.NetworkTransport as LocalP2PTransport;
            return p2pTransport?.PeerID ?? default;
        }

        public static bool StartPeerToPeer(this NetworkManager networkManager)
        {
            // TODO: this?
            // if (networkManager.IsListening)
            //     networkManager.Shutdown();

            var p2pTransport = networkManager.NetworkConfig.NetworkTransport as LocalP2PTransport;
            return p2pTransport?.StartPeerToPeer() ?? false;
        }

        public static void ShutdownPeerToPeer(this NetworkManager networkManager)
        {
            if (networkManager.IsListening)
                networkManager.Shutdown();

            var p2pTransport = networkManager.NetworkConfig.NetworkTransport as LocalP2PTransport;
            p2pTransport?.ShutdownPeerToPeer();
        }

    }

}
