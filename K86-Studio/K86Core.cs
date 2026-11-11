// K86 Studio — núcleo: comunicação com o teclado (HID), protocolo, som/tela do PC e operações.
// A interface fica em K86App.cs. Detalhes do protocolo no topo de K86App.cs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

// ======================================================================= HID
class K86Device : IDisposable
{
    SafeFileHandle h; int featLen; public HidInfo Info;
    public static K86Device Find()
    {
        var all = Hid.List();
        all.Sort((a, b) => (a.Pid == 0x4015 ? 0 : 1) - (b.Pid == 0x4015 ? 0 : 1)); // cabo primeiro
        foreach (var i in all)
        {
            if (i.Vid != 0x3151 || i.UsagePage != 0xFFFF || i.FeatLen < 65) continue;
            if (i.Pid != 0x4015) continue; // pelo receptor 2.4G as leituras voltam vazias: só o cabo é suportado
            var h = Native.CreateFileW(i.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) h = Native.CreateFileW(i.Path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) continue;
            return new K86Device { h = h, featLen = i.FeatLen, Info = i };
        }
        return null;
    }
    public void Send(byte[] b64, int delayMs)
    {
        var buf = new byte[featLen]; Array.Copy(b64, 0, buf, 1, Math.Min(64, featLen - 1));
        if (!Native.HidD_SetFeature(h, buf, buf.Length)) throw new Exception("O teclado recusou o comando (erro " + Marshal.GetLastWin32Error() + "). Ele está ligado no cabo?");
        if (delayMs > 0) Thread.Sleep(delayMs);
    }
    public void Send(byte[] b64) { Send(b64, 20); }
    public byte[] Read()
    {
        var buf = new byte[featLen];
        if (!Native.HidD_GetFeature(h, buf, buf.Length)) throw new Exception("Não deu para ler do teclado (erro " + Marshal.GetLastWin32Error() + ").");
        var r = new byte[64]; Array.Copy(buf, 1, r, 0, Math.Min(64, featLen - 1));
        return r;
    }
    public byte[] Query(byte[] pkt) { Send(pkt, 15); return Read(); }
    public void Dispose() { if (h != null) h.Dispose(); }
}

// ======================================================================= protocolo
static class P
{
    public static byte[] B7(params int[] head) { return Pkt(head, 7, null); }
    public static byte[] Pkt(int[] head, int chkAt, byte[] data)
    {
        var b = new byte[64]; for (int i = 0; i < head.Length; i++) b[i] = (byte)head[i];
        int s = 0; for (int i = 0; i < chkAt; i++) s += b[i];
        b[chkAt] = (byte)(0xFF - (s & 0xFF));
        if (data != null) Array.Copy(data, 0, b, chkAt + 1, Math.Min(data.Length, 63 - chkAt));
        return b;
    }
    public static string Hex(byte[] b, int n) { var sb = new StringBuilder(); for (int i = 0; i < Math.Min(n, b.Length); i++) { if (i > 0) sb.Append(' '); sb.Append(b[i].ToString("x2")); } return sb.ToString(); }

    public static byte[] Effect(int mode, int bright, int speedPos, int option, bool rainbow, int r, int g, int b)
    {
        int flags = (option << 4) | (rainbow ? 8 : 7);
        if (mode == 13) { flags = option << 4; r = 0; g = 200; b = 200; }
        if (mode == 21) flags = 0;
        if (mode == 20 || mode == 22) flags = (option << 4) | (rainbow ? 0 : 4);
        if (r == 255 && g == 255 && b == 255) { r = 0xFA; g = 0xFF; b = 0xFA; } // o firmware usa este "branco"
        return Pkt(new[] { 0x07, mode, 4 - speedPos, bright, flags, r, g, b }, 8, null);
    }

    // LEDs: 21 colunas x 6 linhas, índice = coluna*6 + linha (o mesmo índice do mapa de teclas)
    public static List<byte[]> PerKey(int[] rgb) // rgb[126] em 0xRRGGBB, -1 = apagado
    {
        var s = new byte[392];
        for (int i = 0; i < 126 && i < rgb.Length; i++) { int c = Math.Max(0, rgb[i]); s[i * 3] = (byte)(c >> 16); s[i * 3 + 1] = (byte)(c >> 8); s[i * 3 + 2] = (byte)c; }
        var list = new List<byte[]>();
        for (int p = 0; p < 7; p++) { var chunk = new byte[56]; Array.Copy(s, p * 56, chunk, 0, 56); list.Add(Pkt(new[] { 0x0c, 0, 0x80, 1, p, 0, 0 }, 7, chunk)); }
        return list;
    }

    // mapa de teclas de fábrica do K86 (conferido com o teclado: 92/92), 4 bytes por posição
    public static readonly byte[] DefaultMatrix = ParseCsv("0,0,41,0,0,0,53,0,0,0,43,0,0,0,57,0,0,0,225,0,0,0,224,0,0,0,58,0,0,0,30,0,0,0,20,0,0,0,4,0,0,0,100,0,0,0,0,0,0,0,59,0,0,0,31,0,0,0,26,0,0,0,22,0,0,0,29,0,0,0,227,0,0,0,60,0,0,0,32,0,0,0,8,0,0,0,7,0,0,0,27,0,0,0,226,0,0,0,61,0,0,0,33,0,0,0,21,0,0,0,9,0,0,0,6,0,0,0,0,0,0,0,62,0,0,0,34,0,0,0,23,0,0,0,10,0,0,0,25,0,0,0,0,0,0,0,63,0,0,0,35,0,0,0,28,0,0,0,11,0,0,0,5,0,0,0,44,0,0,0,64,0,0,0,36,0,0,0,24,0,0,0,13,0,0,0,17,0,0,0,0,0,0,0,65,0,0,0,37,0,0,0,12,0,0,0,14,0,0,0,16,0,0,0,0,0,0,0,66,0,0,0,38,0,0,0,18,0,0,0,15,0,0,0,54,0,0,0,230,0,0,0,67,0,0,0,39,0,0,0,19,0,0,0,51,0,0,0,55,0,10,1,0,0,0,0,68,0,0,0,45,0,0,0,47,0,0,0,52,0,0,0,56,0,0,0,101,0,0,0,69,0,0,0,46,0,0,0,48,0,0,0,50,0,0,0,229,0,0,0,228,0,3,0,226,0,0,0,42,0,0,0,49,0,0,0,40,0,0,0,0,0,0,0,80,0,0,0,70,0,0,0,73,0,0,0,76,0,0,0,72,0,0,0,82,0,0,0,81,0,0,0,71,0,0,0,74,0,0,0,77,0,0,0,75,0,0,0,78,0,0,0,79,0,3,0,234,0,3,0,233,0");
    static byte[] ParseCsv(string s) { var parts = s.Split(','); var b = new byte[512]; for (int i = 0; i < parts.Length && i < 512; i++) b[i] = byte.Parse(parts[i]); return b; }
    public static byte[] SetKey(int profile, int idx, byte[] code) { return Pkt(new[] { 0x13, profile, idx, 0, 0, 0, 0 }, 7, code); }

    public class MacroEv { public int Hid; public bool Down; public int Delay; }
    public static List<byte[]> MacroPackets(int idx, int repeat, List<MacroEv> evs)
    {
        var buf = new List<byte>(); buf.Add((byte)(repeat & 255)); buf.Add((byte)(repeat >> 8));
        foreach (var e in evs)
        {
            int d = Math.Max(1, Math.Min(65535, e.Delay));
            buf.Add((byte)e.Hid);
            if (d <= 127) buf.Add((byte)(e.Down ? d + 128 : d));
            else { buf.Add((byte)(e.Down ? 128 : 0)); buf.Add((byte)(d & 255)); buf.Add((byte)(d >> 8)); }
        }
        if (buf.Count > 256) throw new Exception("A macro ficou grande demais (máximo de cerca de 120 eventos).");
        var all = new byte[280]; buf.CopyTo(all);
        int pages = 0; for (int p = 0; p < 5; p++) { bool any = false; for (int i = p * 56; i < p * 56 + 56; i++) if (all[i] != 0) any = true; if (any) pages = p + 1; }
        var list = new List<byte[]>();
        for (int p = 0; p < pages; p++)
        {
            var chunk = new byte[56]; Array.Copy(all, p * 56, chunk, 0, 56);
            list.Add(Pkt(new[] { 0x0B, idx, p, 56, p == pages - 1 ? 1 : 0, 0, 0 }, 7, chunk));
        }
        return list;
    }

    public const int SW = 240, SH = 135;
    public static byte[] ScreenPrepare(int frameIdx, int frames, int delay, int len, int slot)
    {
        var b = Pkt(new[] { 0xA5, frameIdx, frames, delay, len & 255, (len >> 8) & 255, 0 }, 7, null);
        b[8] = 0; b[9] = 0; b[10] = SW & 255; b[11] = SH & 255; b[12] = 0; b[13] = 0; b[14] = SW >> 8; b[15] = SH >> 8;
        b[16] = (byte)((len >> 16) & 255); b[17] = (byte)((len >> 24) & 255); b[18] = (byte)slot;
        return b;
    }
    public static byte[] ScreenChunk(int frameIdx, int frames, int delay, int page, byte[] data)
    {
        var chunk = new byte[56]; int cnt = Math.Min(56, data.Length - page * 56); Array.Copy(data, page * 56, chunk, 0, cnt);
        return Pkt(new[] { 0x25, frameIdx, frames, delay, page & 255, page >> 8, cnt }, 7, chunk);
    }
    public static byte[] Clock(DateTime t)
    {
        var b = B7(0x28);
        b[8] = (byte)(t.Year >> 8); b[9] = (byte)(t.Year & 255); b[10] = (byte)t.Month; b[11] = (byte)t.Day; b[12] = (byte)t.Hour; b[13] = (byte)t.Minute; b[14] = (byte)t.Second;
        return b;
    }
    public static byte[] Sleep(int bt, int g24, int deepBt, int deep24)
    {
        var b = B7(0x12);
        b[8] = (byte)(bt & 255); b[9] = (byte)(bt >> 8); b[10] = (byte)(g24 & 255); b[11] = (byte)(g24 >> 8);
        b[12] = (byte)(deepBt & 255); b[13] = (byte)(deepBt >> 8); b[14] = (byte)(deep24 & 255); b[15] = (byte)(deep24 >> 8);
        return b;
    }
    public static byte[] Music(byte[] levels) { return Pkt(new[] { 0x0D, 0, 0, 0, 0, 0, 0 }, 7, levels); }
    public static byte[] ScreenColor(int r, int g, int b) { return Pkt(new[] { 0x0E, r, g, b, 0xFF, 0, 0 }, 7, null); }
}

// ======================================================================= som do PC (WASAPI loopback)
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice dev);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o); }
[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient
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
interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devPos, out ulong qpc);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint n);
}

class LightSync
{
    public const int Columns = 21;
    public volatile string Mode = "off"; // off | music | screen
    public volatile float[] Levels = new float[Columns];
    public volatile int[] ScreenRgb = new int[3];
    public string LastError = "";
    readonly Backend app;
    public LightSync(Backend a) { app = a; var t = new Thread(Loop) { IsBackground = true }; t.SetApartmentState(ApartmentState.MTA); t.Start(); }

    void Loop()
    {
        while (true)
        {
            try
            {
                if (Mode == "music") RunMusic();
                else if (Mode == "screen") RunScreen();
                else Thread.Sleep(150);
            }
            catch (Exception ex) { LastError = ex.Message; Thread.Sleep(1000); }
        }
    }

    void RunScreen()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        using (var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb))
        using (var g = Graphics.FromImage(bmp))
        {
            int lr = -1, lg = -1, lb = -1;
            while (Mode == "screen")
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                long r = 0, gg = 0, b = 0, n = 0;
                try
                {
                    var row = new byte[data.Stride];
                    for (int y = 0; y < bmp.Height; y += 12)
                    {
                        Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, data.Stride);
                        for (int x = 0; x < bmp.Width; x += 12) { b += row[x * 4]; gg += row[x * 4 + 1]; r += row[x * 4 + 2]; n++; }
                    }
                }
                finally { bmp.UnlockBits(data); }
                int R = (int)(r / n), G = (int)(gg / n), B = (int)(b / n);
                // deixa a cor mais viva (a média da tela costuma ser acinzentada)
                int mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                if (mx > 0 && mx - mn > 8) { double k = 255.0 / mx; R = (int)Math.Min(255, (R - mn * .6) * k); G = (int)Math.Min(255, (G - mn * .6) * k); B = (int)Math.Min(255, (B - mn * .6) * k); }
                ScreenRgb = new[] { R, G, B };
                if (Math.Abs(R - lr) + Math.Abs(G - lg) + Math.Abs(B - lb) > 6) { app.TrySend(P.ScreenColor(R, G, B)); lr = R; lg = G; lb = B; }
                Thread.Sleep(90);
            }
        }
    }

    void RunMusic()
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
        IMMDevice dev; Check(en.GetDefaultAudioEndpoint(0, 0, out dev), "Nenhuma saída de som encontrada.");
        var iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"); object o;
        Check(dev.Activate(ref iidClient, 23, IntPtr.Zero, out o), "Não deu para abrir a saída de som.");
        var client = (IAudioClient)o;
        IntPtr fmt; Check(client.GetMixFormat(out fmt), "Formato de som desconhecido.");
        int tag = Marshal.ReadInt16(fmt, 0), channels = Marshal.ReadInt16(fmt, 2), rate = Marshal.ReadInt32(fmt, 4), bits = Marshal.ReadInt16(fmt, 14);
        bool isFloat = tag == 3 || (tag == unchecked((short)0xFFFE) && bits == 32);
        Check(client.Initialize(0, 0x00020000, 2000000, 0, fmt, IntPtr.Zero), "Não deu para captar o som do PC.");
        var iidCap = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        Check(client.GetService(ref iidCap, out o), "Não deu para captar o som do PC.");
        var cap = (IAudioCaptureClient)o;
        client.Start();
        LastError = "";
        const int N = 1024;
        var ring = new float[N]; int ringPos = 0;
        var re = new double[N]; var im = new double[N]; var win = new double[N];
        for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1));
        // faixas de frequência em escala logarítmica (40 Hz .. 12 kHz)
        var edges = new int[Columns + 1];
        for (int c = 0; c <= Columns; c++) { double f = 40 * Math.Pow(12000.0 / 40, (double)c / Columns); edges[c] = Math.Max(1, Math.Min(N / 2 - 1, (int)Math.Round(f * N / rate))); }
        var smooth = new double[Columns]; double peak = 1e-3; var sw = Stopwatch.StartNew(); long lastSend = 0;
        var tmp = new byte[8192 * 8];
        try
        {
            while (Mode == "music")
            {
                uint pkt; cap.GetNextPacketSize(out pkt);
                while (pkt > 0)
                {
                    IntPtr data; uint frames, flags; ulong a, b;
                    cap.GetBuffer(out data, out frames, out flags, out a, out b);
                    int bytes = (int)frames * channels * (bits / 8);
                    if (tmp.Length < bytes) tmp = new byte[bytes];
                    bool silent = (flags & 2) != 0;
                    if (!silent) Marshal.Copy(data, tmp, 0, bytes);
                    for (int f = 0; f < frames; f++)
                    {
                        float v = 0;
                        if (!silent)
                            for (int ch = 0; ch < channels; ch++)
                                v += isFloat ? BitConverter.ToSingle(tmp, (f * channels + ch) * 4) : BitConverter.ToInt16(tmp, (f * channels + ch) * 2) / 32768f;
                        ring[ringPos] = v / channels; ringPos = (ringPos + 1) % N;
                    }
                    cap.ReleaseBuffer(frames);
                    cap.GetNextPacketSize(out pkt);
                }
                if (sw.ElapsedMilliseconds - lastSend >= 33)
                {
                    lastSend = sw.ElapsedMilliseconds;
                    for (int i = 0; i < N; i++) { re[i] = ring[(ringPos + i) % N] * win[i]; im[i] = 0; }
                    Fft(re, im);
                    var bands = new double[Columns]; double frameMax = 0;
                    for (int c = 0; c < Columns; c++)
                    {
                        double m = 0; int lo = edges[c], hi = Math.Max(edges[c] + 1, edges[c + 1]);
                        for (int k = lo; k < hi; k++) m = Math.Max(m, Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                        m *= 1 + c * 0.12; // compensa os agudos, que têm menos energia
                        bands[c] = m; frameMax = Math.Max(frameMax, m);
                    }
                    peak = Math.Max(frameMax, peak * 0.985); if (peak < 0.02) peak = 0.02;
                    var lv = new byte[Columns]; var fl = new float[Columns];
                    for (int c = 0; c < Columns; c++)
                    {
                        double x = Math.Sqrt(bands[c] / peak);
                        smooth[c] = x > smooth[c] ? x : smooth[c] * 0.80 + x * 0.20;
                        fl[c] = (float)smooth[c];
                        lv[c] = (byte)Math.Max(0, Math.Min(6, Math.Round(smooth[c] * 6)));
                    }
                    Levels = fl;
                    app.TrySend(P.Music(lv));
                }
                Thread.Sleep(8);
            }
        }
        finally
        {
            client.Stop();
            app.TrySend(P.Music(new byte[Columns]));
            Levels = new float[Columns];
            Marshal.ReleaseComObject(cap); Marshal.ReleaseComObject(client); Marshal.ReleaseComObject(dev); Marshal.ReleaseComObject(en);
        }
    }
    static void Check(int hr, string msg) { if (hr != 0) throw new Exception(msg + " (0x" + hr.ToString("x8") + ")"); }
    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                    double t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                }
            }
        }
    }
}

