using NUnit.Framework;
using Netcode.LocalPeerToPeer;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Netcode.LocalPeerToPeer.Tests
{

    public class FakeLocalP2PTransport : LocalP2PTransport
    {
        #region MonoBehaviour Messages

        private void Awake()
        {
// TODO: name this gameObject something unique
            Initialize();
        }

        private void OnDestroy()
        {
            ShutdownPeerToPeer();
        }

        private void Update()
        {
            // // process connections before invitations, and those before discoveries
            // // so that the logic in PeerConnected and ReceivedInvitation works
            // while (m_MCSession.disconnectedQueueSize > 0)
            // {
            //     using (var peerInfo = m_MCSession.DequeueDisconnectedPeer())
            //     {
            //         PeerDisconnected(peerInfo);
            //     }
            // }

            // while (m_MCSession.connectedQueueSize > 0)
            // {
            //     using (var peerInfo = m_MCSession.DequeueConnectedPeer())
            //     {
            //         PeerConnected(peerInfo);
            //     }
            // }

            // while (m_MCSession.invitationQueueSize > 0)
            // {
            //     using (var peerInfo = m_MCSession.DequeueInvitation())
            //     {
            //         ReceivedInvitation(peerInfo);
            //     }
            // }

            // while (m_MCSession.discoveredQueueSize > 0)
            // {
            //     using (var peerInfo = m_MCSession.DequeueDiscoveredPeer())
            //     {
            //         PeerDiscovered(peerInfo);
            //     }
            // }

            // while (m_MCSession.errorCount > 0)
            // {
            //     using (var error = m_MCSession.DequeueError())
            //     {
            //         if (LogLevel <= LogLevel.Error)
            //             Debug.LogError("Error " + error.Code + " from Multipeer Connectivity: " + error.Description);
            //     }
            // }

            // while (m_MCSession.receivedDataQueueSize > 0)
            // {
            //     using (var peerMessage = m_MCSession.DequeueReceivedData())
            //     {
            //         ReceivedPeerMessage(peerMessage.peerID, new ArraySegment<byte>(peerMessage.data.NativeArrayNoCopy.ToArray()));
            //     }
            // }
        }

        #endregion

        #region LocalP2PTransport Overrides

        public override bool Advertising
        {
            get => m_Advertising;
            protected set
            {
                if (m_Advertising != value)
                    DebugLog($"Changing m_Advertising to {value}");
                m_Advertising = value;
            }
        }
        private bool m_Advertising;

        public override bool Discovering
        {
            get => m_Browsing;
            protected set
            {
                if (m_Browsing != value)
                    DebugLog($"Changing m_Browsing to {value}");
                m_Browsing = value;
            }
        }
        private bool m_Browsing;

        protected override int PeerLimit => m_PeerLimit;
        [SerializeField]
        private int m_PeerLimit;

        public override bool IsSupported => true;

        public override int DirectlyConnectedPeerCount => m_DirectlyConnectedPeers.Count;

        private List<FakePeerInfo> m_DirectlyConnectedPeers = new List<FakePeerInfo>();

        public override void Initialize(NetworkManager networkManager = null)
        {
            if (m_Inited)
            {
                DebugLog("Asked to Initialize but was already initialized");
                return;
            }

            base.Initialize(networkManager);
        }

        public override void ShutdownPeerToPeer()
        {
            if (!m_Inited)
            {
                DebugLog("Asked to ShutdownPeerToPeer but was not initialized");
                return;
            }
            base.ShutdownPeerToPeer();
        }

        public override unsafe ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
        }

        public override void Shutdown()
        {
            DebugLog("Shutting down");
        }

        protected override void SendToPeer(Guid peerID, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            // var sendMode = NetworkDeliveryToSendDataMode(delivery);
            // using (var nsData = NSData.CreateWithBytesNoCopy(data))
            // using (var uuid = NSUUID.CreateWithGuid(peerID))
            // {
            //     m_MCSession.SendToPeer(peerID, nsData, sendMode);
            // }
        }

        protected override void InviteDiscoveredPeer(PeerInfo peer)
        {
            // var invitation = new MCPeerInfo(PeerID, UserDisplayName, StartMode);
            // invitation.Mode = Mode;
            // invitation.PeerCount = (byte)KnownPeerCount;
            // invitation.ServerPeerID = ServerPeerID;
            // m_MCSession.InviteDiscoveredPeer(peer.PeerID, invitation);
        }

        protected override void RejectDiscoveredPeer(PeerInfo peer)
        {
            // m_MCSession.RejectDiscoveredPeer(peer.PeerID);
        }

        protected override void AcceptInvitationFrom(PeerInfo peer)
        {
            // m_MCSession.AcceptInvitationFrom(peer.PeerID);
        }

        protected override void RejectInvitationFrom(PeerInfo peer)
        {
            // m_MCSession.RejectInvitationFrom(peer.PeerID);
        }

        public override void DisconnectLocalClient()
        {
            if (m_SuspendDisconnectingClients)
                return;

            DebugLog("Disconnecting local client.");

            m_DirectlyConnectedPeers.Clear();
        }

        #endregion

        private void DebugLog(string message)
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{this}] - {message}");
        }


        private struct FakePeerInfo : PeerInfo
        {
            public Guid PeerID { get; set; }
            public string DisplayName { get; set; }
            public PeerMode StartMode { get; set; }
            public RuntimePlatform Platform { get; set; }
        }
    }

}
