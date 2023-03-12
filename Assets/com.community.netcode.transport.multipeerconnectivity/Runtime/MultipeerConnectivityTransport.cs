using Netcode.LocalPeerToPeer;
using System;
using Unity.Netcode;
using UnityEngine;

namespace Netcode.Transports.MultipeerConnectivity
{
    public class MultipeerConnectivityTransport : LocalP2PTransport
    {
        /// <summary>
        /// The display name of the local user.
        /// </summary>
        [SerializeField]
        [Tooltip("The display name of the local user.")]
        public string UserDisplayName;

        /// <summary>
        /// An override for the maximum number of peers this transport should
        /// accept, not including itself; zero means use the transport's maximum.
        /// The actual used value will never be higher than the transport's actual
        /// limit, regardless of this value.
        /// </summary>
        [SerializeField]
        [Tooltip("An override for the maximum number of peers this transport should accept, not including itself; zero means use the transport's maximum")]
        public int OverridePeerLimit = 0;

        private MCSession m_MCSession;

        #region MonoBehaviour Messages

        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            ShutdownPeerToPeer();
            m_MCSession.Dispose();
        }

// FIXME: handle the situation when the app gets backgrounded, and disconnect
// see https://www.toptal.com/ios/collusion-ios-multipeerconnectivity

        private void Update()
        {
            // process connections before invitations, and those before discoveries
            // so that the logic in PeerConnected and ReceivedInvitation works
            while (m_MCSession.disconnectedQueueSize > 0)
            {
                using (var peerInfo = m_MCSession.DequeueDisconnectedPeer())
                {
                    PeerDisconnected(peerInfo);
                }
            }

            while (m_MCSession.connectedQueueSize > 0)
            {
                using (var peerInfo = m_MCSession.DequeueConnectedPeer())
                {
                    PeerConnected(peerInfo);
                }
            }

            while (m_MCSession.invitationQueueSize > 0)
            {
                using (var peerInfo = m_MCSession.DequeueInvitation())
                {
                    ReceivedInvitation(peerInfo);
                }
            }

            while (m_MCSession.discoveredQueueSize > 0)
            {
                using (var peerInfo = m_MCSession.DequeueDiscoveredPeer())
                {
                    PeerDiscovered(peerInfo);
                }
            }

            while (m_MCSession.errorCount > 0)
            {
                using (var error = m_MCSession.DequeueError())
                {
                    if (LogLevel <= LogLevel.Error)
                        Debug.LogError("Error " + error.Code + " from Multipeer Connectivity: " + error.Description);
                }
            }

            while (m_MCSession.receivedDataQueueSize > 0)
            {
                using (var peerMessage = m_MCSession.DequeueReceivedData())
                {
                    ReceivedPeerMessage(peerMessage.peerID, new ArraySegment<byte>(peerMessage.data.NativeArrayNoCopy.ToArray()));
                }
            }
        }

        #endregion

        #region LocalP2PTransport Overrides

        public override bool Advertising { get => m_MCSession.advertising; protected set => m_MCSession.advertising = value; }

        public override bool Discovering { get => m_MCSession.browsing; protected set => m_MCSession.browsing = value; }

        protected override int PeerLimit
        {
            get
            {
                int transportLimit = (int)(m_MCSession.maximumNumberOfPeers - 1);
                return (OverridePeerLimit > 0 && OverridePeerLimit < transportLimit) ? OverridePeerLimit : transportLimit;
            }
        }

        public override bool IsSupported => Application.platform == RuntimePlatform.IPhonePlayer
                || Application.platform == RuntimePlatform.OSXPlayer
                || Application.platform == RuntimePlatform.tvOS;

        public override int DirectlyConnectedPeerCount => m_MCSession.connectedPeerCount;

        public override void Initialize(NetworkManager networkManager = null)
        {
            if (m_Inited)
                return;

            if (UserDisplayName == null)
                UserDisplayName = SystemInfo.deviceName;
            var serviceType = MultipeerConnectivitySettings.GetOrCreateSettings().BonjourServiceType;
            if (string.IsNullOrEmpty(serviceType))
            {
                Debug.LogError("Multipeer Connectivity for Netcode for GameObjects is missing required settings. Please provide them in the Multipeer Connectivity section of your Project Settings.");
                return;
            }
            m_MCSession = new MCSession(PeerID, UserDisplayName, StartMode, serviceType);

            base.Initialize(networkManager);
        }

        public override void ShutdownPeerToPeer()
        {
            if (!m_Inited)
                return;
            base.ShutdownPeerToPeer();
        }

        public override unsafe ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
        }

        public override void Shutdown()
        {
            try
            {
                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(MultipeerConnectivityTransport)}] - Shutting down.");

                m_MCSession.Disconnect();
            }
            catch (Exception e)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{nameof(MultipeerConnectivityTransport)}] - Caught an exception while shutting down: {e}");
            }
        }

        protected override void SendToPeer(Guid peerID, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            var sendMode = NetworkDeliveryToSendDataMode(delivery);
            using (var nsData = NSData.CreateWithBytesNoCopy(data))
            using (var uuid = NSUUID.CreateWithGuid(peerID))
            {
                m_MCSession.SendToPeer(peerID, nsData, sendMode);
            }
        }

        private MCSessionSendDataMode NetworkDeliveryToSendDataMode(NetworkDelivery delivery)
        {
            return delivery switch
            {
                NetworkDelivery.Reliable => MCSessionSendDataMode.Reliable,
                NetworkDelivery.ReliableFragmentedSequenced => MCSessionSendDataMode.Reliable,
                NetworkDelivery.ReliableSequenced => MCSessionSendDataMode.Reliable,
                NetworkDelivery.Unreliable => MCSessionSendDataMode.Unreliable,
                NetworkDelivery.UnreliableSequenced => MCSessionSendDataMode.Unreliable,
                _ => MCSessionSendDataMode.Reliable
            };
        }

        protected override void InviteDiscoveredPeer(PeerInfo peer)
        {
            var invitation = new MCPeerInfo(PeerID, UserDisplayName, StartMode);
            invitation.Mode = Mode;
            invitation.PeerCount = (byte)KnownPeerCount;
            invitation.ServerPeerID = ServerPeerID;
            m_MCSession.InviteDiscoveredPeer(peer.PeerID, invitation);
        }

        protected override void RejectDiscoveredPeer(PeerInfo peer)
        {
            m_MCSession.RejectDiscoveredPeer(peer.PeerID);
        }

        protected override void AcceptInvitationFrom(PeerInfo peer)
        {
            m_MCSession.AcceptInvitationFrom(peer.PeerID);
        }

        protected override void RejectInvitationFrom(PeerInfo peer)
        {
            m_MCSession.RejectInvitationFrom(peer.PeerID);
        }

        public override void DisconnectLocalClient()
        {
            if (m_SuspendDisconnectingClients)
                return;

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(MultipeerConnectivityTransport)}] - Disconnecting local client.");

            m_MCSession.Disconnect();
        }

        #endregion
    }
}