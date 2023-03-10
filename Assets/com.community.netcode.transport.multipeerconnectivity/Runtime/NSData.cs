using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Netcode.Transports.MultipeerConnectivity
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NSData : IDisposable, IEquatable<NSData>
    {
        IntPtr m_Ptr;

        internal NSData(IntPtr existing) => m_Ptr = existing;

        public bool Created => m_Ptr != IntPtr.Zero;

        public int Length => Created ? GetLength(this) : 0;

        public static unsafe NSData CreateWithBytes(NativeSlice<byte> bytes)
        {
            var ptr = bytes.GetUnsafePtr();
            if (ptr == null)
                throw new ArgumentException($"The {typeof(NativeSlice<byte>).Name} is not valid.", nameof(bytes));

            return new NSData(CreateWithBytes(ptr, bytes.Length));
        }

        public static unsafe NSData CreateWithBytesNoCopy(NativeSlice<byte> bytes)
        {
            var ptr = bytes.GetUnsafePtr();
            if (ptr == null)
                throw new ArgumentException($"The {typeof(NativeSlice<byte>).Name} is not valid.", nameof(bytes));

            return new NSData(CreateWithBytesNoCopy(ptr, bytes.Length, false));
        }

        public static unsafe NSData CreateWithBytes(ArraySegment<byte> bytes)
        {
            using (var handle = bytes.AsMemory().Pin())
            {
                return new NSData(CreateWithBytes(handle.Pointer, bytes.Count));
            }
        }

        public static unsafe NSData CreateWithBytesNoCopy(ArraySegment<byte> bytes)
        {
            using (var handle = bytes.AsMemory().Pin())
            {
                return new NSData(CreateWithBytesNoCopy(handle.Pointer, bytes.Count, false));
            }
        }

        public static unsafe NSData CreateWithBytes(byte[] bytes)
        {
            fixed (byte* ptr = bytes)
            {
                return new NSData(CreateWithBytes(ptr, bytes.Length));
            }
        }

        public static unsafe NSData CreateWithBytesNoCopy(byte[] bytes)
        {
            fixed (byte* ptr = bytes)
            {
                return new NSData(CreateWithBytesNoCopy(ptr, bytes.Length, false));
            }
        }

        public unsafe NativeSlice<byte> NativeSlice
        {
            get
            {
                if (!Created)
                    throw new InvalidOperationException($"The {typeof(NSData).Name} has not been created.");

                return NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<byte>(GetBytes(this), 1, GetLength(this));
            }
        }

        public unsafe NativeArray<byte> NativeArray => GetNativeArray(Allocator.Persistent);

        public unsafe NativeArray<byte> NativeArrayNoCopy => GetNativeArray(Allocator.None);

        private unsafe NativeArray<byte> GetNativeArray(Allocator allocator)
        {
            if (!Created)
                throw new InvalidOperationException($"The {typeof(NSData).Name} has not been created.");

            return NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(GetBytes(this), GetLength(this), allocator);
        }

        public void Dispose() => NativeApi.CFRelease(ref m_Ptr);

        public override int GetHashCode() => m_Ptr.GetHashCode();
        public override bool Equals(object obj) => (obj is NSData) && Equals((NSData)obj);
        public bool Equals(NSData other) => m_Ptr == other.m_Ptr;
        public static bool operator==(NSData lhs, NSData rhs) => lhs.Equals(rhs);
        public static bool operator!=(NSData lhs, NSData rhs) => !lhs.Equals(rhs);

        [DllImport("__Internal", EntryPoint="UnityMC_NSData_getLength")]
        static extern int GetLength(NSData self);

        [DllImport("__Internal", EntryPoint="UnityMC_NSData_getBytes")]
        static extern unsafe void* GetBytes(NSData self);

        [DllImport("__Internal", EntryPoint="UnityMC_NSData_createWithBytes")]
        static extern unsafe IntPtr CreateWithBytes(void* bytes, int length);

        [DllImport("__Internal", EntryPoint="UnityMC_NSData_createWithBytesNoCopy")]
        static extern unsafe IntPtr CreateWithBytesNoCopy(void* bytes, int length, bool freeWhenDone);
    }
}
