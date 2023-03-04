using Netcode.LocalPeerToPeer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Netcode;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

// TODO:
// 1. get this working
// 2. figure out user IDs
// 3. figure out how to choose a server/clients
// 4. figure out connection limits and maybe ways around that (like multiple
//    sessions, or relays, or trees, or...?)
// 5. stop advertising when connection limit reached

// FIXME: avoid Guid/String conversions

namespace Netcode.Transports.MultipeerConnectivity
{
    public class MultipeerConnectivityTransport : LocalP2PTransport
    {
        /// <summary>
        /// The ID of the local user.
        /// </summary>
        [SerializeField]
        [Tooltip("The ID of the local user.")]
// FIXME: get this from the clientId or generate a GUID or something
        public string UserID;

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

        private void Update()
        {
            while (m_MCSession.discoveredQueueSize > 0)
            {
                using (var peerInfo = m_MCSession.DequeueDiscoveredPeer())
                {
                    PeerDiscovered(peerInfo);
                }
            }

            while (m_MCSession.connectedQueueSize > 0)
            {
                using (var peerInfo = m_MCSession.DequeueConnectedPeer())
                {
                    PeerConnected(peerInfo);
                }
            }

            while (m_MCSession.disconnectedQueueSize > 0)
            {
                using (var peerInfo = m_MCSession.DequeueDisconnectedPeer())
                {
                    PeerDisconnected(peerInfo);
                }
            }

            // check for errors encountered
            while (m_MCSession.errorCount > 0)
            {
                using (var error = m_MCSession.DequeueError())
                {
                    Debug.LogError("Error " + error.Code + " from Multipeer Connectivity: " + error.Description);
                    // TODO: maybe base the status on error codes instead
                }
            }

            // check for incoming data
            while (m_MCSession.receivedDataQueueSize > 0)
            {
                using (var peerMessage = m_MCSession.DequeueReceivedData())
                {
                    var peerID = peerMessage.peerID;
                    ReceivedPeerMessage(new Guid(peerMessage.peerID), new ArraySegment<byte>(peerMessage.data.NativeArrayNoCopy.ToArray()));
                }
            }
        }

        #endregion

        #region NetworkTransport Overrides

        public override bool IsSupported => Application.platform == RuntimePlatform.IPhonePlayer
                || Application.platform == RuntimePlatform.OSXPlayer
                || Application.platform == RuntimePlatform.tvOS;

        public override int DirectlyConnectedPeerCount => m_MCSession.connectedPeerCount;

        public override void DisconnectLocalClient()
        {
            if (m_SuspendDisconnectingClients)
                return;

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(MultipeerConnectivityTransport)}] - Disconnecting local client.");

            m_MCSession.Disconnect();
        }

        public override unsafe ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
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
            {
                m_MCSession.SendToPeer(peerID.ToString(), nsData, sendMode);
            }
        }

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

        protected override void AcceptPeer(PeerInfo peer)
        {
            m_MCSession.InviteDiscoveredPeer(peer.PeerID.ToString());
        }

        protected override void RejectPeer(PeerInfo peer)
        {
            m_MCSession.RejectDiscoveredPeer(peer.PeerID.ToString());
        }

        #endregion
        /*
                #region ConnectionManager Implementation

                private byte[] payloadCache = new byte[4096];

                private void EnsurePayloadCapacity(int size)
                {
                    if (payloadCache.Length >= size)
                        return;

                    payloadCache = new byte[Math.Max(payloadCache.Length * 2, size)];
                }

                void IConnectionManager.OnConnecting(ConnectionInfo info)
                {
                    if (LogLevel <= LogLevel.Developer)
                        Debug.Log($"[{nameof(MultipeerConnectivityTransport)}] - Connecting with Steam user {info.Identity.SteamId}.");
                }

                void IConnectionManager.OnConnected(ConnectionInfo info)
                {
                    InvokeOnTransportEvent(NetworkEvent.Connect, ServerClientId, default, Time.realtimeSinceStartup);

                    if (LogLevel <= LogLevel.Developer)
                        Debug.Log($"[{nameof(MultipeerConnectivityTransport)}] - Connected with Steam user {info.Identity.SteamId}.");
                }

                void IConnectionManager.OnDisconnected(ConnectionInfo info)
                {
                    InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, default, Time.realtimeSinceStartup);

                    if (LogLevel <= LogLevel.Developer)
                        Debug.Log($"[{nameof(MultipeerConnectivityTransport)}] - Disconnected Steam user {info.Identity.SteamId}.");
                }

                unsafe void IConnectionManager.OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
                {
                    EnsurePayloadCapacity(size);

                    fixed (byte* payload = payloadCache)
                    {
                        UnsafeUtility.MemCpy(payload, (byte*)data, size);
                    }

                    InvokeOnTransportEvent(NetworkEvent.Data, ServerClientId, new ArraySegment<byte>(payloadCache, 0, size), Time.realtimeSinceStartup);
                }

                #endregion

                #region SocketManager Implementation

                void ISocketManager.OnConnecting(SocketConnection connection, ConnectionInfo info)
                {
                    if (LogLevel <= LogLevel.Developer)
                        Debug.Log($"[{nameof(MultipeerConnectivityTransport)}] - Accepting connection from Steam user {info.Identity.SteamId}.");

                    connection.Accept();
                }

                void ISocketManager.OnConnected(SocketConnection connection, ConnectionInfo info)
                {
                    if (!m_ConnectedPeers.ContainsKey(connection.Id))
                    {
                        m_ConnectedPeers.Add(connection.Id, new Client()
                        {
                            connection = connection,
                            steamId = info.Identity.SteamId
                        });

                        InvokeOnTransportEvent(NetworkEvent.Connect, connection.Id, default, Time.realtimeSinceStartup);

                        if (LogLevel <= LogLevel.Developer)
                            Debug.Log($"[{nameof(MultipeerConnectivityTransport)}] - Connected with Steam user {info.Identity.SteamId}.");
                    }
                    else if (LogLevel <= LogLevel.Normal)
                        Debug.LogWarning($"[{nameof(MultipeerConnectivityTransport)}] - Failed to connect client with ID {connection.Id}, client already connected.");
                }

                void ISocketManager.OnDisconnected(SocketConnection connection, ConnectionInfo info)
                {
                    m_ConnectedPeers.Remove(connection.Id);

                    InvokeOnTransportEvent(NetworkEvent.Disconnect, connection.Id, default, Time.realtimeSinceStartup);

                    if (LogLevel <= LogLevel.Developer)
                        Debug.Log($"[{nameof(MultipeerConnectivityTransport)}] - Disconnected Steam user {info.Identity.SteamId}");
                }

                unsafe void ISocketManager.OnMessage(SocketConnection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
                {
                    EnsurePayloadCapacity(size);

                    fixed (byte* payload = payloadCache)
                    {
                        UnsafeUtility.MemCpy(payload, (byte*)data, size);
                    }

                    InvokeOnTransportEvent(NetworkEvent.Data, connection.Id, new ArraySegment<byte>(payloadCache, 0, size), Time.realtimeSinceStartup);
                }

                #endregion
*/
        #region Utility Methods

        #endregion
    }
}