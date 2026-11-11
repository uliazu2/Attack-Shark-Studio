// H510 Studio — mixer por aplicativo (sessões de áudio do Windows) e equalizador pelo Equalizer APO.
// Nada aqui fala com o headset: o mixer usa o Windows e o equalizador escreve um arquivo do APO.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

// ======================================================================= sessões de áudio
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(IntPtr g, int flags, out IntPtr c);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr g, int flags, out IntPtr v);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator { [PreserveSig] int GetCount(out int n); [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s); }
[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
    [PreserveSig] int GetGroupingParam(out Guid g);
    [PreserveSig] int SetGroupingParam(ref Guid g, ref Guid ctx);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}
[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float v, ref Guid ctx);
    [PreserveSig] int GetMasterVolume(out float v);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
}

// um app que está tocando no headset (pode ter várias sessões, como um navegador)
class AppAudio
{
    public string Key, Name, ExePath; public bool System; public readonly List<ISimpleAudioVolume> Parts = new List<ISimpleAudioVolume>();
    public float Volume { get { float v = 1; if (Parts.Count > 0) Parts[0].GetMasterVolume(out v); return v; } }
    public bool Muted { get { bool m = false; if (Parts.Count > 0) Parts[0].GetMute(out m); return m; } }
    public void SetVolume(float v) { var g = Guid.Empty; v = Math.Max(0, Math.Min(1, v)); foreach (var p in Parts) p.SetMasterVolume(v, ref g); }
    public void SetMute(bool m) { var g = Guid.Empty; foreach (var p in Parts) p.SetMute(m, ref g); }
}

static class Mixer
{
    public static List<AppAudio> Apps(string deviceId)
    {
        var res = new List<AppAudio>(); if (deviceId == null) return res;
        IMMDeviceFull d; if (((IMMDeviceEnumeratorFull)new MMDeviceEnumeratorComObject()).GetDevice(deviceId, out d) != 0) return res;
        var iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"); object o;
        if (d.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return res;
        IAudioSessionEnumerator en; if (((IAudioSessionManager2)o).GetSessionEnumerator(out en) != 0) return res;
        int n; en.GetCount(out n);
        var byKey = new Dictionary<string, AppAudio>();
        for (int i = 0; i < n; i++)
        {
            IAudioSessionControl2 s; if (en.GetSession(i, out s) != 0 || s == null) continue;
            int state; s.GetState(out state); if (state == 2) continue; // sessão já encerrada
            uint pid; s.GetProcessId(out pid);
            bool sys = s.IsSystemSoundsSession() == 0;
            string name = null, path = null;
            if (sys) name = "Sons do sistema";
            else
            {
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    try { path = p.MainModule.FileName; var desc = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim(); } catch { }
                    if (name == null) name = p.ProcessName;
                }
                catch { continue; } // processo já fechou
                string disp; if (s.GetDisplayName(out disp) == 0 && !string.IsNullOrWhiteSpace(disp) && !disp.StartsWith("@")) name = disp;
            }
            string key = sys ? "#sys" : (path ?? name).ToLowerInvariant();
            AppAudio a; if (!byKey.TryGetValue(key, out a)) { a = new AppAudio { Key = key, Name = name, ExePath = path, System = sys }; byKey[key] = a; res.Add(a); }
            a.Parts.Add((ISimpleAudioVolume)s);
        }
        return res.OrderBy(a => a.System ? 0 : 1).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

// ======================================================================= equalizador (Equalizer APO)
// O app só escreve o próprio arquivo (h510-studio.txt). O config.txt do usuário recebe uma única
// linha "Include: h510-studio.txt" no final, depois de um backup, e o resto dele fica como está.
static class Apo
{
    public static readonly int[] Bands = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    const string FileName = "h510-studio.txt", IncludeLine = "Include: h510-studio.txt";

    public static string ConfigDir()
    {
        if (Override != null) return Override;
        try
        {
            using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\EqualizerAPO"))
            { var p = k == null ? null : k.GetValue("ConfigPath") as string; if (p != null && File.Exists(Path.Combine(p, "config.txt"))) return p; }
        }
        catch { }
        return null;
    }
    public static bool Installed { get { return ConfigDir() != null; } }
    static string Ours() { return Path.Combine(ConfigDir(), FileName); }
    static string Config() { return Path.Combine(ConfigDir(), "config.txt"); }

    // bloco que o app acrescenta no fim do config.txt; desligar tira exatamente ele
    const string NL = "\r\n";
    const string Block = NL + "# H510 Studio (pode apagar estas duas linhas para desligar)" + NL + IncludeLine + NL;
    public static string Override; // pasta de teste
    public static bool Linked() { return File.ReadAllText(Config()).Contains(IncludeLine); }

    public static void Link()
    {
        if (Linked()) return;
        var cfg = Config(); var bak = cfg + ".h510studio-backup";
        if (!File.Exists(bak)) File.Copy(cfg, bak);
        File.AppendAllText(cfg, Block, new UTF8Encoding(false));
    }
    public static void Unlink()
    {
        var cfg = Config(); if (!File.Exists(cfg)) return;
        var text = File.ReadAllText(cfg);
        if (text.EndsWith(Block)) text = text.Substring(0, text.Length - Block.Length);
        else if (text.Contains(IncludeLine)) text = string.Join(NL, text.Split(new[] { NL }, StringSplitOptions.None).Where(l => l.Trim() != IncludeLine && !l.StartsWith("# H510 Studio")).ToArray());
        else return;
        File.WriteAllText(cfg, text, new UTF8Encoding(false));
    }

    public static void Write(bool on, double[] gains)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# Gerado pelo H510 Studio. Mude pelo app; o que for escrito aqui à mão será trocado.");
        sb.AppendLine("# ganhos: " + string.Join(" ", gains.Select(g => g.ToString("0.0", inv)).ToArray()));
        sb.AppendLine("# ligado: " + (on ? "sim" : "não"));
        if (on)
        {
            double top = Math.Max(0, gains.Max());
            sb.AppendLine("Preamp: " + (-top).ToString("0.0", inv) + " dB"); // evita distorção quando alguma banda sobe
            sb.AppendLine("GraphicEQ: " + string.Join("; ", Bands.Select((f, i) => f + " " + gains[i].ToString("0.0", inv)).ToArray()));
        }
        File.WriteAllText(Ours(), sb.ToString(), new UTF8Encoding(false));
    }
    public static bool Read(double[] gains)
    {
        try
        {
            var path = Ours(); if (!File.Exists(path)) return false; bool on = false;
            foreach (var l in File.ReadAllLines(path))
            {
                if (l.StartsWith("# ganhos: ")) { var p = l.Substring(10).Split(' '); for (int i = 0; i < gains.Length && i < p.Length; i++) double.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out gains[i]); }
                if (l.StartsWith("# ligado: ")) on = l.Substring(10).Trim() == "sim";
            }
            return on && Linked();
        }
        catch { return false; }
    }

    public static readonly string[] PresetNames = { "Neutro", "Graves", "Jogos", "Voz", "Música" };
    public static readonly double[][] Presets = {
        new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        new double[] { 6, 5, 3, 1, 0, 0, 0, 0, 0, 0 },
        new double[] { -2, -1, 0, 0, 0, 1, 3, 4, 2, 0 },   // passos e tiros mais claros
        new double[] { -4, -3, -1, 0, 1, 3, 3, 2, 0, -1 },
        new double[] { 3, 2, 1, 0, -1, 0, 1, 2, 3, 2 },
    };
}
