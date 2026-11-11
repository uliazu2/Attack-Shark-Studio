// H510 Studio — núcleo: controle de áudio do Windows para o headset Redragon H510-PRO (sem fio)
// e um "laboratório" para observar e testar a porta HID interna dele (VID 0x040B, PID 0x0897).
//
// O headset não tem software oficial nem protocolo documentado. Tudo que é "Som" e "Microfone"
// usa só as APIs de áudio do Windows (Core Audio / WASAPI) e não manda nada para dentro do headset.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

// ======================================================================= Core Audio (COM)
[StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; public PROPERTYKEY(Guid g, int p) { fmtid = g; pid = p; } }
[StructLayout(LayoutKind.Explicit, Size = 24)] struct PROPVARIANT { [FieldOffset(0)] public short vt; [FieldOffset(8)] public IntPtr p; }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorComObject { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumeratorFull
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDeviceFull dev);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDeviceFull dev);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint n); [PreserveSig] int Item(uint i, out IMMDeviceFull dev); }
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceFull
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}
[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint n);
    [PreserveSig] int GetAt(uint i, out PROPERTYKEY key);
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
}
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr n);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr n);
    [PreserveSig] int GetChannelCount(out uint n);
    [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float v, ref Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float db);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float v);
    [PreserveSig] int SetChannelVolumeLevel(uint c, float db, ref Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint c, float v, ref Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(uint c, out float db);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint c, out float v);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClientFull
{
    [PreserveSig] int Initialize(int shareMode, int flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
    [PreserveSig] int GetBufferSize(out uint n);
    [PreserveSig] int GetStreamLatency(out long l);
    [PreserveSig] int GetCurrentPadding(out uint n);
    [PreserveSig] int IsFormatSupported(int mode, IntPtr fmt, out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr fmt);
    [PreserveSig] int GetDevicePeriod(out long def, out long min);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr h);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
}
[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClientFull
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devPos, out ulong qpc);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint n);
}
[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioRenderClient
{
    [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
    [PreserveSig] int ReleaseBuffer(uint frames, int flags);
}
// interface não documentada (usada há anos por ferramentas de áudio) para trocar o dispositivo padrão
[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }
[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
    [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
    [PreserveSig] int ResetDeviceFormat(IntPtr a);
    [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
    [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
    [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
    [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
    [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
}

class AudioEndpoint { public string Id, Name; public bool Capture; }

static class Audio
{
    public const int Render = 0, CaptureFlow = 1;
    static readonly PROPERTYKEY FriendlyName = new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    static IMMDeviceEnumeratorFull En() { return (IMMDeviceEnumeratorFull)new MMDeviceEnumeratorComObject(); }

    public static List<AudioEndpoint> List(int flow)
    {
        var res = new List<AudioEndpoint>();
        IMMDeviceCollection col; if (En().EnumAudioEndpoints(flow, 1, out col) != 0) return res;
        uint n; col.GetCount(out n);
        for (uint i = 0; i < n; i++)
        {
            IMMDeviceFull d; if (col.Item(i, out d) != 0) continue;
            string id; d.GetId(out id);
            res.Add(new AudioEndpoint { Id = id, Name = NameOf(d), Capture = flow == CaptureFlow });
        }
        return res;
    }
    static string NameOf(IMMDeviceFull d)
    {
        IPropertyStore ps; if (d.OpenPropertyStore(0, out ps) != 0) return "?";
        var key = FriendlyName; PROPVARIANT pv;
        if (ps.GetValue(ref key, out pv) != 0 || pv.p == IntPtr.Zero) return "?";
        return Marshal.PtrToStringUni(pv.p);
    }
    // o endpoint do H510 (procura pelo nome que o Windows mostra)
    public static AudioEndpoint FindHeadset(int flow)
    {
        foreach (var e in List(flow)) if (e.Name.IndexOf("H510", StringComparison.OrdinalIgnoreCase) >= 0) return e;
        return null;
    }
    public static string DefaultId(int flow, int role)
    {
        IMMDeviceFull d; if (En().GetDefaultAudioEndpoint(flow, role, out d) != 0) return null;
        string id; d.GetId(out id); return id;
    }
    static IAudioEndpointVolume Vol(string id)
    {
        IMMDeviceFull d; if (En().GetDevice(id, out d) != 0) throw new Exception("Dispositivo de áudio não encontrado.");
        var iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A"); object o;
        if (d.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) throw new Exception("Não deu para controlar o volume.");
        return (IAudioEndpointVolume)o;
    }
    public static float GetVolume(string id) { float v; Vol(id).GetMasterVolumeLevelScalar(out v); return v; }
    public static void SetVolume(string id, float v) { var g = Guid.Empty; Vol(id).SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(1, v)), ref g); }
    public static bool GetMute(string id) { bool m; Vol(id).GetMute(out m); return m; }
    public static void SetMute(string id, bool m) { var g = Guid.Empty; Vol(id).SetMute(m, ref g); }
    public static void SetDefault(string id)
    {
        var pc = (IPolicyConfig)new PolicyConfigClient();
        for (int role = 0; role <= 2; role++) pc.SetDefaultEndpoint(id, role); // console, multimídia e comunicação
    }

    public static IAudioClientFull Client(string id)
    {
        IMMDeviceFull d; if (En().GetDevice(id, out d) != 0) throw new Exception("Dispositivo de áudio não encontrado.");
        var iid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"); object o;
        if (d.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) throw new Exception("Não deu para abrir o dispositivo de áudio.");
        return (IAudioClientFull)o;
    }
}

// Captura o microfone (para o medidor) e, se ligado, devolve a voz no fone (retorno / sidetone)
class MicMonitor
{
    public volatile float Peak;          // 0..1, nível atual do microfone
    public volatile bool Running;
    public volatile bool Loopback;       // ouvir a própria voz no fone
    public volatile float LoopGain = 0.6f;
    public string Error = "";
    Thread thread; volatile bool stop;

    public void Start(string micId, string outId)
    {
        if (Running) return;
        stop = false; Running = true;
        thread = new Thread(() => Run(micId, outId)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA); thread.Start();
    }
    public void Stop() { stop = true; }

    static void Fmt(IntPtr fmt, out int channels, out int rate, out int bits, out bool isFloat)
    {
        int tag = Marshal.ReadInt16(fmt, 0); channels = Marshal.ReadInt16(fmt, 2); rate = Marshal.ReadInt32(fmt, 4); bits = Marshal.ReadInt16(fmt, 14);
        isFloat = tag == 3 || (tag == unchecked((short)0xFFFE) && bits == 32);
    }
    void Run(string micId, string outId)
    {
        IAudioClientFull mic = null, outc = null;
        try
        {
            mic = Audio.Client(micId);
            IntPtr mf; mic.GetMixFormat(out mf);
            int mch, mrate, mbits; bool mfloat; Fmt(mf, out mch, out mrate, out mbits, out mfloat);
            if (mic.Initialize(0, 0, 200000, 0, mf, IntPtr.Zero) != 0) throw new Exception("Não deu para abrir o microfone.");
            var iidCap = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"); object o; mic.GetService(ref iidCap, out o);
            var cap = (IAudioCaptureClientFull)o;

            IAudioRenderClient ren = null; int och = 2, orate = 48000, obits = 32; bool ofloat = true; uint obuf = 0;
            if (outId != null)
            {
                outc = Audio.Client(outId);
                IntPtr of; outc.GetMixFormat(out of); Fmt(of, out och, out orate, out obits, out ofloat);
                if (outc.Initialize(0, 0, 400000, 0, of, IntPtr.Zero) == 0)
                {
                    var iidRen = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"); outc.GetService(ref iidRen, out o);
                    ren = (IAudioRenderClient)o; outc.GetBufferSize(out obuf); outc.Start();
                }
            }
            mic.Start(); Error = "";
            var bytes = new byte[0]; double resamplePos = 0; double ratio = (double)mrate / orate;
            var pending = new List<float>();
            while (!stop)
            {
                uint pkt; cap.GetNextPacketSize(out pkt);
                float framePeak = 0;
                while (pkt > 0)
                {
                    IntPtr data; uint frames, flags; ulong a, b;
                    cap.GetBuffer(out data, out frames, out flags, out a, out b);
                    int n = (int)frames * mch * (mbits / 8);
                    if (bytes.Length < n) bytes = new byte[n];
                    bool silent = (flags & 2) != 0;
                    if (!silent) Marshal.Copy(data, bytes, 0, n);
                    for (int f = 0; f < frames; f++)
                    {
                        float v = 0;
                        if (!silent) for (int c = 0; c < mch; c++) v += mfloat ? BitConverter.ToSingle(bytes, (f * mch + c) * 4) : BitConverter.ToInt16(bytes, (f * mch + c) * 2) / 32768f;
                        v /= mch; framePeak = Math.Max(framePeak, Math.Abs(v));
                        if (Loopback && ren != null) pending.Add(v * LoopGain);
                    }
                    cap.ReleaseBuffer(frames); cap.GetNextPacketSize(out pkt);
                }
                Peak = Math.Max(framePeak, Peak * 0.85f);
                if (ren != null && pending.Count > 0)
                {
                    uint pad; outc.GetCurrentPadding(out pad);
                    int free = (int)(obuf - pad); int outFrames = Math.Min(free, (int)(pending.Count / ratio));
                    if (pad > obuf / 2) { pending.Clear(); resamplePos = 0; } // atraso grande demais: descarta para manter o retorno rápido
                    else if (outFrames > 0)
                    {
                        IntPtr buf; ren.GetBuffer((uint)outFrames, out buf);
                        var outBytes = new byte[outFrames * och * (obits / 8)];
                        for (int f = 0; f < outFrames; f++)
                        {
                            int src = Math.Min(pending.Count - 1, (int)resamplePos); float v = pending[src]; resamplePos += ratio;
                            for (int c = 0; c < och; c++)
                            {
                                if (ofloat) Array.Copy(BitConverter.GetBytes(v), 0, outBytes, (f * och + c) * 4, 4);
                                else Array.Copy(BitConverter.GetBytes((short)Math.Max(-32768, Math.Min(32767, v * 32767))), 0, outBytes, (f * och + c) * 2, 2);
                            }
                        }
                        Marshal.Copy(outBytes, 0, buf, outBytes.Length); ren.ReleaseBuffer((uint)outFrames, 0);
                        int used = Math.Min(pending.Count, (int)resamplePos); pending.RemoveRange(0, used); resamplePos -= used;
                    }
                }
                else if (!Loopback) pending.Clear();
                Thread.Sleep(5);
            }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally
        {
            try { if (mic != null) mic.Stop(); } catch { }
            try { if (outc != null) outc.Stop(); } catch { }
            Peak = 0; Running = false;
        }
    }
}

// ======================================================================= laboratório HID
static class NativeLab
{
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] b, int len);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] b, int len);
}

class HeadsetLab : IDisposable
{
    public const int VID = 0x040B, PID = 0x0897;
    public HidInfo Vendor, Consumer; public int InLen, OutLen;
    SafeFileHandle vh; readonly List<Thread> readers = new List<Thread>(); volatile bool closed;
    public event Action<string, byte[]> Received = delegate { };

    public static HeadsetLab Open()
    {
        var lab = new HeadsetLab();
        foreach (var i in Hid.List())
        {
            if (i.Vid != VID || i.Pid != PID) continue;
            if (i.UsagePage >= 0xFF00 || i.UsagePage == 0xF100) lab.Vendor = i;
            else if (i.UsagePage == 0x0C) lab.Consumer = i;
        }
        if (lab.Vendor == null) return null;
        lab.vh = Native.CreateFileW(lab.Vendor.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (lab.vh.IsInvalid) lab.vh = Native.CreateFileW(lab.Vendor.Path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        IntPtr p; var c = new HIDP_CAPS();
        if (Native.HidD_GetPreparsedData(lab.vh, out p)) { Native.HidP_GetCaps(p, ref c); Native.HidD_FreePreparsedData(p); }
        lab.InLen = c.InputReportByteLength; lab.OutLen = c.OutputReportByteLength;
        lab.Listen("porta interna", lab.Vendor.Path, lab.InLen);
        if (lab.Consumer != null) lab.Listen("botões de mídia", lab.Consumer.Path, 3);
        return lab;
    }
    void Listen(string name, string path, int len)
    {
        var t = new Thread(() =>
        {
            try
            {
                using (var h = Native.CreateFileW(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                using (var fs = new FileStream(h, FileAccess.Read, Math.Max(len, 1), false))
                {
                    var buf = new byte[Math.Max(len, 1)];
                    while (!closed) { int n = fs.Read(buf, 0, buf.Length); if (n <= 0) break; var m = new byte[n]; Array.Copy(buf, m, n); Received(name, m); }
                }
            }
            catch { }
        }) { IsBackground = true };
        t.Start(); readers.Add(t);
    }
    // só leitura: pede um relatório de entrada pelo canal de controle
    public byte[] GetInput(byte reportId)
    {
        var b = new byte[Math.Max(InLen, 2)]; b[0] = reportId;
        if (!NativeLab.HidD_GetInputReport(vh, b, b.Length)) throw new Exception("O headset não respondeu (erro " + Marshal.GetLastWin32Error() + ").");
        return b;
    }
    // envio experimental: o primeiro byte é o report ID; o resto é completado com zeros
    public void SendOutput(byte[] data)
    {
        var b = new byte[OutLen]; Array.Copy(data, b, Math.Min(data.Length, OutLen));
        if (!NativeLab.HidD_SetOutputReport(vh, b, b.Length)) throw new Exception("O headset recusou o envio (erro " + Marshal.GetLastWin32Error() + ").");
    }
    public void Dispose() { closed = true; if (vh != null) vh.Dispose(); }
}
