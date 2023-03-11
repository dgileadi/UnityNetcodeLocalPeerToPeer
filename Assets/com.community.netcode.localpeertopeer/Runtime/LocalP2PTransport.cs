using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Netcode;

// TODO: things this class might do:
// - work with LocalP2PNetworkManagerExtensions to start/stop p2p sessions
//   before/after NetworkManager
// - provide hooks for picking a server, whether to connect, what to do
//   after a disconnect, etc.
// - heartbeat
// - relay

// algorithm for picking a server: https://paulbellamy.com/2017/02/a-distributed-trustless-coin-flip-algorithm
// - along with advertising, send a user ID and guess hash
// - once connected, each peer sends the other their actual guess
// - the winner becomes the server

// TODO: maybe heartbeat/disconnect support, assuming the underlying transport
// doesn't do it for us

// TODO: maybe use string peerIDs for compatibility

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
                if (!m_ConnectedPeersByIDs.ContainsKey(value))
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

            if (m_ConnectedPeers.TryGetValue(clientId, out Guid peerID))
            {
                TransportLevelMessages.SendTransportLevelMessage(peerID, TransportLevelMessageType.DisconnectCommand, new ArraySegment<byte>(), NetworkDelivery.Unreliable);
                m_ConnectedPeers.Remove(clientId);
                m_ConnectedPeersByIDs.Remove(peerID);
                m_MessageRecipients.Remove(peerID);

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{this.GetType().Name}] - Disconnecting remote client with ID {clientId}.");
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning($"[{this.GetType().Name}] - Failed to disconnect remote client with ID {clientId}, client not connected.");
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
// avoid cycles
// TODO: and avoid other impossible situations
/*
// Don't accept if we're already connected
// Always accept if we haven't sent our own invitation to the peer
// Otherwise accept if our ID is lower than theirs so that only one peer accepts
BOOL shouldAccept = ![m_Session.connectedPeers containsObject:mcPeerID] &&
        ((!knownByID) || [peerInfo.peerID compare:m_PeerInfo.peerID] == NSOrderedDescending);

`PickServer` aborts if a server is connecting as a client to a relay, and if
the other server would connect to it instead (i.e. only allow one server to
connect to a relay of the other)
*/
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
// TODO: handle the decision
                TransportLevelMessages.SendPickServerDecision(peer.PeerID, decision);
            }
            else
            {
                TransportLevelMessages.WaitForTransportLevelMessageFrom(peer.PeerID, TransportLevelMessageType.PickServerDecision, payload =>
                {
// TODO: handle the decision
                });
            }

            if (!decision.Connect)
            {
// FIXME: telling the remote client to disconnect *doesn't work* because
// Multipeer Connectivity doesn't allow you to disconnect a peer from a session!
/*
How this needs to work:

An browser sees an advertiser, and asks whether to join. The advertiser's data
is not up to date; i.e. it doesn't know whether the advertiser is/has a server,
etc.

If the query to join is accepted by the adjudicator, the browser sends an
invitation to the advertiser. The advertiser sees the invitation and asks whether
to accept. The invitation's data is up to date, including mode and server ID.
Thus the advertiser can properly judge whether to ask whether to accept.

If the adjudicator accepts the invitation, the peers join.

If two unattached peers are advertising and browsing, let's go through the scenarios:

1. both peers see the advertisement and record the other peer's ID:
    - both peers send an invitation
    - both peers receive an invitation
    - since both peers already know about the other peer, only one of them accepts

2. peer A sees the advertisement and records the other peer's ID:
    - peer A sends an invitation
    - peer B receives an invitation before seeing peer A's advertisement
    - since peer B doesn't know about peer A, peer B accepts

From a peer's perspective, when I receive an invitation I either know about the
other peer or I don't. If I do then I accept conditionally.
*/
                DisconnectRemoteClient(m_ConnectedPeersByIDs[peer.PeerID]);
                // TODO: clear out dictionaries too
                return;
            }

            if (decision.Mode == PeerMode.Server)
                this.RelayPeerID = default;
            else if (decision.Mode == PeerMode.Client)
                this.RelayPeerID = decision.PickedPeerID == this.PeerID ? default : decision.PickedPeerID;
            this.ServerPeerID = decision.ServerPeerID;

            if (decision.PickedPeerID == PeerID && decision.Mode == PeerMode.Server)
                RunAsServer();
            else
                RunAsClient();
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
                TransportLevelMessages.SendTransportLevelMessage(RelayOrServerPeerID, TransportLevelMessageType.RelayPeerConnected, payload, NetworkDelivery.Reliable);
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
            if (peer.PeerID == RelayPeerID || peer.PeerID == ServerPeerID)
                LostConnectionToServer(peer);
            else
                LostConnectionToClient(peer);
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

            if (mode == PeerMode.Server)
                RunAsServer();
            else if (mode == PeerMode.Client)
                RunAsClient();
            else
                RunAsPeerToPeer();

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

        protected virtual void RunAsPeerToPeer()
        {
            if (!ShouldChangeModeTo(PeerMode.PeerToPeer))
                return;
            m_ChangeModeCoroutine = StartCoroutine(DoRunAsPeerToPeer());
        }

        protected virtual void RunAsServer()
        {
            if (!ShouldChangeModeTo(PeerMode.Server))
                return;
            m_ChangeModeCoroutine = StartCoroutine(DoRunAsServer());
        }

        protected virtual void RunAsClient()
        {
            if (!ShouldChangeModeTo(PeerMode.Client))
                return;
            m_ChangeModeCoroutine = StartCoroutine(DoRunAsClient());
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
            foreach (var clientId in m_ConnectedPeers.Keys)
            {
                if (clientId == ServerClientId)
                    continue;
                InvokeOnTransportEvent(NetworkEvent.Connect, clientId, null, Time.realtimeSinceStartup);
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

        protected virtual void LostConnectionToClient(PeerInfo peer)
        {
            if (!m_ConnectedPeersByIDs.TryGetValue(peer.PeerID, out ulong clientId))
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Peer {peer.DisplayName} with ID {peer.PeerID} disconnected, but they weren't a client.");
                return;
            }

            InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId, null, Time.realtimeSinceStartup);
            m_ConnectedPeers.Remove(clientId);
            m_ConnectedPeersByIDs.Remove(peer.PeerID);
            m_MessageRecipients.Remove(peer.PeerID);

            if (m_ConnectedPeers.Count == 0)
            {
                // if (!ServerOnly)
                //     Discovering = true;

                /*
                TODO:
                - start browsing for peers
                - (after a delay?) call `NetworkManager.Shutdown`
                    - what happens to the game objects? transfer control...?
                - the mode becomes `pairing` again
                */
            } else if (!Advertising && DirectlyConnectedPeerCount < PeerLimit)
                Advertising = true; // since we made some room
        }

        protected virtual void LostConnectionToServer(PeerInfo peer)
        {
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

        #endregion
    }

}
