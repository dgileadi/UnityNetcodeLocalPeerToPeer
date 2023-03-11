using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Netcode.Transports.MultipeerConnectivity
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NSUUID : IDisposable, IEquatable<NSUUID>
    {
        IntPtr m_Ptr;

        internal NSUUID(IntPtr existing) => m_Ptr = existing;

        public NSUUID(NSData serializedUUID)
        {
            if (!serializedUUID.Created)
                throw new ArgumentException("The serialized UUID is not valid.", nameof(serializedUUID));

            m_Ptr = Deserialize(serializedUUID);
        }

        public bool Created => m_Ptr != IntPtr.Zero;

        public static NSUUID Random() => new NSUUID(CreateRandom());

        public static unsafe NSUUID CreateWithGuid(Guid guid)
        {
            fixed (byte* ptr = guid.ToByteArray())
            {
                return new NSUUID(CreateWithBytes(ptr));
            }
        }

        public unsafe Guid ToGuid()
        {
            if (!Created)
                return default;

            using (var buffer = new NativeArray<byte>(16, Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
            {
                GetBytes(this, buffer.GetUnsafePtr());
                {
                    return new Guid(buffer.AsReadOnlySpan());
                }
            }
        }

        public NSData Serialize()
        {
            if (!Created)
                throw new InvalidOperationException($"The {typeof(NSUUID).Name} has not been created.");

            return Serialize(this);
        }

        public void Dispose() => NativeApi.CFRelease(ref m_Ptr);
        public override int GetHashCode() => GetHashCode(this);
        public override bool Equals(object obj) => (obj is NSUUID) && Compare(this, (NSUUID)obj) == 0;
        public bool Equals(NSUUID other) => Compare(this, other) == 0;
        public static bool operator==(NSUUID lhs, NSUUID rhs) => lhs.Equals(rhs);
        public static bool operator!=(NSUUID lhs, NSUUID rhs) => !lhs.Equals(rhs);

        [DllImport("__Internal", EntryPoint="UnityMC_NSUUID_createRandom")]
        static extern IntPtr CreateRandom();

        [DllImport("__Internal", EntryPoint="UnityMC_NSUUID_createWithBytes")]
        static extern unsafe IntPtr CreateWithBytes(void* bytes);

        [DllImport("__Internal", EntryPoint="UnityMC_NSUUID_getBytes")]
        static extern unsafe void GetBytes(NSUUID self, void* bytes);

        [DllImport("__Internal", EntryPoint="UnityMC_NSUUID_serialize")]
        static extern NSData Serialize(NSUUID self);

        [DllImport("__Internal", EntryPoint="UnityMC_NSUUID_deserialize")]
        static extern IntPtr Deserialize(NSData data);

        [DllImport("__Internal", EntryPoint = "UnityMC_NSUUID_compare")]
        static extern int Compare(NSUUID self, NSUUID other);

        [DllImport("__Internal", EntryPoint = "UnityMC_NSUUID_hash")]
        static extern int GetHashCode(NSUUID self);
    }
}
