using System;
using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace Netcode.LocalPeerToPeer
{
    public interface NegotiateServerMessage
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
        /// A random guess used to break ties
        /// </summary>
        byte Guess { get; }

        /// <summary>
        /// The PeerID of the peer's server or <c>default(Guid)</c> if the peer
        /// doesn't have a server
        /// </summary>
        public Guid ServerID { get; }
    }


    /// <summary>
    /// Decides whether to connect to peers in various cases. Override to customize
    /// the behavior.
    /// </summary>
    public class ConnectionAdjudicator : MonoBehaviour
    {
        public LocalP2PTransport Transport { get; internal set; }

// TODO: use this:
        [SerializeField]
        public float ConnectPeerTimeout = 30;

        [SerializeField]
        public float ConnectServersTimeout = 30;

        /// <summary>
        /// A callback to call with a decision on whether to accept a connection
        /// with another peer.
        /// </summary>
        /// <param name="accept">Whether to accept the connection</param>
        public delegate void AcceptConnectionCallback(bool accept);

        /// <summary>
        /// A callback to call with a decision on whether this transport or a
        /// newly-connected peer should act as a server.
        /// </summary>
        /// <param name="connect">Whether, after trying to pick a server, the peers
        /// should connect at all</param>
        /// <param name="pickedPeerID">The ID of the peer that should act as a
        /// server</param>
        /// <param name="serverPeerID">If the picked peer already has its own server
        /// and is acting as a relay, the ID of the actual server</param>
        /// <param name="mode">The mode the peer should act in—<c>Server</c> if the
        /// picked peer will act as a server, or <c>Client</c> if the picked peer
        /// will act as a relay to the actual server</param>
        public delegate void PickServerCallback(bool connect, Guid pickedPeerID, Guid serverPeerID, PeerMode mode);

        /// <summary>
        /// Decide whether to accept a connection to a discovered peer.
        /// <para>
        /// The default behavior immediately calls the callback with <c>true</c>
        /// if both were started using
        /// <see cref="LocalP2PNetworkManagerExtensions.StartPeerToPeer"/> or if
        /// one was started using <see cref="NetworkManager.StartServer"/> and the
        /// other was started using <see cref="NetworkManager.StartClient"/>.
        /// Otherwise it immediately calls the callback with <c>false</c>.
        /// </para>
        /// <para>
        /// Override to customize this behavior.
        /// </para>
        /// </summary>
        /// <param name="peer">The connecting peer.</param>
        /// <param name="callback">A callback to call with the decision. Pass
        /// <c>true</c> to accept or <c>false</c> to reject the connection.</param>
        public virtual void HandlePeerDiscovered(PeerInfo peer, AcceptConnectionCallback callback)
        {
            callback(Transport.StartMode != peer.StartMode || Transport.StartMode == PeerMode.Undetermined);
        }

        /// <summary>
        /// Decide whether to connect to the given peer when this local transport
        /// is a server and the connecting peer is also a server. In this case one
        /// of the peers will need to give up being a server and connect to the
        /// other peer as a client or relay.
        /// <para>
        /// The default behavior always immediately calls the callback with
        /// <c>true</c> to accept the connection.
        /// </para>
        /// <para>
        /// Note that this method is only called for peers that were started in
        /// using <see cref="LocalP2PNetworkManagerExtensions.StartPeerToPeer"/>.
        /// Peers that were started using <see cref="NetworkManager.StartServer"/>
        /// always remain servers, and two of them cannot connect to each other.
        /// </para>
        /// </summary>
        /// <param name="peer">The connecting server peer.</param>
        /// <param name="callback">A callback to call with the decision. Pass
        /// <c>true</c> to accept or <c>false</c> to reject the connection.</param>
        public virtual void HandleTwoServersConnecting(PeerInfo peer, AcceptConnectionCallback callback)
        {
            callback(true);
        }

        /// <summary>
        /// Coroutine for choosing a server between this transport and the peer.
        /// This method must result in the same peer being chosen on both ends
        /// of the connection.
        /// <para>
        /// The default behavior... TODO: document this
        /// <list type="bullet">
        ///   <item>
        ///     <description>TODO:</description>
        ///   </item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="peer">The peer to compare with.</param>
        /// <param name="callback">A callback to call with whether picking a server
        /// was successfull and, if so, with the ID of the chosen server peer, the
        /// ID of the chosen peer's own server, and the mode the peer should run in.</param>
        /// <returns>An IEnumerator for the coroutine.</returns>
        public virtual IEnumerator PickServer(PeerInfo peer, PickServerCallback callback)
        {
            var startTime = Time.realtimeSinceStartup;

            // send my message
            var myPayload = SendNegotiateServerMessage(peer);

            // wait for their corresponding message
            ArraySegment<byte>? peerStartBytes = null;
            yield return Transport.WaitForTransportLevelMessageFrom(peer.PeerID, TransportLevelMessageType.NegotiateServer, payload => peerStartBytes = payload, ConnectServersTimeout);
            var receivedMessageDuration = Time.realtimeSinceStartup - startTime;
            if (peerStartBytes == null)
            {
                if (Transport.LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Didn't receive valid server negotiation message from {peer.PeerID} within {receivedMessageDuration} seconds.");
                callback(false, default, default, default);
                yield break;
            }

            var theirPayload = ReadNegotiateServerMessage(peerStartBytes.Value);

            // this peer is already a server
            if (theirPayload.Mode == PeerMode.Server && myPayload.Mode != PeerMode.Server)
            {
                callback(true, peer.PeerID, peer.PeerID, PeerMode.Server);
                yield break;
            }

            // the other peer is already a server
            if (myPayload.Mode == PeerMode.Server && theirPayload.Mode != PeerMode.Server)
            {
                callback(true, Transport.PeerID, Transport.PeerID, PeerMode.Server);
                yield break;
            }

            // both peers are servers
            if (myPayload.Mode == PeerMode.Server && theirPayload.Mode == PeerMode.Server)
            {
                bool? shouldConnectTwoServers = null;
                HandleTwoServersConnecting(peer, accept => shouldConnectTwoServers = accept);
                yield return AwaitDecision(() => shouldConnectTwoServers != null, ConnectServersTimeout - receivedMessageDuration);
                if (shouldConnectTwoServers != true)
                {
                    if (shouldConnectTwoServers == null && Transport.LogLevel <= LogLevel.Normal)
                        Debug.LogError($"[{Transport.GetType().Name}] - Timed out waiting to decide whether to connect two servers.");
                    callback(false, default, default, default);
                    yield break;
                }
            }

            Guid pickedPeerID;

            // if one peer has more known peers, pick that one
            if (!PickServerFromClientCounts(peer, myPayload.PeerCount, theirPayload.PeerCount, out pickedPeerID))
                // otherwise use each guess to pick a server
                pickedPeerID = PickServerFromGuesses(peer, myPayload.Guess, theirPayload.Guess);

            var serverPayload = pickedPeerID == Transport.PeerID ? myPayload : theirPayload;
            var mode = DetectPeerMode(pickedPeerID, serverPayload, peer);
            var serverPeerID = mode == PeerMode.Server ? pickedPeerID : serverPayload.ServerID;

            callback(true, pickedPeerID, serverPeerID, mode);
        }

        /// <summary>
        /// Send a "negotiate server" message and return the sent message.
        /// <para>
        /// This method defaults to sending a <see cref="DefaultNegotiateServerMessage" />.
        /// Override to send a custom message.
        /// </para>
        /// </summary>
        /// <param name="peer">The peer to send the message to</param>
        /// <returns>The message that was sent</returns>
        protected virtual NegotiateServerMessage SendNegotiateServerMessage(PeerInfo peer)
        {
            var guess = UnityEngine.Random.Range(byte.MinValue, byte.MaxValue);
            var message = new DefaultNegotiateServerMessage();
            message.Mode = Transport.Mode;
            message.PeerCount = (byte)Transport.KnownPeerCount;
            message.Guess = (byte)guess;

            Transport.SendTransportLevelMessage<DefaultNegotiateServerMessage>(peer.PeerID, TransportLevelMessageType.NegotiateServer, (DefaultNegotiateServerMessage)message, NetworkDelivery.Reliable);

            return message;
        }

        /// <summary>
        /// Deserialize a "negotiate server" from another peer and return it.
        /// <para>
        /// This method defaults to reading a <see cref="DefaultNegotiateServerMessage" />.
        /// Override to read a custom message.
        /// </para>
        /// </summary>
        /// <param name="bytes">The encoded message from the peer</param>
        /// <returns>The message that was received</returns>
        protected virtual NegotiateServerMessage ReadNegotiateServerMessage(ArraySegment<byte> bytes)
        {
            return Transport.ReadValue<DefaultNegotiateServerMessage>(bytes);
        }

        protected bool PickServerFromClientCounts(PeerInfo peer, int myPeerCount, int theirPeerCount, out Guid serverPeerID)
        {
            if (myPeerCount > theirPeerCount)
            {
                serverPeerID = Transport.PeerID;
                return true;
            }
            if (myPeerCount < theirPeerCount)
            {
                serverPeerID = peer.PeerID;
                return true;
            }
            serverPeerID = default;
            return false;
        }

        protected Guid PickServerFromGuesses(PeerInfo peer, int myGuess, int theirGuess)
        {
            var result = theirGuess * myGuess;
            Guid serverPeerID;
            if (result % 2 == 0)
                serverPeerID = Transport.PeerID.CompareTo(peer.PeerID) <= 0 ? Transport.PeerID : peer.PeerID;
            else
                serverPeerID = Transport.PeerID.CompareTo(peer.PeerID) > 0 ? Transport.PeerID : peer.PeerID;
            return serverPeerID;
        }

        protected IEnumerator AwaitDecision(Func<bool> isDecided, float timeout)
        {
            var timeoutTime = timeout > 0 ? Time.realtimeSinceStartup + timeout : float.MaxValue;
            while (!isDecided() && Time.realtimeSinceStartup <= timeoutTime)
                yield return null;
        }

        protected PeerMode DetectPeerMode(Guid serverPeerID, NegotiateServerMessage serverPayload, PeerInfo peer)
        {
            if (serverPayload.ServerID != default && serverPayload.ServerID != peer.PeerID)
                return serverPayload.Mode == PeerMode.Undetermined ? PeerMode.Client : serverPayload.Mode;
            return PeerMode.Server;
        }


        protected struct DefaultNegotiateServerMessage : NegotiateServerMessage, INetworkSerializeByMemcpy
        {
            public PeerMode Mode { get => (PeerMode)m_Mode; set => m_Mode = (byte)value; }
            private byte m_Mode;
            public byte PeerCount { get; set; }
            public byte Guess { get; set; }
            public Guid ServerID { get; set; }
        }
    }

}
