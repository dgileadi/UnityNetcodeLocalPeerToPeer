using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Netcode;

// TODO: maybe heartbeat/disconnect support, assuming the underlying transport
// doesn't do it for us

// TODO: maybe use string peerIDs for compatibility

namespace Netcode.LocalPeerToPeer
{
    public abstract class LocalP2PTransport : NetworkTransport
    {
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

        public PeerMode Mode => NetworkManager.Singleton.IsServer ? PeerMode.Server : NetworkManager.Singleton.IsClient ? PeerMode.Client : PeerMode.Undetermined;

        // FIXME: use this? relying on m_ConnectedPeers.Count is wrong, since server goes there too
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

        public ConnectionAdjudicator ConnectionAdjudicator;

        public int KnownPeerCount => m_MessageRecipients.Count;
        public int DirectlyConnectedPeerCount => m_MessageRecipients.Count(entry => entry.Key == entry.Value);

        public override ulong ServerClientId => m_ConnectedPeersByIDs.GetValueOrDefault(m_ServerPeerID, 0ul);

        public bool Started { get; protected set; }
        public bool HasServer => m_ServerPeerID != default && m_ServerPeerID != PeerID;
        public bool IsRelayed => m_RelayPeerID != default;
        public bool IsRelayer => Mode != PeerMode.Server && (Advertising || m_ConnectedPeers.Count > 1);

        protected Dictionary<ulong, Guid> m_ConnectedPeers = new Dictionary<ulong, Guid>();
        protected Dictionary<Guid, ulong> m_ConnectedPeersByIDs = new Dictionary<Guid, ulong>();
        protected Dictionary<Guid, Guid> m_MessageRecipients = new Dictionary<Guid, Guid>();
        protected List<PendingTransportLevelMessage> m_PendingTransportLevelMessages = new List<PendingTransportLevelMessage>();
        protected Guid m_ServerPeerID;
        protected Guid m_RelayPeerID;
        protected ulong m_NextClientId = 1;
        protected bool m_SuspendDisconnectingClients;
        protected Coroutine m_ChangeModeCoroutine;
        internal protected LogLevel LogLevel => NetworkManager.Singleton.LogLevel;

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

            if (ConnectionAdjudicator == null)
                ConnectionAdjudicator = FindObjectOfType<ConnectionAdjudicator>(true);
            if (ConnectionAdjudicator == null)
            {
                if (LogLevel <= LogLevel.Normal)
                    Debug.LogWarning($"[{this.GetType().Name}] - A ConnectionAdjudicator component wasn't present in the scene; creating one on " + this + ".");
                ConnectionAdjudicator = gameObject.AddComponent<ConnectionAdjudicator>();
            }
            if (ConnectionAdjudicator.Transport == null)
                ConnectionAdjudicator.Transport = this;

            StartMode = mode;

            if (mode == PeerMode.Server)
                RunAsServer();
            else if (mode == PeerMode.Client)
                RunAsClient();
            else
                RunAsUndetermined();

            Started = true;
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

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (m_SuspendDisconnectingClients)
                return;

            if (m_ConnectedPeers.TryGetValue(clientId, out Guid peerID))
            {
                SendTransportLevelMessage(peerID, TransportLevelMessageType.DisconnectCommand, new ArraySegment<byte>(), NetworkDelivery.Unreliable);
                m_ConnectedPeers.Remove(clientId);

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{this.GetType().Name}] - Disconnecting remote client with ID {clientId}.");
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning($"[{this.GetType().Name}] - Failed to disconnect remote client with ID {clientId}, client not connected.");
        }

        protected virtual void RunAsUndetermined()
        {
            if (!ShouldChangeModeTo(PeerMode.Undetermined))
                return;
            m_ChangeModeCoroutine = StartCoroutine(DoRunAsUndetermined());
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
            if (mode == this.Mode)
                return false;
            if (m_ChangeModeCoroutine != null)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Unable to switch to {mode} mode because a mode change is already in progress.");
                return false;
            }
            if (mode != StartMode && StartMode != PeerMode.Undetermined)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Unable to switch back to {mode} mode because was started in {StartMode} mode.");
                return false;
            }
            return true;
        }

        protected virtual IEnumerator DoRunAsUndetermined()
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
            Advertising = true;
            Discovering = false; // TODO: true in some/all cases?
        }

        protected virtual IEnumerator DoRunAsClient()
        {
            if (NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
                yield return ShutdownNetworkManager();
            if (!NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.StartClient();
            foreach (var peerID in m_ConnectedPeersByIDs.Keys)
            {
                if (peerID == m_ServerPeerID)
                    continue;
                SendRelayPeerConnected(peerID);
                SendServerChanged(peerID);
            }
            Discovering = !HasServer;
            Advertising = IsRelayer;
        }

        protected IEnumerator ShutdownNetworkManager(bool disconnectClients = false)
        {
            m_SuspendDisconnectingClients = !disconnectClients;

            NetworkManager.Singleton.Shutdown(discardMessageQueue: false);
            while (NetworkManager.Singleton.ShutdownInProgress)
                yield return null;

            m_SuspendDisconnectingClients = false;
        }

        protected void PeerDiscovered(PeerInfo peer)
        {
            // avoid circles
            if (IsClient(peer.PeerID))
            {
                RejectPeer(peer);
                return;
            }

            ConnectionAdjudicator.HandlePeerDiscovered(peer, accept =>
            {
                if (accept)
                    AcceptPeer(peer);
                else
                    RejectPeer(peer);
            });
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

        protected abstract void AcceptPeer(PeerInfo peer);

        protected abstract void RejectPeer(PeerInfo peer);

        protected abstract void SendToPeer(Guid peerID, ArraySegment<byte> data, NetworkDelivery delivery);

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            Guid peerID;
            if (clientId == ServerClientId)
                peerID = m_ServerPeerID;
            else if (!m_ConnectedPeers.TryGetValue(clientId, out peerID))
            {
                if (LogLevel <= LogLevel.Normal)
                    Debug.LogWarning($"[{this.GetType().Name}] - Attempted to send data to unconnected client: {clientId}");
                return;
            }

            SendToPeer(peerID, data, delivery);

            // TODO: handle relay here?
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            return NetworkEvent.Nothing;
        }

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

            StartCoroutine(ConnectionAdjudicator.PickServer(peer, (success, pickedPeerID, serverPeerID, mode) =>
            {
                if (!success)
                    return;

                this.m_ServerPeerID = serverPeerID;
                if (mode == PeerMode.Server)
                    this.m_RelayPeerID = default;
                else if (mode == PeerMode.Client)
                    this.m_RelayPeerID = pickedPeerID == this.PeerID ? default : pickedPeerID;

                if (pickedPeerID == PeerID && mode == PeerMode.Server)
                    RunAsServer();
                else
                    RunAsClient();
            }));
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

        protected virtual ulong GenerateClientId(PeerInfo peer)
        {
            return ++m_NextClientId;
        }

        protected virtual void SendRelayPeerConnected(Guid peerID)
        {
            var toPeerID = m_RelayPeerID == default ? m_ServerPeerID : m_RelayPeerID;
            SendTransportLevelMessage(toPeerID, TransportLevelMessageType.RelayPeerConnected, new RelayPeerIDPayload(peerID), NetworkDelivery.Reliable);
        }

        protected virtual void SendServerChanged(Guid peerID)
        {
            SendTransportLevelMessage(peerID, TransportLevelMessageType.RelayServerChanged, new RelayPeerIDPayload(m_ServerPeerID), NetworkDelivery.Reliable);
        }

        /// <summary>
        /// Send a payload to the specified peer, data and networkDelivery that
        /// will be handled internally by the other peer's LocalP2PTransport rather
        /// than delivered to its NetworkManager.
        /// </summary>
        /// <param name="peerID">The peerID to send to</param>
        /// <param name="type">The type of transport-level message</param>
        /// <param name="payload">The data to send</param>
        /// <param name="payloadMaxSizeInBytes">The maximum size in bytes of the payload</param>
        /// <param name="networkDelivery">The delivery type (QoS) to send data with</param>
        internal protected void SendTransportLevelMessage(Guid peerID, TransportLevelMessageType type, INetworkSerializable payload, int payloadMaxSizeInBytes, NetworkDelivery networkDelivery)
        {
            using var writer = new FastBufferWriter(payloadMaxSizeInBytes + 4, Allocator.Temp);
            writer.WriteBytes(TransportLevelMessageHeader);
            writer.WriteByte((byte)type);
            writer.WriteNetworkSerializable(payload);
            SendTransportLevelMessage(peerID, type, new ArraySegment<byte>(writer.ToArray()), networkDelivery);
        }

        /// <summary>
        /// Send a payload to the specified peer, data and networkDelivery that
        /// will be handled internally by the other peer's LocalP2PTransport rather
        /// than delivered to its NetworkManager.
        /// </summary>
        /// <typeparam name="T">The generic type of the payload. Must be entirely unmanaged.</typeparam>
        /// <param name="peerID">The peerID to send to</param>
        /// <param name="type">The type of transport-level message</param>
        /// <param name="payload">The data to send</param>
        /// <param name="networkDelivery">The delivery type (QoS) to send data with</param>
        internal protected void SendTransportLevelMessage<T>(Guid peerID, TransportLevelMessageType type, T payload, NetworkDelivery networkDelivery, FastBufferWriter.ForStructs unused = default) where T : unmanaged, INetworkSerializeByMemcpy
        {
            using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<T>() + 4, Allocator.Temp);
            writer.WriteBytes(TransportLevelMessageHeader);
            writer.WriteByte((byte)type);
            writer.WriteValue(payload);
            SendTransportLevelMessage(peerID, type, new ArraySegment<byte>(writer.ToArray()), networkDelivery);
        }

        /// <summary>
        /// Send a payload to the specified peer, data and networkDelivery that
        /// will be handled internally by the other peer's LocalP2PTransport rather
        /// than delivered to its NetworkManager.
        /// </summary>
        /// <param name="peerID">The peerID to send to</param>
        /// <param name="type">The type of transport-level message</param>
        /// <param name="payload">The data to send</param>
        /// <param name="networkDelivery">The delivery type (QoS) to send data with</param>
        internal protected void SendTransportLevelMessage(Guid peerID, TransportLevelMessageType type, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            if (m_ConnectedPeersByIDs.TryGetValue(peerID, out ulong clientId))
            {
                if (!IsTransportLevelMessage(payload))
                    payload = new ArraySegment<byte>(TransportLevelMessageHeader.Append((byte)type).Concat(payload).ToArray());

                Send(clientId, payload, networkDelivery);
            }
            else if (LogLevel <= LogLevel.Error)
                Debug.LogError($"[{this.GetType().Name}] - Failed to send transport-level message to peer with ID {peerID}, client not connected.");
        }

        internal protected T ReadValue<T>(ArraySegment<byte> bytes, FastBufferWriter.ForStructs unused = default) where T : unmanaged, INetworkSerializeByMemcpy
        {
            var reader = new FastBufferReader(bytes, Allocator.Temp);
            T message;
            reader.ReadValue(out message);
            return message;
        }

        internal protected T ReadValue<T>(ArraySegment<byte> bytes) where T : INetworkSerializable, new()
        {
            var reader = new FastBufferReader(bytes, Allocator.Temp);
            T message;
            reader.ReadNetworkSerializable(out message);
            return message;
        }

        internal protected IEnumerator WaitForTransportLevelMessageFrom(Guid peerID, TransportLevelMessageType type, Action<ArraySegment<byte>?> callback, float timeout = 10)
        {
            var timeoutTime = timeout > 0 ? Time.realtimeSinceStartup + timeout : float.MaxValue;
            m_PendingTransportLevelMessages.Add(new PendingTransportLevelMessage(peerID, type));
            ArraySegment<byte>? received;
            while ((received = m_PendingTransportLevelMessages.FirstOrDefault(pending => pending.Received(peerID, type)).Message) == null && Time.realtimeSinceStartup <= timeoutTime)
                yield return null;
            m_PendingTransportLevelMessages.RemoveAll(pending => pending.Matches(peerID, type));
            callback.Invoke(received);
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

        protected virtual void HandleTransportLevelMessage(Guid fromPeerID, TransportLevelMessageType type, ArraySegment<byte> payload)
        {
            var pendingMessage = m_PendingTransportLevelMessages.FirstOrDefault(message => message.Matches(fromPeerID, type));
            if (pendingMessage.Matches(fromPeerID, type))
            {
                pendingMessage.Message = payload;
                return;
            }

            // TODO:
            switch (type)
            {
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


        protected struct RelayPeerIDPayload : INetworkSerializeByMemcpy
        {
            public Guid PeerID { get; set; }

            public RelayPeerIDPayload(Guid peerID)
            {
                this.PeerID = peerID;
            }
        }

    }


    public enum TransportLevelMessageType : byte
    {
        NegotiateServer,
        DisconnectCommand,
        RelayPeerConnected,
        RelayPeerDisconnected,
        RelayServerChanged,
    }

}
