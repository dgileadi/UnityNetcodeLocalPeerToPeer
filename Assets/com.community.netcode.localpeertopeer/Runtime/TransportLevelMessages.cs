using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Netcode;

namespace Netcode.LocalPeerToPeer
{
    public enum TransportLevelMessageType : byte
    {
        PickServerDecision,
        DisconnectCommand,
        RelayPeerConnected,
        RelayPeerDisconnected,
        RelayServerChanged,
        RelayMessage,
    }


    public class TransportLevelMessages : MonoBehaviour
    {
        protected static readonly byte[] TransportLevelMessageHeader = { 0xFF, 0xBA, 0xE0 };

        public LocalP2PTransport Transport { get; internal set; }

        protected List<PendingTransportLevelMessage> m_PendingTransportLevelMessages = new List<PendingTransportLevelMessage>();


        public virtual void SendPickServerDecision(Guid peerID, PickServerDecision decision)
        {
            SendTransportLevelMessage(peerID, TransportLevelMessageType.PickServerDecision, new PickServerDecisionPayload(decision), NetworkDelivery.Reliable);
        }

        public virtual void SendRelayPeerConnected(Guid peerID)
        {
            SendTransportLevelMessage(Transport.RelayOrServerPeerID, TransportLevelMessageType.RelayPeerConnected, new RelayPeerIDPayload(peerID), NetworkDelivery.Reliable);
        }

        public virtual void SendRelayPeerDisconnected(Guid peerID)
        {
            SendTransportLevelMessage(Transport.RelayOrServerPeerID, TransportLevelMessageType.RelayPeerDisconnected, new RelayPeerIDPayload(peerID), NetworkDelivery.Reliable);
        }

        public virtual void SendServerChanged(Guid peerID)
        {
            SendTransportLevelMessage(peerID, TransportLevelMessageType.RelayServerChanged, new RelayPeerIDPayload(Transport.ServerPeerID), NetworkDelivery.Reliable);
        }

        public virtual void SendDisconnectRequest(Guid peerID)
        {
            SendTransportLevelMessage(peerID, TransportLevelMessageType.DisconnectCommand, new ArraySegment<byte>(), NetworkDelivery.Unreliable);
        }

        public virtual void SendRelayMessage(Guid toPeerID, Guid throughPeerID, ArraySegment<byte> message, NetworkDelivery delivery)
        {
            var relayHeader = new RelayMessage(toPeerID, delivery);

            using var writer = new FastBufferWriter(4 + FastBufferWriter.GetWriteSize<RelayMessage>() + message.Count, Allocator.Temp);
            writer.WriteBytes(TransportLevelMessageHeader);
            writer.WriteByte((byte)TransportLevelMessageType.RelayMessage);
            writer.WriteValue(relayHeader);
            writer.WriteBytes(message.Array, message.Count, message.Offset);

            SendTransportLevelMessage(throughPeerID, TransportLevelMessageType.RelayMessage, writer.ToArray(), delivery);
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
        /// <param name="delivery">The delivery type (QoS) to send data with</param>
        internal protected void SendTransportLevelMessage(Guid peerID, TransportLevelMessageType type, ArraySegment<byte> payload, NetworkDelivery delivery)
        {
            if (!Transport.TryGetRecipientPeerID(peerID, out Guid throughPeerID))
            {
                if (Transport.LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Asked to send a transport-level message to unknown peer {peerID}.");
                return;
            }

            if (throughPeerID == peerID)
                Transport.SendToPeer(peerID, payload, delivery);
            else
                SendRelayMessage(peerID, throughPeerID, payload, delivery);
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

        public virtual bool HandleTransportLevelMessage(Guid fromPeerID, ArraySegment<byte> payload)
        {
            if (!IsTransportLevelMessage(payload))
                return false;

            var type = (TransportLevelMessageType)payload[3];
            HandleTransportLevelMessage(fromPeerID, type, payload);
            return true;
        }

        protected virtual void HandleTransportLevelMessage(Guid fromPeerID, TransportLevelMessageType type, ArraySegment<byte> payload)
        {
            var pendingMessage = m_PendingTransportLevelMessages.FirstOrDefault(message => message.Matches(fromPeerID, type));
            if (pendingMessage.Matches(fromPeerID, type))
            {
                pendingMessage.Message = payload.Slice(4);
                return;
            }

            switch (type)
            {
                case TransportLevelMessageType.DisconnectCommand:
                    Transport.DisconnectLocalClient();
                    break;

                case TransportLevelMessageType.RelayPeerConnected:
                    HandleRelayPeerConnected(fromPeerID, payload.Slice(4));
                    break;

                case TransportLevelMessageType.RelayServerChanged:
                    HandleRelayServerChanged(fromPeerID, payload.Slice(4));
                    break;

                case TransportLevelMessageType.RelayPeerDisconnected:
                    HandleRelayPeerDisconnected(fromPeerID, payload.Slice(4));
                    break;

                case TransportLevelMessageType.RelayMessage:
                    HandleRelayMessage(fromPeerID, payload);
                    break;
            }
        }

        protected virtual void HandleRelayPeerConnected(Guid fromPeerID, ArraySegment<byte> payload)
        {
            var peerID = ReadValue<RelayPeerIDPayload>(payload).PeerID;
            Transport.RelayedPeerConnected(peerID, fromPeerID, payload);
        }

        protected virtual void HandleRelayPeerDisconnected(Guid fromPeerID, ArraySegment<byte> payload)
        {
            var peerID = ReadValue<RelayPeerIDPayload>(payload).PeerID;
            Transport.RelayedPeerDisconnected(peerID, fromPeerID, payload);
        }

        protected virtual void HandleRelayServerChanged(Guid fromPeerID, ArraySegment<byte> payload)
        {
            Transport.ServerPeerID = ReadValue<RelayPeerIDPayload>(payload).PeerID;
            Transport.SetPeerReachedThrough(Transport.ServerPeerID, fromPeerID);
        }

        protected virtual void HandleRelayMessage(Guid fromPeerID, ArraySegment<byte> payload)
        {
            var relayMessage = ReadValue<RelayMessage>(payload.Slice(4));
            if (!Transport.TryGetRecipientPeerID(relayMessage.ToPeerID, out Guid throughPeerID))
            {
                if (Transport.LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{this.GetType().Name}] - Asked to relay to unknown peer {relayMessage.ToPeerID}.");
                return;
            }

            if (relayMessage.ToPeerID == throughPeerID)
                Transport.SendToPeer(relayMessage.ToPeerID, payload.Slice(4), relayMessage.Delivery);
            else
                SendTransportLevelMessage(throughPeerID, TransportLevelMessageType.RelayMessage, payload, relayMessage.Delivery);
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


        protected struct PickServerDecisionPayload : INetworkSerializeByMemcpy
        {
            public Guid PickedPeerID { get; set; }
            public Guid ServerPeerID { get; set; }
            public PeerMode Mode { get; set; }

            public PickServerDecisionPayload(PickServerDecision decision)
            {
                this.PickedPeerID = decision.PickedPeerID;
                this.ServerPeerID = decision.ServerPeerID;
                this.Mode = decision.Mode;
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


        protected struct RelayMessage : INetworkSerializeByMemcpy
        {
            public Guid ToPeerID { get; set; }
            public NetworkDelivery Delivery { get; set; }

            public RelayMessage(Guid toPeerID, NetworkDelivery delivery)
            {
                this.ToPeerID = toPeerID;
                this.Delivery = delivery;
            }
        }

    }

}
