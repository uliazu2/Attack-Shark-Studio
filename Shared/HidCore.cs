// HID compartilhado (K86 Studio e X11 Studio): enumerar dispositivos e enviar/ler relatórios de feature.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

static class Native
{
    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SetupDiGetClassDevsW(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr devInfo);
    [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS c);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
    [DllImport("hid.dll")] public static extern bool HidD_GetProductString(SafeFileHandle h, byte[] b, int len);
}
[StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
[StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
[StructLayout(LayoutKind.Sequential)]
struct HIDP_CAPS
{
    public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
    public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
        NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
}
class HidInfo { public string Path; public ushort Vid, Pid, UsagePage, Usage, FeatLen; public string Product; }

static class Hid
{
    public static List<HidInfo> List()
    {
        var res = new List<HidInfo>();
        Guid g; Native.HidD_GetHidGuid(out g);
        IntPtr set = Native.SetupDiGetClassDevsW(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        try
        {
            for (uint i = 0; ; i++)
            {
                var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                int req; Native.SetupDiGetDeviceInterfaceDetailW(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref d, buf, req, out req, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                    var info = Probe(path); if (info != null) res.Add(info);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return res;
    }
    static HidInfo Probe(string path)
    {
        using (var h = Native.CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (h.IsInvalid) return null;
            var a = new HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(a);
            if (!Native.HidD_GetAttributes(h, ref a)) return null;
            var info = new HidInfo { Path = path, Vid = a.VendorID, Pid = a.ProductID, Product = "" };
            IntPtr p;
            if (Native.HidD_GetPreparsedData(h, out p))
            {
                var c = new HIDP_CAPS(); Native.HidP_GetCaps(p, ref c); Native.HidD_FreePreparsedData(p);
                info.UsagePage = c.UsagePage; info.Usage = c.Usage; info.FeatLen = c.FeatureReportByteLength;
            }
            var sb = new byte[256];
            if (Native.HidD_GetProductString(h, sb, sb.Length)) info.Product = Encoding.Unicode.GetString(sb).TrimEnd('\0');
            return info;
        }
    }
}

