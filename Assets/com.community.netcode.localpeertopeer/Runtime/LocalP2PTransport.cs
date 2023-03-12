using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Netcode;

// TODO: maybe heartbeat/disconnect support, assuming the underlying transport
// doesn't do it for us

namespace Netcode.LocalPeerToPeer
{


// TODO: should this class [RequireComponent typeof(ConnectionAdjudicator)]?
// if so, would/should multiple local transports share the same adjudicator?

    public abstract class LocalP2PTransport : NetworkTransport
    {
        /// <summary>
        /// The ID of this peer. Defaults to a generated Guid.
        /// <para>
        /// To make peer IDs persistent you should set this value before starting
        /// any netcode components. Never change this value after networking has
        /// started.
        /// </para>
        /// </summary>
        public Guid PeerID { get; set; }

        /// <summary>
        /// The mode this transport was started in. If started in Server or Client
        /// mode then this transport must remain in that mode until networking is
        /// stopped. If started in PeerToPeer mode then this transport may become
        /// a server or a client at different points of its lifetime.
        /// </summary>
        public PeerMode StartMode { get; protected set; } = PeerMode.PeerToPeer;

        /// <summary>
        /// The mode this transport is currently running in.
        /// </summary>
        public PeerMode Mode => NetworkManager.Singleton.IsServer ? PeerMode.Server : NetworkManager.Singleton.IsClient ? PeerMode.Client : PeerMode.PeerToPeer;

        /// <summary>
        /// Whether this transport is advertising its presence to other local peers.
        /// </summary>
        public abstract bool Advertising { get; protected set; }

        /// <summary>
        /// Whether this transport is looking for other peers that are advertising
        /// themselves.
        /// </summary>
        public abstract bool Discovering { get; protected set; }

        /// <summary>
        /// The maximum number of peers this transport can support, not including
        /// itself.
        /// </summary>
        protected abstract int PeerLimit { get; }

        /// <summary>
        /// A component that decides whether two peers should connect. A default
        /// adjudicator is created at runtime if one is not provided.
        /// </summary>
        [SerializeField]
        public ConnectionAdjudicator ConnectionAdjudicator;

        /// <summary>
        /// A component that sends and handles transport-level messages. A default
        /// is created at runtime if one is not provided.
        /// </summary>
        public TransportLevelMessages TransportLevelMessages;

        /// <summary>
        /// The number of peers this transport knows about.
        /// </summary>
        public int KnownPeerCount => m_MessageRecipients.Count;

        /// <summary>
        /// The number of peers this transport is directly connected to (i.e. not
        /// through any relay).
        /// </summary>
        public abstract int DirectlyConnectedPeerCount { get; }

        public override ulong ServerClientId => m_ConnectedPeersByIDs.GetValueOrDefault(ServerPeerID, 0ul);

        /// <summary>
        /// Whether this transport has been started.
        /// </summary>
        public bool Started { get; protected set; }

        /// <summary>
        /// Whether this transport has (or is) a server.
        /// </summary>
        public bool HasServer => ServerPeerID != default;

        /// <summary>
        /// Whether this transport is indirectly connected to the server through
        /// a relay peer.
        /// </summary>
        public bool IsRelayed => RelayPeerID != default;

        /// <summary>
        /// Whether this transport is a relay between a server and other peers.
        /// </summary>
        public bool IsRelayer => Mode != PeerMode.Server && (Advertising || m_ConnectedPeers.Count > 1);

        /// <summary>
        /// The ID of the relay peer if this peer has one, or else the ID of the
        /// server peer if directly connected, or else <c>default</c> if this
        /// peer isn't connected to any other peer.
        /// </summary>
        public Guid RelayOrServerPeerID => RelayPeerID == default ? ServerPeerID : RelayPeerID;

        /// <summary>
        /// The ID of this transport's server, or <c>default</c> if it has no
        /// server. If this transport is a host it is this transport's own
        /// <c>PeerID</c>.
        /// </summary>
        public Guid ServerPeerID
        {
            get => m_ServerPeerID;
            protected internal set
            {
                if (value != default && !m_ConnectedPeersByIDs.ContainsKey(value))
                {
                    var serverClientId = GenerateClientId(value);
                    m_ConnectedPeers.Add(serverClientId, value);
                    m_ConnectedPeersByIDs.Add(value, serverClientId);
                    m_MessageRecipients.Add(value, RelayPeerID == default ? value : RelayPeerID);
                }
                m_ServerPeerID = value;
            }
        }
        private Guid m_ServerPeerID;

        /// <summary>
        /// The ID of this transport's relay peer, or <c>default</c> if it has no
        /// relay peer.
        /// </summary>
        public Guid RelayPeerID { get; protected internal set; }

// TODO: document these:
        protected bool m_Inited;
        protected Dictionary<ulong, Guid> m_ConnectedPeers = new Dictionary<ulong, Guid>();
        protected Dictionary<Guid, ulong> m_ConnectedPeersByIDs = new Dictionary<Guid, ulong>();
        protected Dictionary<Guid, Guid> m_MessageRecipients = new Dictionary<Guid, Guid>();
        protected Dictionary<Guid, PickServerDecision> m_PendingConnections = new Dictionary<Guid, PickServerDecision>();
        protected ulong m_NextClientId = 1;
        protected bool m_SuspendDisconnectingClients;
        protected Coroutine m_ChangeModeCoroutine;
        internal protected LogLevel LogLevel => NetworkManager.Singleton.LogLevel;

        #region Public API

        public override void Initialize(NetworkManager networkManager = null)
        {
            if (!m_Inited)
                m_Inited = true;
        }

        /// <summary>
        /// Starts peer advertising and discovering. Must be started before
        /// <see cref="NetworkManager"/> has been started.
        /// </summary>
        /// <returns>Returns success or failure</returns>
        public virtual bool StartPeerToPeer()
        {
            if (Started)
            {
                if (LogLevel <= LogLevel.Normal)
                    Debug.LogWarning($"[{this.GetType().Name}] - Asked to StartPeerToPeer but was already running.");
                return true;
            }

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{this.GetType().Name}] - Starting in peer-to-peer mode.");

            return StartWithMode(PeerMode.PeerToPeer);
        }

        public override bool StartServer()
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{this.GetType().Name}] - Starting as server.");

            return StartWithMode(PeerMode.Server);
        }

        public override bool StartClient()
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{this.GetType().Name}] - Starting as client.");

            return StartWithMode(PeerMode.Client);
        }

        /// <summary>
        /// Stops peer advertising and discovering and shuts down the transport.
        /// </summary>
        public virtual void ShutdownPeerToPeer()
        {
            Advertising = false;
            Discovering = false;
            m_ConnectedPeers.Clear();
            m_ConnectedPeersByIDs.Clear();
            m_MessageRecipients.Clear();
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (m_SuspendDisconnectingClients)
                return;

            if (!m_ConnectedPeers.TryGetValue(clientId, out Guid peerID))
            {
                if (LogLevel <= LogLevel.Normal)
                    Debug.LogWarning($"[{this.GetType().Name}] - Failed to disconnect remote client with ID {clientId}, client not connected.");
                return;
            }

            TransportLevelMessages.SendDisconnectRequest(peerID);

            m_ConnectedPeers.Remove(clientId);
            m_ConnectedPeersByIDs.Remove(peerID);
            m_MessageRecipients.Remove(peerID);

            // also remove all peers that relay through the disconnected peer
            foreach (var key in m_MessageRecipients.Keys)
            {
                if (m_MessageRecipients[key] == peerID)
                    m_MessageRecipients.Remove(key);
                if (m_ConnectedPeersByIDs.Remove(key, out ulong peerClientId))
                    m_ConnectedPeers.Remove(peerClientId);
            }

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{this.GetType().Name}] - Disconnecting remote client with ID {clientId}.");
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            Guid peerID;
            if (clientId == ServerClientId)
                peerID = ServerPeerID;
            else if (!m_ConnectedPeers.TryGetValue(clientId, out peerID))
            {
                if (LogLevel <= LogLevel.Normal)
                    Debug.LogWarning($"[{this.GetType().Name}] - Attempted to send data to unconnected client: {clientId}");
                return;
            }

            var throughPeerID = m_MessageRecipients[peerID];
            if (throughPeerID == peerID)
                SendToPeer(peerID, data, delivery);
            else
                TransportLevelMessages.SendRelayMessage(peerID, throughPeerID, data, delivery);
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            return NetworkEvent.Nothing;
        }

        public bool IsConnectedToPeer(Guid peerID) => m_MessageRecipients.ContainsKey(peerID);

        public bool IsPendingConnectionToPeer(Guid peerID) => m_PendingConnections.ContainsKey(peerID);

        public bool TryGetRecipientPeerID(Guid peerID, out Guid recipientPeerID)
        {
            return m_MessageRecipients.TryGetValue(peerID, out recipientPeerID);
        }

        #endregion

        #region Methods a child class must implement

        protected abstract void InviteDiscoveredPeer(PeerInfo peer);

        protected abstract void RejectDiscoveredPeer(PeerInfo peer);

        protected abstract void AcceptInvitationFrom(PeerInfo peer);

        protected abstract void RejectInvitationFrom(PeerInfo peer);

        protected internal abstract void SendToPeer(Guid peerID, ArraySegment<byte> data, NetworkDelivery delivery);

        #endregion

        #region Methods a child class should call to handle network events

        protected void PeerDiscovered(PeerInfo peer)
        {
            // reject cycles and impossible connections
            if (!ConnectionAdjudicator.ShouldConnectTo(peer))
            {
                RejectDiscoveredPeer(peer);
                return;
            }

            m_PendingConnections.Add(peer.PeerID, default);

            ConnectionAdjudicator.HandlePeerDiscovered(peer, accept =>
            {
                if (accept)
                    InviteDiscoveredPeer(peer);
                else
                {
                    m_PendingConnections.Remove(peer.PeerID);
                    RejectDiscoveredPeer(peer);
                }
            });
        }

        protected void ReceivedInvitation(PeerInvitation invitation)
        {
            // be careful before accepting the invitation
            var sendInvitation = !m_MessageRecipients.ContainsKey(invitation.PeerID);
            PickServerDecision decision = default;
            if (sendInvitation)
            {
                decision = ConnectionAdjudicator.PickServer(invitation);
                sendInvitation = decision.Connect;
            }

            if (!sendInvitation)
            {
                m_PendingConnections.Remove(invitation.PeerID);
                RejectInvitationFrom(invitation);
                return;
            }

            m_PendingConnections[invitation.PeerID] = decision;

            var twoServersConnecting = this.HasServer && invitation.HasServer;
            ConnectionAdjudicator.HandleInvitation(invitation, twoServersConnecting, accept =>
            {
                if (accept)
                {
                    AcceptInvitationFrom(invitation);
                }
                else
                {
                    RejectInvitationFrom(invitation);
                    m_PendingConnections.Remove(invitation.PeerID);
                }
            });
        }

        protected virtual void PeerConnected(PeerInfo peer)
        {
            var clientId = GenerateClientId(peer.PeerID);
            m_ConnectedPeers.Add(clientId, peer.PeerID);
            m_ConnectedPeersByIDs.Add(peer.PeerID, clientId);
            m_MessageRecipients.Add(peer.PeerID, peer.PeerID);

            if (StartMode == peer.StartMode && StartMode != PeerMode.PeerToPeer)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - This transport and {peer.PeerID} both started in {StartMode} mode and shouldn't have connected.");
                DisconnectRemoteClient(clientId);
                return;
            }

            PickServerDecision decision;
            if (m_PendingConnections.TryGetValue(peer.PeerID, out decision))
            {
                FinalizePeerConnected(peer, decision);
                TransportLevelMessages.SendPickServerDecision(peer.PeerID, decision);
            }
            else
            {
                // receive the decision from the invited peer
                StartCoroutine(TransportLevelMessages.WaitForTransportLevelMessageFrom(peer.PeerID, TransportLevelMessageType.PickServerDecision, payload =>
                {
                    FinalizePeerConnected(peer, decision);
                }));
            }
        }

        protected internal virtual void RelayedPeerConnected(Guid peerID, Guid relayerPeerID, ArraySegment<byte> payload)
        {
            var clientId = GenerateClientId(peerID);
            m_ConnectedPeers.Add(clientId, peerID);
            m_ConnectedPeersByIDs.Add(peerID, clientId);
            m_MessageRecipients.Add(peerID, relayerPeerID);

            if (NetworkManager.Singleton.IsServer)
                InvokeOnTransportEvent(NetworkEvent.Connect, clientId, null, Time.realtimeSinceStartup);
            else
                TransportLevelMessages.SendRelayPeerConnected(peerID);
        }

        protected internal virtual void RelayedPeerDisconnected(Guid peerID, Guid relayerPeerID, ArraySegment<byte> payload)
        {
            if (!m_ConnectedPeersByIDs.TryGetValue(peerID, out ulong clientId))
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Relayed peer with ID {peerID} disconnected, but they didn't have a clientId.");
                return;
            }

            m_ConnectedPeers.Remove(clientId);
            m_ConnectedPeersByIDs.Remove(peerID);
            m_MessageRecipients.Remove(peerID);
            m_PendingConnections.Remove(peerID);

            if (NetworkManager.Singleton.IsServer)
                InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId, null, Time.realtimeSinceStartup);
            else
                TransportLevelMessages.SendRelayPeerDisconnected(peerID);
        }

        protected void ReceivedPeerMessage(Guid fromPeerID, ArraySegment<byte> payload)
        {
            if (TransportLevelMessages.HandleTransportLevelMessage(fromPeerID, payload))
                return;
            else if (m_ConnectedPeersByIDs.ContainsKey(fromPeerID))
                InvokeOnTransportEvent(NetworkEvent.Data, m_ConnectedPeersByIDs[fromPeerID], payload, Time.realtimeSinceStartup);
            else if (LogLevel <= LogLevel.Error)
                Debug.LogError($"[{this.GetType().Name}] - Received message from unknown peer with ID {fromPeerID}.");
        }

        protected void PeerDisconnected(PeerInfo peer)
        {
            if (!m_ConnectedPeersByIDs.TryGetValue(peer.PeerID, out ulong clientId))
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Peer {peer.DisplayName} with ID {peer.PeerID} disconnected, but they didn't have a clientId.");
                return;
            }

            m_ConnectedPeers.Remove(clientId);
            m_ConnectedPeersByIDs.Remove(peer.PeerID);
            m_MessageRecipients.Remove(peer.PeerID);
            m_PendingConnections.Remove(peer.PeerID);
            InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId, null, Time.realtimeSinceStartup);

            if (peer.PeerID == RelayPeerID || peer.PeerID == ServerPeerID)
                LostConnectionToServer(peer, clientId);
            else
                LostConnectionToClient(peer, clientId);
        }

        #endregion

        #region Implementation

        protected virtual bool StartWithMode(PeerMode mode)
        {
            if (Started)
                return true;

            if (!m_Inited)
            {
                Initialize(NetworkManager.Singleton);
                m_Inited = true;
            }

            if (PeerID == default)
                PeerID = Guid.NewGuid();

            ConnectionAdjudicator = GetOrCreateComponent<ConnectionAdjudicator>(ConnectionAdjudicator);
            if (ConnectionAdjudicator.Transport == null)
                ConnectionAdjudicator.Transport = this;

            TransportLevelMessages = GetOrCreateComponent<TransportLevelMessages>(TransportLevelMessages);
            if (TransportLevelMessages.Transport == null)
                TransportLevelMessages.Transport = this;

            StartMode = mode;

            RunWithMode(mode);

            Started = true;
            return true;
        }

        protected T GetOrCreateComponent<T>(T component) where T : MonoBehaviour
        {
            if (component == null)
                component = FindObjectOfType<T>(true);
            if (component == null)
            {
                if (LogLevel <= LogLevel.Normal)
                    Debug.LogWarning($"[{this.GetType().Name}] - A {typeof(T).Name} component wasn't present in the scene; creating one on " + this + ".");
                component = gameObject.AddComponent<T>();
            }
            return component;
        }

        protected virtual bool RunWithMode(PeerMode mode)
        {
            if (!ShouldChangeModeTo(mode))
                return false;
            if (mode == PeerMode.PeerToPeer)
                m_ChangeModeCoroutine = StartCoroutine(DoRunAsPeerToPeer());
            else if (mode == PeerMode.Server)
                m_ChangeModeCoroutine = StartCoroutine(DoRunAsServer());
            else if (mode == PeerMode.Client)
                m_ChangeModeCoroutine = StartCoroutine(DoRunAsClient());
            return true;
        }

        protected virtual bool ShouldChangeModeTo(PeerMode mode)
        {
            if (this.Mode == mode)
                return false;
            if (m_ChangeModeCoroutine != null)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Unable to switch to {mode} mode because a mode change is already in progress.");
                return false;
            }
            if (mode != StartMode && StartMode != PeerMode.PeerToPeer)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Unable to switch to {mode} mode because was started in {StartMode} mode.");
                return false;
            }
            return true;
        }

        protected virtual IEnumerator DoRunAsPeerToPeer()
        {
            if (NetworkManager.Singleton.IsListening)
                yield return ShutdownNetworkManager(disconnectClients: true);
            Advertising = true;
            Discovering = true;
        }

        protected virtual IEnumerator DoRunAsServer()
        {
            if (NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
                yield return ShutdownNetworkManager();
            if (!NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.StartHost();
            // notify the network manager of all known clients
            // and notify all clients that we're now the server
            foreach (var clientId in m_ConnectedPeers.Keys)
            {
                if (clientId == ServerClientId)
                    continue;
                InvokeOnTransportEvent(NetworkEvent.Connect, clientId, null, Time.realtimeSinceStartup);
                TransportLevelMessages.SendServerChanged(m_ConnectedPeers[clientId]);
            }
            Advertising = DirectlyConnectedPeerCount < PeerLimit;
            Discovering = StartMode == PeerMode.PeerToPeer; // servers need to discover other servers
        }

        protected virtual IEnumerator DoRunAsClient()
        {
            if (NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
                yield return ShutdownNetworkManager();
            if (!NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.StartClient();
            foreach (var peerID in m_ConnectedPeersByIDs.Keys)
            {
                if (peerID == ServerPeerID)
                    InvokeOnTransportEvent(NetworkEvent.Connect, ServerClientId, null, Time.realtimeSinceStartup);
                else
                {
                    TransportLevelMessages.SendRelayPeerConnected(peerID);
                    TransportLevelMessages.SendServerChanged(peerID);
                }
            }
            Advertising = IsRelayer && DirectlyConnectedPeerCount < PeerLimit;
            Discovering = !HasServer;
        }

        protected IEnumerator ShutdownNetworkManager(bool disconnectClients = false)
        {
            m_SuspendDisconnectingClients = !disconnectClients;

            NetworkManager.Singleton.Shutdown(discardMessageQueue: false);
            while (NetworkManager.Singleton.ShutdownInProgress)
                yield return null;

            m_SuspendDisconnectingClients = false;
        }

        protected virtual void LostConnectionToClient(PeerInfo peer, ulong clientId)
        {
            // also remove all peers that relay through the disconnected peer
            foreach (var peerID in m_MessageRecipients.Keys)
            {
                if (m_MessageRecipients[peerID] == peer.PeerID)
                {
                    m_MessageRecipients.Remove(peerID);
                    TransportLevelMessages.SendRelayPeerDisconnected(peerID);
                    if (m_ConnectedPeersByIDs.Remove(peerID, out ulong peerClientId))
                    {
                        m_ConnectedPeers.Remove(peerClientId);
                        InvokeOnTransportEvent(NetworkEvent.Disconnect, peerClientId, null, Time.realtimeSinceStartup);
                    }
                }
            }

            if (!Advertising && DirectlyConnectedPeerCount < PeerLimit)
                Advertising = true; // since we made some room
        }

        protected virtual void LostConnectionToServer(PeerInfo peer, ulong clientId)
        {
            this.ServerPeerID = default;
            if (peer.PeerID == this.RelayPeerID)
                this.RelayPeerID = default;

            if (this.IsRelayer && !this.IsRelayed)
            {
                RunWithMode(PeerMode.Server);
            }
            else if (!this.IsRelayed)
            {
                RunWithMode(PeerMode.PeerToPeer);
                // TODO: take ownership somehow
            }
            // otherwise if relayed, just wait around for a new server

/*
TODO: things we could do:

- immediately become a server (necessary if we're a relay)
    - this means that each of a former server's direct peers will become a server,
      leading to seven new servers
- remain a solo client and shut down or take ownership of netcode
    - in the mean time hunt for peers
- remain a client and rely on a different peer (like the top relayer) to become
  a server
- remain a client and wait around for a different server

I feel like if we have a relay hierarchy left, we should make the top relayer
into a server right away, and then depend on discovery to merge servers. This
leads to jumps in NPC movement but hopefully won't be too bad.

If we don't have a relay hierarchy then by definition we're unconnected and we
can remain a client.

So if we're a top relayer we immediately become a server and send a message to
all connected clients about it.

If we're not connected to a relayer then we so something like take ownership.

If we're connected to a relayer we just wait around for another server.
*/

            /*
            TODO:
            - immediately take over animation, physics, AI, etc.?
            - call a callback to determine what to do
                - abort the session and start discovery again
                - call `NetworkManager.Shutdown`
                - start advertising and browsing for peers
                - wait longer to reconnect
                - if the trigger was a "disconnected" event, start discovering peers
                - pick a new server
                - TODO: if the client is a relay then it might immediately become a server?
                - TODO: perhaps clients should keep track of other peers and try connecting to one of them?
            */
        }

        protected virtual ulong GenerateClientId(Guid peerID)
        {
            return ++m_NextClientId;
        }

        protected internal void SetPeerReachedThrough(Guid peerID, Guid throughPeerID)
        {
            m_MessageRecipients[peerID] = throughPeerID;
        }

        protected virtual void FinalizePeerConnected(PeerInfo peer, PickServerDecision decision)
        {
            if (decision.PickedPeerID != this.PeerID)
            {
                this.ServerPeerID = decision.ServerPeerID;
                if (decision.Mode == PeerMode.Client)
                    this.RelayPeerID = decision.PickedPeerID;
            }

            bool changedMode;
            if (decision.PickedPeerID == PeerID && decision.Mode == PeerMode.Server)
                changedMode = RunWithMode(PeerMode.Server);
            else
                changedMode = RunWithMode(PeerMode.Client);

            if (!changedMode)
                InvokeOnTransportEvent(NetworkEvent.Connect, m_ConnectedPeersByIDs[peer.PeerID], null, Time.realtimeSinceStartup);
            // otherwise the events are invoked by RunWithMode
        }

        #endregion
    }

}
