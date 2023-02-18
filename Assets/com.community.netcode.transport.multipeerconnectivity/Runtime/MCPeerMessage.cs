using System;
using System.Runtime.InteropServices;

namespace Netcode.Transports.MultipeerConnectivity
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MCPeerMessage : IDisposable, IEquatable<MCPeerMessage>
    {
        IntPtr m_Ptr;

        public bool Valid => m_Ptr != IntPtr.Zero;

        public string peerID
        {
            get
            {
                if (!Valid)
                    throw new InvalidOperationException($"The {typeof(MCPeerMessage).Name} is not valid.");

                using (var peerID = GetPeerID(this))
                {
                    return peerID.ToString();
                }
            }
        }

        public NSData data
        {
            get
            {
                if (!Valid)
                    throw new InvalidOperationException($"The {typeof(MCPeerMessage).Name} is not valid.");

                return GetData(this);
            }
        }

        public void Dispose()
        {
            data.Dispose();
            NativeApi.CFRelease(ref m_Ptr);
        }

        public override int GetHashCode() => m_Ptr.GetHashCode();
        public override bool Equals(object obj) => (obj is MCPeerMessage) && Equals((MCPeerMessage)obj);
        public bool Equals(MCPeerMessage other) => m_Ptr == other.m_Ptr;
        public static bool operator ==(MCPeerMessage lhs, MCPeerMessage rhs) => lhs.Equals(rhs);
        public static bool operator !=(MCPeerMessage lhs, MCPeerMessage rhs) => !lhs.Equals(rhs);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerMessage_peerID")]
        static extern NSString GetPeerID(MCPeerMessage peerMessage);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerMessage_data")]
        static extern NSData GetData(MCPeerMessage peerMessage);
    }
}
