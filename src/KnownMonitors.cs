using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MonitorSwitch
{
    // Enumerate registered monitor interfaces, including non-present devices.
    // Use the interface path returned by SetupAPI, the same identity used by CCD.
    internal static class KnownMonitors
    {
        [StructLayout(LayoutKind.Sequential)] internal struct InterfaceData
        { public uint Size; public Guid Class; public uint Flags; public UIntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceData
        { public uint Size; public Guid Class; public uint Instance; public UIntPtr Reserved; }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid category, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid category, uint index, ref InterfaceData data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint needed, ref DeviceData device);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DeviceData device, uint property, out uint kind, byte[] buffer, uint size, out uint needed);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        public static List<MonitorRecord> Read()
        {
            var result = new List<MonitorRecord>(); var category = new Guid("e6f07b5f-ee97-4a90-b076-33f57bf4eaa7");
            IntPtr set = SetupDiGetClassDevs(ref category, null, IntPtr.Zero, 0x10);
            if (set == new IntPtr(-1)) return result;
            try
            {
                for (uint index = 0; index < 512; index++)
                {
                    var data = new InterfaceData { Size = (uint)Marshal.SizeOf(typeof(InterfaceData)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref category, index, ref data)) break;
                    var device = new DeviceData { Size = (uint)Marshal.SizeOf(typeof(DeviceData)) }; uint needed;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out needed, ref device);
                    if (needed < 8 || needed > 8192) continue;
                    IntPtr buffer = Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, needed, out needed, ref device)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(buffer.ToInt64() + 4));
                        if (String.IsNullOrWhiteSpace(path) || path.Length > 512) continue;
                        string name = Property(set, ref device, 12) ?? Property(set, ref device, 0) ?? "Monitor Windows";
                        string[] parts = path.Split('#'); if (parts.Length > 1) name = parts[1] + " · " + name;
                        result.Add(new MonitorRecord { DevicePath = path, HardwareName = name.Length > 256 ? name.Substring(0, 256) : name });
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }
        private static string Property(IntPtr set, ref DeviceData device, uint property)
        {
            var bytes = new byte[1024]; uint kind, needed;
            if (!SetupDiGetDeviceRegistryProperty(set, ref device, property, out kind, bytes, (uint)bytes.Length, out needed) || kind != 1 || needed < 2 || needed > bytes.Length) return null;
            return Encoding.Unicode.GetString(bytes, 0, (int)needed).TrimEnd('\0');
        }
    }
}
