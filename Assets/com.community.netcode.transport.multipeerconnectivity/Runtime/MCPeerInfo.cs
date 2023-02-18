using Netcode.LocalPeerToPeer;
using System;
using System.Runtime.InteropServices;

namespace Netcode.Transports.MultipeerConnectivity
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MCPeerInfo : LocalP2PTransport.PeerInfo, IDisposable, IEquatable<MCPeerInfo>
    {
        IntPtr m_Ptr;

        public bool Valid => m_Ptr != IntPtr.Zero;

        public Guid PeerID
        {
            get
            {
                if (!Valid)
                    throw new InvalidOperationException($"The {typeof(MCPeerInfo).Name} is not valid.");

                using (var peerID = GetPeerID(this))
                {
                    return Guid.Parse(peerID.ToString());
                }
            }
        }

        public string DisplayName
        {
            get
            {
                if (!Valid)
                    throw new InvalidOperationException($"The {typeof(MCPeerInfo).Name} is not valid.");

                using (var displayName = GetDisplayName(this))
                {
                    return displayName.ToString();
                }
            }
        }

        public LocalP2PTransport.PeerMode StartMode => (LocalP2PTransport.PeerMode)GetMode(this);

        public MCPeerInfo(Guid peerID, string displayName, LocalP2PTransport.PeerMode mode)
        {
            if (displayName == null)
                throw new ArgumentNullException(nameof(displayName));

            using (var peerID_NSString = new NSString(peerID.ToString()))
            using (var displayName_NSString = new NSString(displayName))
            {
                m_Ptr = InitWithPeerID(peerID_NSString, displayName_NSString, (byte)mode);
            }
        }

        public void Dispose() => NativeApi.CFRelease(ref m_Ptr);

        public override int GetHashCode() => m_Ptr.GetHashCode();
        public override bool Equals(object obj) => (obj is MCPeerInfo) && Equals((MCPeerInfo)obj);
        public bool Equals(MCPeerInfo other) => m_Ptr == other.m_Ptr;
        public static bool operator ==(MCPeerInfo lhs, MCPeerInfo rhs) => lhs.Equals(rhs);
        public static bool operator !=(MCPeerInfo lhs, MCPeerInfo rhs) => !lhs.Equals(rhs);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_initWithPeerID")]
        static extern IntPtr InitWithPeerID(NSString peerID, NSString displayName, byte mode);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_peerID")]
        static extern NSString GetPeerID(MCPeerInfo peerInfo);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_displayName")]
        static extern NSString GetDisplayName(MCPeerInfo peerInfo);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_mode")]
        static extern byte GetMode(MCPeerInfo peerInfo);
    }
}
