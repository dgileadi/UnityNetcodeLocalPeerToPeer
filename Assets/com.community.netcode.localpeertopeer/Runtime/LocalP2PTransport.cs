using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

// TODO: maybe heartbeat/disconnect support, assuming the underlying transport
// doesn't do it for us

// TODO: maybe use string peerIDs for compatibility

namespace Netcode.LocalPeerToPeer
{
    public abstract class LocalP2PTransport : NetworkTransport
    {
        public enum PeerMode
        {
            Undetermined,
            Server,
            Client,
            Relay,
        }

        public interface PeerInfo
        {
            Guid PeerID { get; }
            string DisplayName { get; }
            PeerMode StartMode { get; }
        }


        protected static readonly byte[] TransportLevelMessageHeader = { 0xFF, 0xBA, 0xE0 };

        /// <summary>
        /// The ID of this peer. Defaults to a generated Guid.
        /// <para>
        /// To make peer IDs persistent you should set this value before starting
        /// any netcode components. Never change this value after networking has
        /// started.
        /// </para>
        /// </summary>
        public Guid PeerID { get; set; } = Guid.NewGuid();

        public PeerMode StartMode { get; set; } = PeerMode.Undetermined;
        public PeerMode Mode {
            get => m_Mode;
            set => SetMode(value);
        }
        private PeerMode m_Mode = PeerMode.Undetermined;

        public bool ShouldBeServer => StartMode == PeerMode.Server || m_ConnectedPeers.Count > 0;

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
        /// A handler to invoke when a peer has been discovered. Defaults to
        /// <see cref="HandlePeerDiscovered"/>.
        /// </summary>
        public Action<PeerInfo> PeerDiscoveredHandler;

        protected int DirectlyConnectedClientCount => m_MessageRecipients.Count(entry => entry.Key == entry.Value);

        protected bool Started { get; set; }
        protected bool HasServer => m_ServerPeerID != default;
        protected Dictionary<ulong, Guid> m_ConnectedPeers = new Dictionary<ulong, Guid>();
        protected Dictionary<Guid, ulong> m_ConnectedPeersByIDs = new Dictionary<Guid, ulong>();
        protected Dictionary<Guid, Guid> m_MessageRecipients = new Dictionary<Guid, Guid>();
        protected List<PendingTransportLevelMessage> m_PendingTransportLevelMessages = new List<PendingTransportLevelMessage>();
        protected Guid m_ServerPeerID;
        protected Guid m_RelayPeerID;
        protected ulong m_NextClientId = 1;
        protected Coroutine m_ChangeModeCoroutine;
        protected LogLevel LogLevel => NetworkManager.Singleton.LogLevel;

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

            return StartWithMode(PeerMode.Undetermined);
        }

        public override bool StartServer()
        {
            return StartWithMode(PeerMode.Server);
        }

        public override bool StartClient()
        {
            return StartWithMode(PeerMode.Client);
        }

        protected virtual bool StartWithMode(PeerMode mode)
        {
            if (Started)
                return true;

            if (PeerDiscoveredHandler == null)
                PeerDiscoveredHandler = HandlePeerDiscovered;

            Started = true;
            Mode = StartMode = mode;
            return true;
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

        private void SetMode(PeerMode mode)
        {
            if (mode == this.Mode)
                return;
            if (m_ChangeModeCoroutine != null)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Unable to switch to {mode} mode because a mode change is already in progress.");
                return;
            }
            if (mode != StartMode && StartMode != PeerMode.Undetermined)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Unable to switch back to {mode} mode because was started in {StartMode} mode.");
                return;
            }
            m_ChangeModeCoroutine = StartCoroutine(ChangeModeTo(mode));
        }

        protected virtual IEnumerator ChangeModeTo(PeerMode mode)
        {
            switch (mode)
            {
                case PeerMode.Undetermined:
                    if (NetworkManager.Singleton.IsListening)
                    {
                        NetworkManager.Singleton.Shutdown(discardMessageQueue: false);
                        yield return WaitForNetworkManagerShutdown();
                    }
                    Advertising = true;
                    Discovering = true;
                    break;

                case PeerMode.Server:
                    if (NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
                    {
                        NetworkManager.Singleton.Shutdown(discardMessageQueue: false);
                        yield return WaitForNetworkManagerShutdown();
                    }
                    if (!NetworkManager.Singleton.IsListening)
                        NetworkManager.Singleton.StartHost();
                    Advertising = true;
                    Discovering = false; // TODO: true in some/all cases?
                    break;

                case PeerMode.Relay:
                case PeerMode.Client:
                    if (NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
                    {
                        NetworkManager.Singleton.Shutdown(discardMessageQueue: false);
                        yield return WaitForNetworkManagerShutdown();
                    }
                    if (!NetworkManager.Singleton.IsListening)
                        NetworkManager.Singleton.StartClient();
                    Discovering = !HasServer;
                    Advertising = (mode == PeerMode.Relay);
// TODO: if not relay, remove all clients?
                    break;
            }
            m_Mode = mode;
            m_ChangeModeCoroutine = null;
        }

        protected IEnumerator WaitForNetworkManagerShutdown()
        {
            while (NetworkManager.Singleton.ShutdownInProgress)
                yield return null;
        }

        protected void PeerDiscovered(PeerInfo peer)
        {
            // avoid circles
            if (IsClient(peer.PeerID))
            {
                RejectPeer(peer);
                return;
            }

            PeerDiscoveredHandler(peer);
        }

        public virtual void HandlePeerDiscovered(PeerInfo peer)
        {
            // accept unless both peers started in server mode
            if (StartMode != PeerMode.Server || peer.StartMode != PeerMode.Server)
                AcceptPeer(peer);
            else
                RejectPeer(peer);
        }

        protected bool IsClient(Guid peerID)
        {
            return m_MessageRecipients.ContainsKey(peerID);
        }

        protected bool IsRelayedClient(Guid peerID)
        {
            Guid relayPeerID;
            if (m_MessageRecipients.TryGetValue(peerID, out relayPeerID))
                return relayPeerID != peerID;
            return false;
        }

        public abstract void AcceptPeer(PeerInfo peer);

        public abstract void RejectPeer(PeerInfo peer);

        protected virtual void PeerConnected(PeerInfo peer, Guid throughPeerID)
        {
// TODO: should we avoid generating a client ID until we know who the server is?
            var clientId = GenerateClientId(peer);
            m_ConnectedPeers.Add(clientId, peer.PeerID);
            m_ConnectedPeersByIDs.Add(peer.PeerID, clientId);
            m_MessageRecipients.Add(peer.PeerID, throughPeerID);

            if (StartMode == peer.StartMode && StartMode != PeerMode.Undetermined)
            {
                // this should never happen
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - This transport and {peer.PeerID} both started in {StartMode} mode and shouldn't have connected.");
                DisconnectRemoteClient(clientId);
                return;
            }

            StartCoroutine(PickServer(peer, (success, serverPeerID) =>
            {

            }));

            /*
            TODO:
              - peer transport calls back with a decision on whether to connect
              - if so, transport initiates a connection
              - relay transport calls back with a decision on whether to accept the connection
              - if so, client transport calls `NetworkManager.StartClient`
              - client stops advertising and discovering
              - if relay transport hit the client limit, it stops advertising and sends a message to all clients to become relays
              - relay sends message to parent of the user ID of the new client
              - parent stores the fact that it should send messages to that client to the relay instead
            */

            // TODO: negotiate server, as needed
            // then, once NetworkManager is running,
            // InvokeOnTransportEvent(NetworkEvent.Connect, clientId, null, Time.realtimeSinceStartup);
        }

        /// <summary>
        /// Coroutine for choosing a server between this transport and the peer.
        /// This method must result in the same peer being chosen on both ends
        /// of the connection.
        /// </summary>
        /// <param name="peer">The peer to compare with.</param>
        /// <param name="callback">A callback to call with whether picking a server
        /// was successfull and, if so, with the ID of the chosen server peer.</param>
        /// <returns>An IEnumerator for the coroutine.</returns>
        protected virtual IEnumerator PickServer(PeerInfo peer, Action<bool, Guid> callback)
        {
            var clientCount = m_ConnectedPeers.Count;
            var guess = UnityEngine.Random.Range(byte.MinValue, byte.MaxValue);
            var startPayload = new ArraySegment<byte>(new byte[] { (byte)Mode, (byte)clientCount, (byte)guess });
            SendTransportLevelMessage(peer.PeerID, TransportLevelMessageType.NegotiateServerStart, startPayload, NetworkDelivery.Reliable);

            ArraySegment<byte>? peerStartPayload = null;
            yield return WaitForTransportLevelMessageFrom(peer.PeerID, TransportLevelMessageType.NegotiateServerStart, payload => peerStartPayload = payload);
            if (peerStartPayload == null || peerStartPayload?.Count != 3)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Didn't receive valid server negotiation message from {peer.PeerID}.");
                yield break;
            }
            var peerMode = (PeerMode)peerStartPayload?[0];

            // this peer is already a server
            if (peerMode == PeerMode.Server && Mode != PeerMode.Server)
            {
                callback(true, peer.PeerID);
                yield break;
            }

            // the other peer is already a server
            if (Mode == PeerMode.Server && peerMode != PeerMode.Server)
            {
                callback(true, PeerID);
                yield break;
            }

            // neither peer is a server
            if (Mode != PeerMode.Server && peerMode != PeerMode.Server)
            {
                // if one peer has more clients, pick that one
                var peerClientCount = (int)peerStartPayload?[2];
                if (clientCount > peerClientCount)
                {
                    callback(true, PeerID);
                    yield break;
                }
                if (clientCount < peerClientCount)
                {
                    callback(true, peer.PeerID);
                    yield break;
                }

                // use each guess to pick a server
                callback(true, PickServerFromGuesses(peer, guess, (int)peerStartPayload?[1]));
                yield break;
            }

            // both peers are servers: this is complicated

            // TODO:
            /*
            algorithm:
            - tell each other about current server status
            - if one is server and other isn't, done
            - if neither is server, use "guess" from first message to pick server
            - if both are server, decide whether to reconnect
                - if not, abort
                - if so, use "guess" from the first message to pick server
                - winner does nothing
                - loser stops NetworkManager
                    - keeps peers and starts as relay?
                    - drops peers and lets everyone reconnect?
            */

            callback(false, default);
        }

        protected Guid PickServerFromGuesses(PeerInfo peer, int guess, int peerGuess)
        {
            var result = peerGuess * guess;
            Guid serverPeerID;
            if (result % 2 == 0)
                serverPeerID = PeerID.CompareTo(peer.PeerID) <= 0 ? PeerID : peer.PeerID;
            else
                serverPeerID = PeerID.CompareTo(peer.PeerID) > 0 ? PeerID : peer.PeerID;
            return serverPeerID;
        }

        protected IEnumerator WaitForTransportLevelMessageFrom(Guid peerID, TransportLevelMessageType type, Action<ArraySegment<byte>?> callback, float timeout = 10)
        {
            var timeoutTime = timeout > 0 ? Time.realtimeSinceStartup + timeout : float.MaxValue;
            m_PendingTransportLevelMessages.Add(new PendingTransportLevelMessage(peerID, type));
            ArraySegment<byte>? received;
            while ((received = m_PendingTransportLevelMessages.First(pending => pending.Received(peerID, type)).Message) == null && Time.realtimeSinceStartup <= timeoutTime)
                yield return null;
            m_PendingTransportLevelMessages.RemoveAll(pending => pending.Matches(peerID, type));
            callback.Invoke(received);
        }

        protected void ReceivedPeerMessage(Guid fromPeerID, ArraySegment<byte> payload)
        {
            if (IsTransportLevelMessage(payload))
            {
                var type = (TransportLevelMessageType)payload[3];
                HandleTransportLevelMessage(fromPeerID, type, payload.Slice(4));
            }
            else if (m_ConnectedPeersByIDs.ContainsKey(fromPeerID))
                InvokeOnTransportEvent(NetworkEvent.Data, m_ConnectedPeersByIDs[fromPeerID], payload, Time.realtimeSinceStartup);
            else if (LogLevel <= LogLevel.Error)
                Debug.LogError($"[{this.GetType().Name}] - Received message from unknown peer with ID {fromPeerID}.");
        }

        protected virtual void HandleTransportLevelMessage(Guid fromPeerID, TransportLevelMessageType type, ArraySegment<byte> payload)
        {
// TODO:
            switch (type)
            {
                case TransportLevelMessageType.NegotiateServerStart:
                    // TODO:
                    break;
                case TransportLevelMessageType.NegotiateServerDecide:
                    // TODO:
                    break;
                case TransportLevelMessageType.DisconnectCommand:
                    DisconnectLocalClient();
                    break;
                case TransportLevelMessageType.RelayPeerConnected:
                    // TODO:
                    break;
                case TransportLevelMessageType.RelayPeerDisconnected:
                    // TODO:
                    break;
            }
        }

        protected void PeerDisconnected(PeerInfo peer)
        {
            if (peer.PeerID == m_RelayPeerID || peer.PeerID == m_ServerPeerID)
                LostConnectionToServer(peer);
            else
                LostConnectionToClient(peer);
        }

        protected virtual void LostConnectionToClient(PeerInfo peer)
        {
            ulong clientId;
            if (!m_ConnectedPeersByIDs.TryGetValue(peer.PeerID, out clientId))
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
            } else if (!Advertising && DirectlyConnectedClientCount < PeerLimit)
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

        protected virtual ulong GenerateClientId(PeerInfo peer)
        {
            return ++m_NextClientId;
        }

        /// <summary>
        /// Send a payload to the specified peer, data and networkDelivery that
        /// will be handled internally by the other peer's LocalP2PTransport rather
        /// than delivered to its NetworkManager.
        /// </summary>
        /// <param name="peerID">The peerID to send to</param>
        /// <param name="payload">The data to send</param>
        /// <param name="networkDelivery">The delivery type (QoS) to send data with</param>
        protected void SendTransportLevelMessage(Guid peerID, TransportLevelMessageType type, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            var newPayload = new ArraySegment<byte>(TransportLevelMessageHeader.Append((byte)type).Concat(payload).ToArray());
            if (m_ConnectedPeersByIDs.TryGetValue(peerID, out ulong clientId))
                Send(clientId, newPayload, networkDelivery);
            else if (LogLevel <= LogLevel.Error)
                Debug.LogError($"[{this.GetType().Name}] - Failed to send transport-level message to peer with ID {peerID}, client not connected.");
        }

        /// <summary>
        /// Determine whether the payload is an internal transport-level message.
        /// </summary>
        /// <param name="payload">The received data</param>
        /// <returns><c>true</c> if the payload is intended to be handled
        /// internally by the transport, <c>false</c> if it is intended to be
        /// forwarded to the NetworkManager</returns>
        protected bool IsTransportLevelMessage(ArraySegment<byte> payload)
        {
            return payload.Count >= TransportLevelMessageHeader.Length
                    && payload[0] == TransportLevelMessageHeader[0]
                    && payload[1] == TransportLevelMessageHeader[1]
                    && payload[2] == TransportLevelMessageHeader[2];
        }

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


        protected enum TransportLevelMessageType : byte
        {
            NegotiateServerStart,
            NegotiateServerDecide,
            DisconnectCommand,
            RelayPeerConnected,
            RelayPeerDisconnected,
        }

        protected struct PendingTransportLevelMessage
        {
            public readonly Guid PeerID;
            public readonly TransportLevelMessageType Type;
            public ArraySegment<byte>? Message;

            public PendingTransportLevelMessage(Guid PeerID, TransportLevelMessageType Type)
            {
                this.PeerID = PeerID;
                this.Type = Type;
                this.Message = null;
            }

            public bool Matches(Guid peerID, TransportLevelMessageType type)
            {
                return peerID == PeerID && type == Type;
            }

            public bool Received(Guid peerID, TransportLevelMessageType type)
            {
                return Matches(peerID, type) && Message != null;
            }
        }

    }

}
