using Netcode.LocalPeerToPeer;
using System;
using System.Runtime.InteropServices;

namespace Netcode.Transports.MultipeerConnectivity
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MCSession : IDisposable, IEquatable<MCSession>
    {
        IntPtr m_Ptr;

        public bool created => m_Ptr != IntPtr.Zero;

        public bool advertising
        {
            get
            {
                if (!created)
                    return false;
                return GetAdvertising(this);
            }

            set
            {
                if (!created && value)
                    throw new InvalidOperationException();
                SetAdvertising(this, value);
            }
        }

        public bool browsing
        {
            get
            {
                if (!created)
                    return false;
                return GetBrowsing(this);
            }

            set
            {
                if (!created && value)
                    throw new InvalidOperationException();
                SetBrowsing(this, value);
            }
        }

        public ulong maximumNumberOfPeers => GetMaximumNumberOfPeers();

        public MCSession(Guid peerID, string displayName, PeerMode mode, string serviceType)
        {
            if (displayName == null)
                throw new ArgumentNullException(nameof(displayName));

            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));

            using (var peerInfo_ObjCPeerInfo = new MCPeerInfo(peerID, displayName, mode))
            using (var serviceType_NSString = new NSString(serviceType))
            {
                m_Ptr = InitWithPeerInfo(peerInfo_ObjCPeerInfo, serviceType_NSString);
            }
        }

        public void SendToAllPeers(NSData data, MCSessionSendDataMode mode)
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            if (!data.Created)
                throw new ArgumentException($"'{nameof(data)}' is not valid.", nameof(data));

            using (var error = SendToAllPeers(this, data, mode))
            {
                if (error.Valid)
                    throw error.ToException();
            }
        }

        public void SendToPeer(Guid peerID, NSData data, MCSessionSendDataMode mode)
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            if (!data.Created)
                throw new ArgumentException($"'{nameof(data)}' is not valid.", nameof(data));

            using (var peerID_NSUUID = NSUUID.CreateWithGuid(peerID))
            using (var error = SendToPeer(this, peerID_NSUUID, data, mode))
            {
                if (error.Valid)
                    throw error.ToException();
            }
        }

        public void InviteDiscoveredPeer(Guid peerID, MCPeerInfo invitation)
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            using (var peerID_NSUUID = NSUUID.CreateWithGuid(peerID))
            using (var error = InviteDiscoveredPeer(this, peerID_NSUUID, invitation))
            {
                if (error.Valid)
                    throw error.ToException();
            }
        }

        public void RejectDiscoveredPeer(Guid peerID)
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            using (var peerID_NSUUID = NSUUID.CreateWithGuid(peerID))
            {
                RejectDiscoveredPeer(this, peerID_NSUUID);
            }
        }

        public void AcceptInvitationFrom(Guid peerID)
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            using (var peerID_NSUUID = NSUUID.CreateWithGuid(peerID))
            using (var error = AcceptInvitationFrom(this, peerID_NSUUID))
            {
                if (error.Valid)
                    throw error.ToException();
            }
        }

        public void RejectInvitationFrom(Guid peerID)
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            using (var peerID_NSUUID = NSUUID.CreateWithGuid(peerID))
            {
                RejectInvitationFrom(this, peerID_NSUUID);
            }
        }

        public int receivedDataQueueSize => GetReceivedDataQueueSize(this);

        public MCPeerMessage DequeueReceivedData()
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            return DequeueReceivedData(this);
        }

        public int errorCount => GetErrorCount(this);

        public NSError DequeueError()
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            return DequeueError(this);
        }

        public int discoveredQueueSize => GetDiscoveredQueueSize(this);

        public MCPeerInfo DequeueDiscoveredPeer()
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            return DequeueDiscoveredPeer(this);
        }

        public int invitationQueueSize => GetInvitationQueueSize(this);

        public MCPeerInfo DequeueInvitation()
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            return DequeueInvitation(this);
        }

        public int connectedQueueSize => GetConnectedQueueSize(this);

        public MCPeerInfo DequeueConnectedPeer()
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            return DequeueConnectedPeer(this);
        }

        public int disconnectedQueueSize => GetDisconnectedQueueSize(this);

        public MCPeerInfo DequeueDisconnectedPeer()
        {
            if (!created)
                throw new InvalidOperationException($"The {typeof(MCSession).Name} has not been created.");

            return DequeueDisconnectedPeer(this);
        }

        public void Disconnect()
        {
            Disconnect(this);
        }

        public int connectedPeerCount => GetConnectedPeerCount(this);

        public void Dispose() => NativeApi.CFRelease(ref m_Ptr);
        public override int GetHashCode() => m_Ptr.GetHashCode();
        public override bool Equals(object obj) => (obj is MCSession) && Equals((MCSession)obj);
        public bool Equals(MCSession other) => m_Ptr == other.m_Ptr;
        public static bool operator==(MCSession lhs, MCSession rhs) => lhs.Equals(rhs);
        public static bool operator!=(MCSession lhs, MCSession rhs) => !lhs.Equals(rhs);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_initWithPeerInfo")]
        static extern IntPtr InitWithPeerInfo(MCPeerInfo peerInfo, NSString serviceType);

        [DllImport("__Internal", EntryPoint="UnityMC_Delegate_sendToAllPeers")]
        static extern NSError SendToAllPeers(MCSession self, NSData data, MCSessionSendDataMode mode);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_sendToPeer")]
        static extern NSError SendToPeer(MCSession self, NSUUID userID, NSData data, MCSessionSendDataMode mode);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_inviteDiscoveredPeer")]
        static extern NSError InviteDiscoveredPeer(MCSession self, NSUUID peerID, MCPeerInfo invitation);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_rejectDiscoveredPeer")]
        static extern void RejectDiscoveredPeer(MCSession self, NSUUID peerID);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_acceptInvitationFrom")]
        static extern NSError AcceptInvitationFrom(MCSession self, NSUUID peerID);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_rejectInvitationFrom")]
        static extern void RejectInvitationFrom(MCSession self, NSUUID peerID);

        [DllImport("__Internal", EntryPoint="UnityMC_Delegate_receivedDataQueueSize")]
        static extern int GetReceivedDataQueueSize(MCSession self);

        [DllImport("__Internal", EntryPoint="UnityMC_Delegate_dequeueReceivedData")]
        static extern MCPeerMessage DequeueReceivedData(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_errorCount")]
        static extern int GetErrorCount(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_dequeueError")]
        static extern NSError DequeueError(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_discoveredQueueSize")]
        static extern int GetDiscoveredQueueSize(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_dequeueDiscoveredPeer")]
        static extern MCPeerInfo DequeueDiscoveredPeer(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_invitationQueueSize")]
        static extern int GetInvitationQueueSize(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_dequeueInvitation")]
        static extern MCPeerInfo DequeueInvitation(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_connectedQueueSize")]
        static extern int GetConnectedQueueSize(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_dequeueConnectedPeer")]
        static extern MCPeerInfo DequeueConnectedPeer(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_disconnectedQueueSize")]
        static extern int GetDisconnectedQueueSize(MCSession self);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_dequeueDisconnectedPeer")]
        static extern MCPeerInfo DequeueDisconnectedPeer(MCSession self);

        [DllImport("__Internal", EntryPoint="UnityMC_Delegate_connectedPeerCount")]
        static extern int GetConnectedPeerCount(MCSession self);

        [DllImport("__Internal", EntryPoint="UnityMC_Delegate_setAdvertising")]
        static extern void SetAdvertising(MCSession self, bool advertising);

        [DllImport("__Internal", EntryPoint="UnityMC_Delegate_getAdvertising")]
        static extern bool GetAdvertising(MCSession self);

        [DllImport("__Internal", EntryPoint="UnityMC_Delegate_setBrowsing")]
        static extern void SetBrowsing(MCSession self, bool browsing);

        [DllImport("__Internal", EntryPoint="UnityMC_Delegate_getBrowsing")]
        static extern bool GetBrowsing(MCSession self);

        [DllImport("__Internal", EntryPoint="UnityMC_Delegate_disconnect")]
        static extern void Disconnect(MCSession self);

        [DllImport("__Internal", EntryPoint = "kMCSessionMaximumNumberOfPeers")]
        static extern ulong GetMaximumNumberOfPeers();
    }
}
