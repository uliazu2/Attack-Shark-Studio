// X11 Studio — núcleo do mouse Attack Shark X11 (VID 0x1D57; receptor 2.4G PID 0xFA60, cabo PID 0xFA55).
// Protocolo adaptado do projeto MIT "attack-shark-x11-driver" (github.com/HarukaYamamoto0/attack-shark-x11-driver).
//
// Interface 2 do receptor:
//   coleção UsagePage 0x0B  -> comandos (relatórios de feature com report ID, 64 bytes no Windows)
//   coleção UsagePage 0x0A  -> mensagens (5 bytes): bateria (0x40/0x41), confirmação (0x50), perfil (0x80)
// Escrita: SetFeature com o pacote do comando. Leitura: pede permissão [A0, id, tamanho, 0, parâmetro, 0,0,0],
// espera ~250 ms, confere GetFeature(A0)[1] == 1 e então faz GetFeature(id).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class X11
{
    public const int VID = 0x1D57, PID_WIRELESS = 0xFA60, PID_WIRED = 0xFA55;
    public const byte R_DPI = 0x04, R_LIGHT = 0x05, R_POLL = 0x06, R_BUTTONS = 0x08, R_MACRO = 0x09, R_PROFILE = 0x0A, R_PERMISSION = 0xA0;
    public const int L_DPI = 0x38, L_LIGHT = 0x0F, L_POLL = 0x09, L_BUTTONS = 0x3B, L_PROFILE = 0x0A;

    // tabela do sensor PAW3311 (índice -> byte), do projeto original
    public static readonly byte[] DpiMap = {
        0x01,0x02,0x03,0x04,0x05,0x06,0x08,0x09,0x0a,0x0b,0x0c,0x0e,0x0f,0x10,0x11,0x12,0x13,0x15,0x16,0x17,0x18,0x19,0x1b,0x1c,0x1d,0x1e,0x1f,0x20,0x22,0x23,0x24,0x25,0x26,0x27,0x29,0x2a,0x2b,0x2c,
        0x2d,0x2f,0x30,0x31,0x32,0x33,0x34,0x36,0x37,0x38,0x39,0x3a,0x3b,0x3d,0x3e,0x3f,0x40,0x41,0x43,0x44,0x45,0x46,0x47,0x48,0x4a,0x4b,0x4c,0x4d,0x4e,0x4f,0x51,0x52,0x53,0x54,0x55,0x57,0x58,0x59,
        0x5a,0x5b,0x5c,0x5e,0x5f,0x60,0x61,0x62,0x63,0x65,0x66,0x67,0x68,0x69,0x6b,0x6c,0x6d,0x6e,0x6f,0x70,0x72,0x73,0x74,0x75,0x76,0x77,0x79,0x7a,0x7b,0x7c,0x7d,0x7f,0x80,0x81,0x82,0x83,0x84,0x86,
        0x87,0x88,0x89,0x8a,0x8b,0x8d,0x8e,0x8f,0x90,0x91,0x93,0x94,0x95,0x96,0x97,0x98,0x9a,0x9b,0x9c,0x9d,0x9e,0x9f,0xa1,0xa2,0xa3,0xa4,0xa5,0xa7,0xa8,0xa9,0xaa,0xab,0xac,0xae,0xaf,0xb0,0xb1,0xb2,
        0xb3,0xb5,0xb6,0xb7,0xb8,0xb9,0xbb,0xbc,0xbd,0xbe,0xbf,0xc0,0xc2,0xc3,0xc4,0xc5,0xc6,0xc7,0xc9,0xca,0xcb,0xcc,0xcd,0xcf,0xd0,0xd1,0xd2,0xd3,0xd4,0xd6,0xd7,0xd8,0xd9,0xda,0xdb,0xdd,0xde,0xdf,
        0xe0,0xe1,0xe3,0xe4,0xe5,0xe6,0xe7,0xe8,0xea,0xeb,0x76,0x77,0x79,0x7a,0x7b,0x7c,0x7d,0x7f,0x80,0x81,0x82,0x83,0x84,0x86,0x87,0x88,0x89,0x8a,0x8b,0x8d };

    public static void DpiToBytes(int value, out byte low, out byte high)
    {
        int v = Math.Max(50, value), dpi = v;
        if (v >= 24100 && v <= 25000) dpi = Math.Max(8000, Math.Min(8500, (int)Math.Round(v / 3.0)));
        else if (v > 24000) dpi = 8500;
        else if (v > 12000) dpi = (int)Math.Round(v / 2.0);
        if (dpi <= 10000) dpi = Math.Min(10000, Math.Max(50, (int)Math.Round(dpi / 50.0) * 50));
        else dpi = Math.Max(10100, (int)Math.Round(dpi / 100.0) * 100);
        high = (byte)(dpi <= 10000 ? 0 : 1);
        int index = dpi <= 10000 ? (dpi - 50) / 50 : 200 + (dpi - 10100) / 100;
        if (index < 0 || index >= DpiMap.Length) throw new Exception("DPI não suportado: " + value);
        low = DpiMap[index];
    }
    public static int BytesToDpi(byte x, byte y, bool isDouble)
    {
        if (x == 0 && y == 0) return 0;
        int v;
        if (y == 0) { int i = Array.IndexOf(DpiMap, x); v = i != -1 ? i * 50 + 50 : 50; }
        else if (y == 1) { int i = Array.IndexOf(DpiMap, x); v = i != -1 ? i * 100 + 100 : 100; }
        else { int combined = (y << 8) | x; v = 10100 + (combined - 199) * 100; }
        if (isDouble) { if (x == 235) return 20100; v *= 2; }
        return Math.Min(v, 26000);
    }
    // valores que o sensor realmente aceita (para o controle deslizante)
    public static int SnapDpi(int v)
    {
        if (v <= 10000) return Math.Max(50, (int)Math.Round(v / 50.0) * 50);
        if (v <= 12000) return (int)Math.Round(v / 100.0) * 100;
        return Math.Min(22000, (int)Math.Round(v / 200.0) * 200);
    }
}

class DpiSettings
{
    public int Profile = 1, LiftOff, ActiveStages = 0x3F, CurrentStage = 2, Indicator = 2;
    public bool MotionSync, AngleSnap, Ripple = true;
    public int[] Dpi = { 800, 1600, 2400, 3200, 5000, 22000, 0, 0 };
    public int[] Color = { 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0x00FFFF, 0xFF00FF, 0xFF4000, 0xFFFFFF };

    public byte[] Build()
    {
        var b = new byte[X11.L_DPI];
        b[0] = X11.R_DPI; b[1] = X11.L_DPI; b[2] = (byte)Profile;
        b[3] = (byte)((LiftOff << 4) | (AngleSnap ? 1 : 0));
        b[4] = (byte)(((MotionSync ? 1 : 0) << 4) | (Ripple ? 1 : 0));
        b[5] = (byte)ActiveStages;
        int dbl = 0, tri = 0;
        for (int i = 0; i < 8; i++)
        {
            byte lo = 0, hi = 0;
            if (Dpi[i] > 0) X11.DpiToBytes(Dpi[i], out lo, out hi);
            b[8 + i] = lo; b[16 + i] = hi;
            if (Dpi[i] > 12000 && Dpi[i] < 24000) dbl |= 1 << i;
            if (Dpi[i] >= 24100 && Dpi[i] <= 25000) tri |= 1 << i;
        }
        b[6] = (byte)dbl; b[7] = (byte)(dbl | tri); // byte 6 = X dobrado, byte 7 = Y dobrado
        b[24] = (byte)CurrentStage;
        for (int i = 0; i < 8; i++) { b[25 + i * 3] = (byte)(Color[i] >> 16); b[26 + i * 3] = (byte)(Color[i] >> 8); b[27 + i * 3] = (byte)Color[i]; }
        b[49] = (byte)Indicator;
        int sum = 0; for (int i = 3; i <= 49; i++) sum += b[i];
        b[50] = (byte)(sum >> 8); b[51] = (byte)sum;
        return b;
    }
    public static DpiSettings Parse(byte[] r)
    {
        int sum = 0; for (int i = 3; i <= 49; i++) sum += r[i];
        if (((r[50] << 8) | r[51]) != (sum & 0xFFFF)) throw new Exception("Resposta de DPI com checksum inválido.");
        var d = new DpiSettings { Profile = r[2], LiftOff = r[3] >> 4, AngleSnap = (r[3] & 15) != 0, MotionSync = (r[4] >> 4) != 0, Ripple = (r[4] & 15) != 0,
            ActiveStages = r[5], CurrentStage = r[24], Indicator = r[49] };
        for (int i = 0; i < 8; i++)
        {
            d.Dpi[i] = X11.BytesToDpi(r[8 + i], r[16 + i], (r[6] & (1 << i)) != 0);
            d.Color[i] = (r[25 + i * 3] << 16) | (r[26 + i * 3] << 8) | r[27 + i * 3];
        }
        return d;
    }
}

class LightSettings
{
    // modo: 0 desligado, 1 fixo, 2 respiração, 3 neon, 4 respiração colorida, 5 fixo (cor do DPI), 6 respiração (cor do DPI)
    public int Profile = 1, Mode = 1, Speed = 3, Brightness = 8, DeepSleepMin = 10, KeyResponseMs = 8, Color = 0x00FF00;
    public double SleepMin = 0.5;
    public byte[] Build()
    {
        var b = new byte[X11.L_LIGHT];
        b[0] = X11.R_LIGHT; b[1] = X11.L_LIGHT; b[2] = (byte)Profile;
        b[3] = (byte)(Mode << 4);
        b[4] = (byte)((DeepSleepMin & 0xF0) | (Speed & 0x0F));
        b[5] = (byte)(((DeepSleepMin & 0x0F) << 4) | (Brightness & 0x0F));
        b[6] = (byte)(Color >> 16); b[7] = (byte)(Color >> 8); b[8] = (byte)Color;
        b[9] = (byte)Math.Round(SleepMin * 2);
        b[10] = (byte)(Math.Max(4, Math.Min(50, KeyResponseMs)) / 2);
        int sum = 0; for (int i = 3; i <= 10; i++) sum += b[i];
        b[11] = (byte)(sum >> 8); b[12] = (byte)sum;
        return b;
    }
    public static LightSettings Parse(byte[] r)
    {
        int sum = 0; for (int i = 3; i <= 10; i++) sum += r[i];
        if (((r[11] << 8) | r[12]) != (sum & 0xFFFF)) throw new Exception("Resposta de iluminação com checksum inválido.");
        return new LightSettings { Profile = r[2], Mode = r[3] >> 4, Speed = r[4] & 15, Brightness = r[5] & 15,
            DeepSleepMin = (r[4] & 0xF0) | (r[5] >> 4), Color = (r[6] << 16) | (r[7] << 8) | r[8], SleepMin = r[9] / 2.0, KeyResponseMs = r[10] * 2 };
    }
}

static class Poll
{
    public static readonly int[] Rates = { 125, 250, 500, 1000 };
    static int Code(int hz) { return hz == 125 ? 8 : hz == 250 ? 4 : hz == 500 ? 2 : 1; }
    public static byte[] Build(int profile, int hz) { var c = Code(hz); return new byte[] { X11.R_POLL, X11.L_POLL, (byte)profile, (byte)c, (byte)(c ^ 0xFF), 0, 0, 0, 0 }; }
    public static int Parse(byte[] r) { switch (r[3]) { case 8: return 125; case 4: return 250; case 2: return 500; default: return 1000; } }
}

class ButtonSlot { public int Action, Mods, Key; public ButtonSlot(int a, int m, int k) { Action = a; Mods = m; Key = k; } }

static class Buttons
{
    // posições físicas do X11 (slots do firmware)
    public static readonly int[] PhysicalSlots = { 1, 2, 3, 7, 8, 6 };
    public static readonly string[] PhysicalNames = { "Botão esquerdo", "Botão direito", "Clique da roda", "Lateral da frente", "Lateral de trás", "Botão de DPI" };
    public static ButtonSlot[] Defaults()
    {
        var s = new ButtonSlot[18];
        for (int i = 0; i < 18; i++) s[i] = new ButtonSlot(0x01, 0, 0);
        s[0] = new ButtonSlot(0x02, 0, 0); s[1] = new ButtonSlot(0x03, 0, 0); s[2] = new ButtonSlot(0x04, 0, 0);
        s[5] = new ButtonSlot(0x0D, 0, 0); s[6] = new ButtonSlot(0x06, 0, 0); s[7] = new ButtonSlot(0x05, 0, 0);
        s[16] = new ButtonSlot(0x09, 0, 0); s[17] = new ButtonSlot(0x0A, 0, 0);
        return s;
    }
    public static byte[] Build(int profile, ButtonSlot[] slots)
    {
        var b = new byte[X11.L_BUTTONS];
        b[0] = X11.R_BUTTONS; b[1] = X11.L_BUTTONS; b[2] = (byte)profile;
        for (int i = 0; i < 18; i++) { int o = 3 + i * 3; b[o] = (byte)slots[i].Action; b[o + 1] = (byte)slots[i].Mods; b[o + 2] = (byte)slots[i].Key; }
        int sum = 0; for (int i = 0; i < 54; i++) sum += b[3 + i];
        b[57] = (byte)((sum >> 8) & 0xFF); b[58] = (byte)sum;
        return b;
    }
    public static ButtonSlot[] Parse(byte[] r)
    {
        var s = new ButtonSlot[18];
        for (int i = 0; i < 18; i++) { int o = 3 + i * 3; s[i] = new ButtonSlot(r[o], r[o + 1], r[o + 2]); }
        return s;
    }
    // ações do firmware: (código, nome)
    public static readonly object[][] Actions = {
        new object[]{0x02,"Clique esquerdo"}, new object[]{0x03,"Clique direito"}, new object[]{0x04,"Clique do meio"}, new object[]{0x06,"Avançar"}, new object[]{0x05,"Voltar"},
        new object[]{0x07,"Clique duplo"}, new object[]{0x08,"Tiro rápido (segura = vários cliques)"}, new object[]{0x09,"Rolar para cima"}, new object[]{0x0A,"Rolar para baixo"},
        new object[]{0x0D,"Trocar DPI"}, new object[]{0x0E,"DPI +"}, new object[]{0x0F,"DPI −"}, new object[]{0x10,"DPI de mira (segurar)"},
        new object[]{0x28,"Trocar taxa de resposta"}, new object[]{0x34,"Trocar perfil"}, new object[]{0x29,"Trocar efeito de luz"}, new object[]{0x30,"Liga/desliga luz"},
        new object[]{0x18,"Play/Pause"}, new object[]{0x17,"Próxima faixa"}, new object[]{0x16,"Faixa anterior"}, new object[]{0x1A,"Mudo"}, new object[]{0x1B,"Volume +"}, new object[]{0x1C,"Volume −"},
        new object[]{0x1D,"Calculadora"}, new object[]{0x23,"Meu computador"}, new object[]{0x1E,"E-mail"}, new object[]{0x25,"Navegador"}, new object[]{0x14,"Menu Iniciar"},
        new object[]{0x01,"Desativado"} };
    public const int KEYBOARD = 0x11;
}

class X11Device : IDisposable
{
    SafeFileHandle cmd, events; int featLen; public int Pid;
    public Action<byte[]> OnMessage = delegate { };
    Thread reader; volatile bool closed;

    public static X11Device Open()
    {
        HidInfo c = null, e = null;
        foreach (var i in Hid.List())
        {
            if (i.Vid != X11.VID || (i.Pid != X11.PID_WIRELESS && i.Pid != X11.PID_WIRED)) continue;
            if (i.UsagePage == 0x0B && i.FeatLen > 0 && c == null) c = i;
            if (i.UsagePage == 0x0A && e == null) e = i;
        }
        if (c == null) return null;
        var d = new X11Device { featLen = c.FeatLen, Pid = c.Pid };
        d.cmd = Native.CreateFileW(c.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (d.cmd.IsInvalid) return null;
        if (e != null)
        {
            d.events = Native.CreateFileW(e.Path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (!d.events.IsInvalid) { d.reader = new Thread(d.ReadLoop) { IsBackground = true }; d.reader.Start(); }
        }
        return d;
    }
    void ReadLoop()
    {
        try
        {
            using (var fs = new FileStream(events, FileAccess.Read, 64, false))
            {
                var buf = new byte[64];
                while (!closed)
                {
                    int n = fs.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    var msg = new byte[n]; Array.Copy(buf, msg, n); OnMessage(msg);
                }
            }
        }
        catch { }
    }
    public void Send(byte[] packet)
    {
        var buf = new byte[featLen]; Array.Copy(packet, buf, Math.Min(packet.Length, featLen));
        if (!Native.HidD_SetFeature(cmd, buf, buf.Length)) throw new Exception("O mouse recusou o comando (erro " + Marshal.GetLastWin32Error() + "). Ele está ligado?");
    }
    public byte[] Read(byte reportId, int len, int param)
    {
        Send(new byte[] { X11.R_PERMISSION, reportId, (byte)len, 0, (byte)param, 0, 0, 0 });
        Thread.Sleep(250);
        var p = new byte[featLen]; p[0] = X11.R_PERMISSION;
        if (!Native.HidD_GetFeature(cmd, p, p.Length)) throw new Exception("Não deu para ler do mouse (erro " + Marshal.GetLastWin32Error() + ").");
        if (p[1] != 1) throw new Exception("O mouse não liberou a leitura agora. Mexa nele para acordar e tente de novo.");
        var r = new byte[featLen]; r[0] = reportId;
        if (!Native.HidD_GetFeature(cmd, r, r.Length)) throw new Exception("Não deu para ler do mouse (erro " + Marshal.GetLastWin32Error() + ").");
        return r;
    }
    public void Dispose() { closed = true; if (cmd != null) cmd.Dispose(); if (events != null) events.Dispose(); }
}
