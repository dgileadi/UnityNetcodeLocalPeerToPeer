using System;
using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace Netcode.LocalPeerToPeer
{
    public struct PickServerDecision
    {
        /// <summary>
        /// Whether, after trying to pick a server, the peers should connect at
        /// all
        /// </summary>
        public readonly bool Connect;

        /// <summary>
        /// The ID of the peer that should act as a server or relay
        /// </summary>
        public readonly Guid PickedPeerID;

        /// <summary>
        /// If the picked peer already has its own server and is acting as a
        /// relay, the ID of the actual server
        /// </summary>
        public readonly Guid ServerPeerID;

        /// <summary>
        /// The mode the peer should act in—<c>Server</c> if the picked peer will
        /// act as a server, or <c>Client</c> if the picked peer will act as a
        /// relay to the actual server
        /// </summary>
        public readonly PeerMode Mode;

        public PickServerDecision(bool connect, Guid pickedPeerID, Guid serverPeerID, PeerMode mode)
        {
            this.Connect = connect;
            this.PickedPeerID = pickedPeerID;
            this.ServerPeerID = serverPeerID;
            this.Mode = mode;
        }
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
        /// A callback to call with a decision on whether to connect with another
        /// peer.
        /// </summary>
        /// <param name="accept">Whether to connect</param>
        public delegate void ConnectToPeerCallback(bool accept);

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
        /// Decide whether to invite a discovered peer to connect.
        /// <para>
        /// The default behavior immediately calls the callback with <c>true</c>.
        /// </para>
        /// <para>
        /// Override to customize this behavior.
        /// </para>
        /// </summary>
        /// <param name="peer">The connecting peer.</param>
        /// <param name="callback">A callback to call with the decision. Pass
        /// <c>true</c> to send an invitation or <c>false</c> to pass.</param>
        public virtual void HandlePeerDiscovered(PeerInfo peer, ConnectToPeerCallback callback)
        {
            callback(true);
        }

        /// <summary>
        /// Decide whether to accept an invitation from a peer.
        /// <para>
        /// The default behavior immediately calls the callback with <c>true</c>.
        /// </para>
        /// <para>
        /// Override to customize this behavior.
        /// </para>
        /// </summary>
        /// <param name="invitation">The invitation from the connecting peer.</param>
        /// <param name="twoServersConnecting">Whether both this transport and the
        /// inviting peer have a server. In this case one of the peers will need
        /// to give up being a server and connect to the other peer as a client
        /// or relay.</param>
        /// <param name="callback">A callback to call with the decision. Pass
        /// <c>true</c> to accept or <c>false</c> to reject the invitation.</param>
        public virtual void HandleInvitation(PeerInvitation invitation, bool twoServersConnecting, ConnectToPeerCallback callback)
        {
            callback(true);
        }

        public virtual bool ShouldConnectTo(PeerInfo peer)
        {
            return !Transport.IsConnectedToPeer(peer.PeerID)
                    && !Transport.IsPendingConnectionToPeer(peer.PeerID)
                    && (Transport.StartMode != peer.StartMode || Transport.StartMode == PeerMode.PeerToPeer);
        }

        /// <summary>
        /// Pick one of two Guids. This algorithm must return the same value
        /// every time and give the same result on every peer that runs it,
        /// regardless of the order of the Guid arguments.
        /// </summary>
        /// <param name="one">A guid to pick between</param>
        /// <param name="two">Another guid to pick between</param>
        /// <returns>The chosen Guid</returns>
        public virtual Guid PickOne(Guid one, Guid two)
        {
            var result = one.GetHashCode() * two.GetHashCode();
            var comparison = one.CompareTo(two);
            var pickFirst = (result % 2 == 0) ? comparison < 0 : comparison > 0;
            return pickFirst ? one : two;
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
        /// <param name="invitation">The invitation from the peer.</param>
        /// <returns>A decision on the picked server</returns>
        public virtual PickServerDecision PickServer(PeerInvitation invitation)
        {
            // the other peer is already a server
            if (invitation.Mode == PeerMode.Server && !Transport.HasServer)
                return new PickServerDecision(true, invitation.PeerID, invitation.PeerID, PeerMode.Server);

            // this transport is already a server
            if (Transport.Mode == PeerMode.Server && !invitation.HasServer)
                return new PickServerDecision(true, Transport.PeerID, Transport.PeerID, PeerMode.Server);

            Guid pickedPeerID;
            if (PickServerPeer(invitation, out pickedPeerID) != PickResult.Picked)
            {
                if (Transport.LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{Transport.GetType().Name}] - Couldn't pick a server; aborting.");
                return new PickServerDecision(false, default, default, default);
            }

            var serverPeerID = pickedPeerID == Transport.PeerID ? Transport.ServerPeerID : invitation.ServerPeerID;
            var pickedPeerMode = pickedPeerID == Transport.PeerID ? Transport.Mode : invitation.Mode;
            var mode = DetectPeerMode(pickedPeerID, pickedPeerMode, serverPeerID);
            serverPeerID = mode == PeerMode.Server ? pickedPeerID : serverPeerID;

            // if attempting to switch to a disallowed mode, abort
            var serverStartMode = pickedPeerID == Transport.PeerID ? Transport.StartMode : invitation.StartMode;
            var clientStartMode = pickedPeerID == Transport.PeerID ? invitation.StartMode : Transport.StartMode;
            if (clientStartMode == PeerMode.Server || (mode != serverStartMode && serverStartMode != PeerMode.PeerToPeer))
            {
                if (Transport.LogLevel <= LogLevel.Normal)
                {
                    if (clientStartMode == PeerMode.Server)
                    {
                        var clientPeerID = pickedPeerID == Transport.PeerID ? invitation.PeerID : Transport.PeerID;
                        Debug.Log($"[{Transport.GetType().Name}] - Attempt to change peer {clientPeerID} to client mode from start mode {clientStartMode}; aborting.");
                    }
                    else
                        Debug.Log($"[{Transport.GetType().Name}] - Attempt to change peer {pickedPeerID} to mode {mode} from start mode {serverStartMode}; aborting.");
                }
                return new PickServerDecision(false, default, default, default);
            }

            return new PickServerDecision(true, pickedPeerID, serverPeerID, mode);
        }

        /// <summary>
        /// Pick a server/relay peer ID from the negotiation messages.
        /// </summary>
        /// <param name="peer">The other peer</param>
        /// <param name="invitation">The other peer's negotiation message</param>
        /// <param name="pickedPeerID">The chosen peer ID, if one was picked</param>
        /// <returns>The result of picking a server</returns>
        protected virtual PickResult PickServerPeer(PeerInvitation invitation, out Guid pickedPeerID)
        {
            pickedPeerID = default;

            // if a server is connecting to a relay, pick the relay
            var result = PickServerConnectingToRelay(Transport.PeerID, Transport.Mode, invitation.PeerID, invitation.ServerPeerID, out pickedPeerID);
            if (result != PickResult.TryAgain)
                return result;
            result = PickServerConnectingToRelay(invitation.PeerID, invitation.Mode, Transport.PeerID, Transport.ServerPeerID, out pickedPeerID);
            if (result != PickResult.TryAgain)
                return result;

            // if one peer has more known peers, pick that one
            result = PickServerFromClientCounts(invitation, Transport.KnownPeerCount, invitation.PeerCount, out pickedPeerID);
            if (result != PickResult.TryAgain)
                return result;

            // otherwise pick a server from the IDs
            pickedPeerID = PickOne(Transport.PeerID, invitation.PeerID);
            return PickResult.Picked;
        }

        /// <summary>
        /// If one peer is a server and the other peer has a server but isn't
        /// one itself, make this server connect to the other peer (and give up
        /// being a server).
        /// </summary>
        /// <param name="myPeerID">The ID of the comparing peer</param>
        /// <param name="myMode">The current mode of the comparing peer</param>
        /// <param name="theirPeerID">The ID of the other peer</param>
        /// <param name="theirServerID">The server ID of the other peer</param>
        /// <param name="pickedPeerID">The chosen peer ID, if one was picked</param>
        /// <returns>The result of picking a server</returns>
        protected PickResult PickServerConnectingToRelay(Guid myPeerID, PeerMode myMode, Guid theirPeerID, Guid theirServerID, out Guid pickedPeerID)
        {
            pickedPeerID = default;
            if (myMode == PeerMode.Server && theirServerID != default && theirServerID != theirPeerID)
            {
                // if two servers connect to each other's relays, then we could
                // get a situation with no server at all. To prevent that, we
                // only allow one server to connect
                if (theirServerID.CompareTo(myPeerID) < 0)
                    return PickResult.AbortConnection;

                pickedPeerID = theirPeerID;
                return PickResult.Picked;
            }
            return PickResult.TryAgain;
        }

        protected PickResult PickServerFromClientCounts(PeerInfo peer, int myPeerCount, int theirPeerCount, out Guid serverPeerID)
        {
            if (myPeerCount > theirPeerCount)
            {
                serverPeerID = Transport.PeerID;
                return PickResult.Picked;
            }
            if (myPeerCount < theirPeerCount)
            {
                serverPeerID = peer.PeerID;
                return PickResult.Picked;
            }
            serverPeerID = default;
            return PickResult.TryAgain;
        }

        protected IEnumerator AwaitDecision(Func<bool> isDecided, float timeout)
        {
            var timeoutTime = timeout > 0 ? Time.realtimeSinceStartup + timeout : float.MaxValue;
            while (!isDecided() && Time.realtimeSinceStartup <= timeoutTime)
                yield return null;
        }

        protected PeerMode DetectPeerMode(Guid pickedPeerID, PeerMode pickedPeerMode, Guid serverPeerID)
        {
// FIXME: figure out all cases, document
            // if the picked peer already has a server, make it a (relay) client
            if (serverPeerID != default && serverPeerID != pickedPeerID)
                return pickedPeerMode == PeerMode.PeerToPeer ? PeerMode.Client : pickedPeerMode;
            // otherwise it's a server
            return PeerMode.Server;
        }


        protected enum PickResult
        {
            Picked,
            TryAgain,
            AbortConnection,
        }
    }

}
