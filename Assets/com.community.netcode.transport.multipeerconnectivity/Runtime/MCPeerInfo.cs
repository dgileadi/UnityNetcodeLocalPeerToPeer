using Netcode.LocalPeerToPeer;
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Netcode.Transports.MultipeerConnectivity
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MCPeerInfo : PeerInfo, PeerInvitation, IDisposable, IEquatable<MCPeerInfo>
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
                    return peerID.ToGuid();
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

        public PeerMode StartMode => (PeerMode)GetStartMode(this);

        public RuntimePlatform Platform => (RuntimePlatform)GetPlatform(this);

        public PeerMode Mode
        {
            get => (PeerMode)GetMode(this);
            set => SetMode(this, (byte)value);
        }

        public byte PeerCount
        {
            get => GetPeerCount(this);
            set => SetPeerCount(this, (byte)value);
        }

        public Guid ServerPeerID
        {
            get
            {
                if (!Valid)
                    throw new InvalidOperationException($"The {typeof(MCPeerInfo).Name} is not valid.");

                using (var serverPeerID = GetServerPeerID(this))
                {
                    return serverPeerID.ToGuid();
                }
            }
            set
            {
                if (!Valid)
                    throw new InvalidOperationException($"The {typeof(MCPeerInfo).Name} is not valid.");

                using (var serverUUID = NSUUID.CreateWithGuid(value))
                {
                    SetServerPeerID(this, serverUUID);
                }
            }
        }


        public MCPeerInfo(Guid peerID, string displayName, PeerMode startMode)
        {
            if (displayName == null)
                throw new ArgumentNullException(nameof(displayName));

            using (var peerID_NSUUID = NSUUID.CreateWithGuid(peerID))
            using (var displayName_NSString = new NSString(displayName))
            {
                m_Ptr = InitWithPeerID(peerID_NSUUID, displayName_NSString, (byte)startMode, (byte)Application.platform);
            }
        }

        public void Dispose() => NativeApi.CFRelease(ref m_Ptr);

        public override int GetHashCode() => m_Ptr.GetHashCode();
        public override bool Equals(object obj) => (obj is MCPeerInfo) && Equals((MCPeerInfo)obj);
        public bool Equals(MCPeerInfo other) => m_Ptr == other.m_Ptr;
        public static bool operator ==(MCPeerInfo lhs, MCPeerInfo rhs) => lhs.Equals(rhs);
        public static bool operator !=(MCPeerInfo lhs, MCPeerInfo rhs) => !lhs.Equals(rhs);

        [DllImport("__Internal", EntryPoint = "UnityMC_Delegate_initWithPeerID")]
        static extern IntPtr InitWithPeerID(NSUUID peerID, NSString displayName, byte startMode, byte platform);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_peerID")]
        static extern NSUUID GetPeerID(MCPeerInfo peerInfo);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_displayName")]
        static extern NSString GetDisplayName(MCPeerInfo peerInfo);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_startMode")]
        static extern byte GetStartMode(MCPeerInfo peerInfo);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_platform")]
        static extern byte GetPlatform(MCPeerInfo peerInfo);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_mode")]
        static extern byte GetMode(MCPeerInfo peerInfo);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_setMode")]
        static extern void SetMode(MCPeerInfo peerInfo, byte mode);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_peerCount")]
        static extern byte GetPeerCount(MCPeerInfo peerInfo);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_setPeerCount")]
        static extern void SetPeerCount(MCPeerInfo peerInfo, byte peerCount);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_serverPeerID")]
        static extern NSUUID GetServerPeerID(MCPeerInfo peerInfo);

        [DllImport("__Internal", EntryPoint = "UnityMC_PeerInfo_setServerPeerID")]
        static extern void SetServerPeerID(MCPeerInfo peerInfo, NSUUID serverPeerID);
    }
}
