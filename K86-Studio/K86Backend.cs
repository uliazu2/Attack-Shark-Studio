// K86 Studio — operações de alto nível no teclado. Todas são síncronas e protegidas por um lock;
// a interface chama elas fora da thread da janela (Task.Run).
using System;
using System.Collections.Generic;
using System.Threading;

class EffectState { public int Mode, Speed, Bright, Option, Color; public bool Rainbow; }
class SettingsState { public int Sleep, Deep, Debounce; public bool WinLock, Wasd; }
class StatusState { public bool Connected, Dongle; public int Profile; }

class Backend
{
    public readonly object Lock = new object();
    K86Device dev;
    public LightSync Sync;
    public bool DryRun;
    public Backend() { Sync = new LightSync(this); }

    K86Device Need()
    {
        if (dev == null) dev = K86Device.Find();
        if (dev == null) throw new Exception("K86 não encontrado. Ligue o teclado no cabo USB e feche o app oficial.");
        return dev;
    }
    void Drop() { if (dev != null) { try { dev.Dispose(); } catch { } dev = null; } }
    T Do<T>(Func<K86Device, T> f) { lock (Lock) { try { return f(Need()); } catch { Drop(); throw; } } }
    void Do(Action<K86Device> f) { Do<int>(d => { f(d); return 0; }); }
    public void TrySend(byte[] p) { if (DryRun) return; lock (Lock) { try { Need().Send(p, 0); } catch { Drop(); } } }

    public StatusState Status()
    {
        lock (Lock)
        {
            try { var d = Need(); var pr = d.Query(P.B7(0x85)); return new StatusState { Connected = true, Profile = pr[0] == 0x85 ? pr[1] : 0 }; }
            catch { Drop(); return new StatusState { Connected = false, Dongle = Hid.List().Exists(h => h.Vid == 0x3151 && h.Pid == 0x4011) }; }
        }
    }
    public EffectState GetEffect()
    {
        var s = Do(d => d.Query(P.B7(0x87)));
        if (Sync.Mode == "off") Sync.Mode = s[1] == 20 || s[1] == 22 ? "music" : s[1] == 21 ? "screen" : "off";
        return new EffectState { Mode = s[1], Speed = Math.Max(0, Math.Min(4, 4 - s[2])), Bright = Math.Min(4, (int)s[3]), Option = s[4] >> 4,
            Rainbow = (s[4] & 15) == 8 || ((s[1] == 20 || s[1] == 22) && (s[4] & 15) == 0), Color = (s[5] << 16) | (s[6] << 8) | s[7] };
    }
    public void SetEffect(EffectState e)
    {
        Do(d => d.Send(P.Effect(e.Mode, e.Bright, e.Speed, e.Option, e.Rainbow, (e.Color >> 16) & 255, (e.Color >> 8) & 255, e.Color & 255), 30));
        Sync.Mode = e.Mode == 20 || e.Mode == 22 ? "music" : e.Mode == 21 ? "screen" : "off";
    }
    public void SetPerKey(int[] colors, bool enterMode, int bright)
    {
        Do(d =>
        {
            if (enterMode) { d.Send(P.Effect(13, Math.Max(1, bright), 0, 0, false, 0, 0, 0), 20); Sync.Mode = "off"; }
            foreach (var p in P.PerKey(colors)) d.Send(p, 6);
        });
    }
    public byte[] GetMatrix(int profile)
    {
        return Do(d => { var m = new byte[512]; for (int pg = 0; pg < 8; pg++) Array.Copy(d.Query(P.B7(0x89, profile, pg)), 0, m, pg * 64, 64); return m; });
    }
    public void SetKey(int profile, int idx, byte[] code) { Do(d => d.Send(P.SetKey(profile, idx, code), 120)); }
    public void ResetKeys(int profile)
    {
        Do(d =>
        {
            var cur = new byte[512];
            for (int pg = 0; pg < 8; pg++) Array.Copy(d.Query(P.B7(0x89, profile, pg)), 0, cur, pg * 64, 64);
            for (int i = 0; i < 128; i++)
            {
                bool same = true; for (int k = 0; k < 4; k++) if (cur[i * 4 + k] != P.DefaultMatrix[i * 4 + k]) same = false;
                if (!same) { var def = new byte[4]; Array.Copy(P.DefaultMatrix, i * 4, def, 0, 4); d.Send(P.SetKey(profile, i, def), 60); }
            }
        });
    }
    public void SetMacro(int idx, int repeat, List<P.MacroEv> evs)
    {
        var pk = P.MacroPackets(idx, Math.Max(1, repeat), evs);
        Do(d => { foreach (var p in pk) d.Send(p, 30); Thread.Sleep(150); });
    }
    public void SetProfile(int p) { Do(d => d.Send(P.B7(0x05, p), 300)); }
    public void Clock() { Do(d => d.Send(P.Clock(DateTime.Now), 20)); }
    public void EraseScreen() { Do(d => d.Send(P.B7(0x2C), 20)); }
    public void Reset() { Sync.Mode = "off"; Do(d => d.Send(P.B7(0x02), 4000)); lock (Lock) Drop(); }

    public SettingsState GetSettings()
    {
        return Do(d =>
        {
            var s = d.Query(P.B7(0x92)); var db = d.Query(P.B7(0x91)); var pr = d.Query(P.B7(0x85)); var k = d.Query(P.B7(0x86, pr[1]));
            return new SettingsState { Sleep = s[1] | (s[2] << 8), Deep = s[5] | (s[6] << 8), Debounce = db[2], WinLock = (k[2] & 1) != 0, Wasd = (k[2] & 8) != 0 };
        });
    }
    public void SetSettings(SettingsState st)
    {
        Do(d =>
        {
            d.Send(P.Sleep(st.Sleep, st.Sleep, st.Deep, st.Deep), 120);
            d.Send(P.B7(0x11, 0, Math.Max(1, Math.Min(10, st.Debounce))), 120);
            var pr = d.Query(P.B7(0x85))[1]; var k = d.Query(P.B7(0x86, pr));
            if (k[0] != 0x86) throw new Exception("Não deu para ler as opções atuais.");
            int b2 = k[2]; b2 = st.WinLock ? b2 | 1 : b2 & ~1; b2 = st.Wasd ? b2 | 8 : b2 & ~8;
            d.Send(P.B7(0x06, pr, b2, k[3], k[4]), 120);
        });
    }

    // envia imagem/GIF (quadros já em RGB565, 64800 bytes cada); progress recebe 0..100
    public void SendScreen(List<byte[]> frames, int slot, int delay, Action<int> progress)
    {
        int n = frames.Count, len = frames[0].Length, dl = n > 1 ? Math.Max(1, Math.Min(255, delay)) : 0;
        bool ok = false;
        for (int t = 0; t < 10 && !ok; t++)
        {
            ok = Do(d => d.Query(P.ScreenPrepare(n == 1 ? slot : 0, n, dl, len, slot))[1] == 1);
            if (!ok) Thread.Sleep(150);
        }
        if (!ok) throw new Exception("O teclado não aceitou a imagem agora. Tente de novo em alguns segundos.");
        Thread.Sleep(100);
        int pages = (len + 55) / 56, total = pages * n, done = 0;
        for (int f = 0; f < n; f++)
            for (int pg = 0; pg < pages; pg += 40)
            {
                int ff = f, start = pg;
                Do(d => { for (int k = start; k < Math.Min(pages, start + 40); k++) { d.Send(P.ScreenChunk(n == 1 ? slot : ff, n, dl, k, frames[ff]), 1); done++; } });
                progress(Math.Min(99, done * 100 / total));
            }
        progress(100);
    }
}
