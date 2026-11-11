// H510 Studio (teste) — app para o headset Redragon H510-PRO sem fio.
//
// Compilar:
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:H510Studio.exe
//     /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:WindowsBase.dll /r:PresentationCore.dll
//     /r:PresentationFramework.dll /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
//     H510App.cs H510Core.cs UiKit.cs HidCore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

class H510Window : Window
{
    AudioEndpoint spk, mic; readonly MicMonitor monitor = new MicMonitor(); HeadsetLab lab;
    readonly Grid pages = new Grid(); readonly Dictionary<string, FrameworkElement> pageMap = new Dictionary<string, FrameworkElement>();
    readonly StackPanel toasts = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 20, 20) };
    TextBlock title, stTitle, stSub, bannerTxt; Ellipse dot; Border banner, navInd; readonly List<Button> navBtns = new List<Button>(); string page = "som"; bool showing;

    // ---------- helpers (mesmo visual do K86 Studio e do X11 Studio)
    static Style S(string k) { return (Style)Application.Current.Resources[k]; }
    static TextBlock Txt(string t, double size = 14, Brush fg = null, FontWeight? w = null) { return new TextBlock { Text = t, FontSize = size, Foreground = fg ?? T.Text, FontWeight = w ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap }; }
    static FrameworkElement Card(UIElement child, string heading = null)
    {
        var box = new Border { Background = T.Panel, BorderBrush = T.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = child };
        if (heading == null) return box;
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = heading.ToUpper(), FontSize = 11, Foreground = T.Muted, Margin = new Thickness(14, 0, 0, 7) });
        sp.Children.Add(box); return sp;
    }
    static Button Btn(string text, string style = "Btn") { return new Button { Content = text, Style = S(style) }; }
    static Slider Range(int min, int max) { return new Slider { Style = S("Range"), Minimum = min, Maximum = max }; }
    static StackPanel Stack(double gap, params UIElement[] items)
    {
        var sp = new StackPanel();
        for (int i = 0; i < items.Length; i++) { if (items[i] == null) continue; var fe = items[i] as FrameworkElement; if (i > 0 && fe != null) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + gap, fe.Margin.Right, fe.Margin.Bottom); sp.Children.Add(items[i]); }
        return sp;
    }
    static StackPanel Row(double gap, params UIElement[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < items.Length; i++) { var fe = items[i] as FrameworkElement; if (fe != null) { if (i > 0) fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom); fe.VerticalAlignment = VerticalAlignment.Center; } sp.Children.Add(items[i]); }
        return sp;
    }
    static DockPanel Field(string label, TextBlock value, UIElement control)
    {
        var dp = new DockPanel(); var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(head, Dock.Top);
        head.Children.Add(Txt(label, 13, T.Muted)); if (value != null) { value.HorizontalAlignment = HorizontalAlignment.Right; value.FontSize = 13; head.Children.Add(value); }
        dp.Children.Add(head); dp.Children.Add(control); return dp;
    }
    static Grid ToggleRow(string t, string sub, UIElement right)
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 10) }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; sp.Children.Add(Txt(t, 14, T.Text, FontWeights.Medium)); if (sub != null) sp.Children.Add(Txt(sub, 13, T.Muted));
        g.Children.Add(sp); Grid.SetColumn(right, 1); g.Children.Add(right); return g;
    }
    static Border Sep() { return new Border { Height = 1, Background = T.Line }; }
    static Grid TwoCols(UIElement left, double leftWidth, UIElement right)
    {
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(left); Grid.SetColumn(right, 2); g.Children.Add(right); return g;
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    public H510Window()
    {
        Title = "H510 Studio"; Width = 1120; Height = 780; MinWidth = 940; MinHeight = 640; Background = T.Bg; Foreground = T.Text; FontFamily = T.Font; FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SourceInitialized += (s, e) => { try { int on = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, 4); } catch { } };
        var root = new Grid { Background = T.Bg }; Content = root;
        var shell = new Grid(); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) }); shell.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(shell); root.Children.Add(toasts);
        shell.Children.Add(BuildSidebar());
        var main = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(34, 26, 34, 50) }; Grid.SetColumn(main, 1); shell.Children.Add(main);
        var mainStack = new StackPanel { MaxWidth = 1000 }; main.Content = mainStack;
        banner = new Border { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(12), BorderBrush = T.B("#4DFBBF24"), BorderThickness = new Thickness(1), Background = T.B("#14FBBF24"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 18) };
        var bg = new DockPanel(); var retry = Btn("Tentar de novo"); retry.Click += (s, e) => Connect(); DockPanel.SetDock(retry, Dock.Right); bg.Children.Add(retry);
        bannerTxt = Txt("O H510 não foi encontrado. Ligue o receptor sem fio e o headset.", 13, T.B("#FDE68A")); bannerTxt.VerticalAlignment = VerticalAlignment.Center; bg.Children.Add(bannerTxt); banner.Child = bg;
        mainStack.Children.Add(banner);
        title = new TextBlock { FontFamily = T.Display, FontSize = 34, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 22) };
        mainStack.Children.Add(title); mainStack.Children.Add(pages);
        AddPage("som", BuildSound()); AddPage("mic", BuildMic()); AddPage("lab", BuildLab());
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; poll.Tick += (s, e) => RefreshValues(); poll.Start();
        var meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) }; meterTimer.Tick += (s, e) => UpdateMeter(); meterTimer.Start();
        Loaded += (s, e) => { Go("som"); Connect(); };
        Closed += (s, e) => { monitor.Stop(); if (lab != null) lab.Dispose(); };
    }
    void AddPage(string k, FrameworkElement el) { el.Visibility = Visibility.Collapsed; pages.Children.Add(el); pageMap[k] = el; }

    FrameworkElement BuildSidebar()
    {
        var side = new Border { BorderBrush = T.Line, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(14, 20, 14, 20), Background = new LinearGradientBrush(T.C("#10151C"), T.C("#141B24"), 90) };
        var dp = new DockPanel(); side.Child = dp;
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 24) }; DockPanel.SetDock(brand, Dock.Top);
        brand.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center, Background = new LinearGradientBrush(T.C("#47BFFF"), T.C("#1A44C2"), 45), Child = new TextBlock { Text = "H", Foreground = Brushes.White, FontFamily = T.Display, FontWeight = FontWeights.Bold, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        var bt = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bt.Children.Add(new TextBlock { Text = "H510 Studio", FontFamily = T.Display, FontWeight = FontWeights.SemiBold, FontSize = 14 }); bt.Children.Add(new TextBlock { Text = "Redragon · teste", FontSize = 11, Foreground = T.Muted });
        brand.Children.Add(bt); dp.Children.Add(brand);
        var nav = new Grid(); DockPanel.SetDock(nav, Dock.Top);
        navInd = new Border { Height = 38, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(8), Background = T.B("#2C3949"), RenderTransform = new TranslateTransform() };
        nav.Children.Add(navInd); var ns = new StackPanel(); nav.Children.Add(ns);
        var items = new[] {
            new[] { "som", "Som", "M4 14v-2a8 8 0 0 1 16 0v2M4 14h3v6H5a1 1 0 0 1-1-1zM20 14h-3v6h2a1 1 0 0 0 1-1z" },
            new[] { "mic", "Microfone", "M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3zM5 11a7 7 0 0 0 14 0M12 18v3" },
            new[] { "lab", "Laboratório", "M9 3h6M10 3v6l-5 9a2 2 0 0 0 2 3h10a2 2 0 0 0 2-3l-5-9V3" } };
        foreach (var it in items)
        {
            var path = new Path { Data = Geometry.Parse(it[2]), Stroke = T.Muted, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 18, Height = 18, Stretch = Stretch.Uniform };
            var b = new Button { Style = S("Plain"), Height = 38, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 0, 0, 0), Foreground = T.Muted, Tag = it[0],
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { path, new TextBlock { Text = it[1], Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } } } };
            string key = it[0]; b.Click += (s, e) => Go(key); ns.Children.Add(b); navBtns.Add(b);
        }
        dp.Children.Add(nav);
        var status = new Border { BorderBrush = T.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(6, 14, 6, 0), VerticalAlignment = VerticalAlignment.Bottom }; DockPanel.SetDock(status, Dock.Bottom);
        dot = new Ellipse { Width = 8, Height = 8, Fill = T.Faint, Margin = new Thickness(0, 0, 9, 0) };
        stTitle = new TextBlock { Text = "Procurando…", FontWeight = FontWeights.Medium }; stSub = Txt("Ligue o receptor sem fio", 13, T.Muted); stSub.Margin = new Thickness(0, 4, 0, 0);
        status.Child = Stack(0, Row(0, dot, stTitle), stSub);
        dp.Children.Add(status); dp.Children.Add(new Border());
        return side;
    }
    void Go(string key)
    {
        page = key;
        title.Text = key == "som" ? "Som" : key == "mic" ? "Microfone" : "Laboratório";
        for (int i = 0; i < navBtns.Count; i++)
        {
            var b = navBtns[i]; bool on = (string)b.Tag == key; b.Foreground = on ? T.Text : T.Muted; ((Path)((StackPanel)b.Content).Children[0]).Stroke = on ? T.Accent : T.Muted;
            if (on) T.Anim((TranslateTransform)navInd.RenderTransform, TranslateTransform.YProperty, i * 38, 160, T.InOut);
        }
        foreach (var kv in pageMap) kv.Value.Visibility = kv.Key == key ? Visibility.Visible : Visibility.Collapsed;
        UpdateCapture();
    }

    // ---------- conexão
    void Connect()
    {
        try { spk = Audio.FindHeadset(Audio.Render); mic = Audio.FindHeadset(Audio.CaptureFlow); } catch { spk = mic = null; }
        bool ok = spk != null || mic != null;
        dot.Fill = ok ? T.B("#57CBDE") : T.Faint; stTitle.Text = ok ? "Conectado" : "Desconectado"; stTitle.Foreground = ok ? T.B("#57CBDE") : T.Muted;
        stSub.Text = ok ? "Pelo receptor 2.4G" : "Ligue o receptor sem fio";
        banner.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        if (lab == null) { try { lab = HeadsetLab.Open(); if (lab != null) { lab.Received += (src, data) => Dispatcher.BeginInvoke(new Action(() => Log("← " + src, data))); ShowLabInfo(); } } catch (Exception ex) { Log("não deu para abrir a porta interna: " + ex.Message, null); } }
        RefreshValues(); UpdateCapture();
    }
    void RefreshValues()
    {
        if (spk == null && mic == null) return;
        showing = true;
        try
        {
            if (spk != null && !spkVol.IsMouseCaptureWithin) { spkVol.Value = Math.Round(Audio.GetVolume(spk.Id) * 100); spkMute.IsChecked = Audio.GetMute(spk.Id); }
            if (mic != null && !micVol.IsMouseCaptureWithin) { micVol.Value = Math.Round(Audio.GetVolume(mic.Id) * 100); micMute.IsChecked = Audio.GetMute(mic.Id); }
            spkDefault.Text = spk != null && spk.Id == Audio.DefaultId(Audio.Render, 1) ? "É a saída de som padrão do Windows" : "Não é a saída de som padrão";
            micDefault.Text = mic != null && mic.Id == Audio.DefaultId(Audio.CaptureFlow, 1) ? "É o microfone padrão do Windows" : "Não é o microfone padrão";
        }
        catch { }
        showing = false;
    }

    // ============================================================ SOM
    Slider spkVol; TextBlock spkOut, spkDefault; ToggleButton spkMute;
    FrameworkElement BuildSound()
    {
        spkVol = Range(0, 100); spkOut = Txt("");
        spkVol.ValueChanged += (s, e) => { spkOut.Text = (int)spkVol.Value + "%"; if (!showing && spk != null) try { Audio.SetVolume(spk.Id, (float)(spkVol.Value / 100)); } catch (Exception ex) { Toast(ex.Message, true); } };
        spkMute = new SwitchButton(); spkMute.Click += (s, e) => { if (spk != null) try { Audio.SetMute(spk.Id, spkMute.IsChecked == true); } catch (Exception ex) { Toast(ex.Message, true); } };
        spkDefault = Txt("", 13, T.Muted);
        var makeDefault = Btn("Usar como padrão", "Primary");
        makeDefault.Click += (s, e) => { try { if (spk != null) Audio.SetDefault(spk.Id); if (mic != null) Audio.SetDefault(mic.Id); RefreshValues(); T.Confirm(makeDefault, "Pronto"); } catch (Exception ex) { Toast("Não deu para trocar o padrão: " + ex.Message, true); } };
        var fone = Card(Stack(0, Field("Volume do fone", spkOut, spkVol), Sep(), ToggleRow("Mudo", null, spkMute)), "Fone");
        var def = Card(Stack(12, spkDefault, makeDefault), "Padrão do Windows");
        makeDefault.HorizontalAlignment = HorizontalAlignment.Left;
        return TwoCols(Card(Headset()), 300, Stack(16, fone, def,
            Card(Txt("Este app é um teste. O headset não tem software oficial, então tudo aqui usa só os controles de áudio do Windows. Bateria, luz e equalizador internos não estão disponíveis.", 13, T.Muted))));
    }
    static FrameworkElement Headset()
    {
        var cv = new Canvas { Width = 220, Height = 230 };
        var band = new Path { Data = Geometry.Parse("M40,130 C40,40 180,40 180,130"), Stroke = T.B("#3B4D63"), StrokeThickness = 14, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var bandInner = new Path { Data = Geometry.Parse("M48,128 C48,52 172,52 172,128"), Stroke = T.B("#222E3D"), StrokeThickness = 4 };
        cv.Children.Add(band); cv.Children.Add(bandInner);
        foreach (var x in new[] { 18.0, 152.0 })
        {
            var cup = new Border { Width = 50, Height = 82, CornerRadius = new CornerRadius(22), Background = new LinearGradientBrush(T.C("#2B394B"), T.C("#1C2632"), 90), BorderBrush = T.Line2, BorderThickness = new Thickness(1) };
            Canvas.SetLeft(cup, x); Canvas.SetTop(cup, 112); cv.Children.Add(cup);
            var ring = new Border { Width = 34, Height = 62, CornerRadius = new CornerRadius(16), BorderBrush = T.B("#331A9FFF"), BorderThickness = new Thickness(2) };
            Canvas.SetLeft(ring, x + 8); Canvas.SetTop(ring, 122); cv.Children.Add(ring);
        }
        var micArm = new Path { Data = Geometry.Parse("M30,175 C30,205 60,215 85,212"), Stroke = T.B("#3B4D63"), StrokeThickness = 4, StrokeEndLineCap = PenLineCap.Round };
        cv.Children.Add(micArm);
        var label = new TextBlock { Text = "H510-PRO", FontFamily = T.Display, FontWeight = FontWeights.SemiBold, Foreground = T.Muted, FontSize = 13 };
        return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = { new Viewbox { Width = 220, Child = cv }, new TextBlock { Text = "H510-PRO", FontWeight = FontWeights.SemiBold, Foreground = T.Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) } } };
    }

    // ============================================================ MICROFONE
    Slider micVol, loopGain; TextBlock micOut, micDefault, loopOut; ToggleButton micMute, loopSw; Border meterFill;
    FrameworkElement BuildMic()
    {
        micVol = Range(0, 100); micOut = Txt("");
        micVol.ValueChanged += (s, e) => { micOut.Text = (int)micVol.Value + "%"; if (!showing && mic != null) try { Audio.SetVolume(mic.Id, (float)(micVol.Value / 100)); } catch (Exception ex) { Toast(ex.Message, true); } };
        micMute = new SwitchButton(); micMute.Click += (s, e) => { if (mic != null) try { Audio.SetMute(mic.Id, micMute.IsChecked == true); } catch (Exception ex) { Toast(ex.Message, true); } };
        meterFill = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = T.Accent, RenderTransformOrigin = new Point(0, .5), RenderTransform = new ScaleTransform(0, 1) };
        var meter = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = T.Panel2, Child = meterFill };
        micDefault = Txt("", 13, T.Muted);
        var level = Card(Stack(10, Txt("Fale no microfone: a barra mostra o nível ao vivo.", 13, T.Muted), meter), "Nível");
        var vol = Card(Stack(0, Field("Volume do microfone", micOut, micVol), Sep(), ToggleRow("Mudo", null, micMute)), "Microfone");
        loopSw = new SwitchButton(); loopSw.Click += (s, e) => { monitor.Loopback = loopSw.IsChecked == true; UpdateCapture(); };
        loopGain = Range(5, 100); loopGain.Value = 60; loopOut = Txt("60%");
        loopGain.ValueChanged += (s, e) => { loopOut.Text = (int)loopGain.Value + "%"; monitor.LoopGain = (float)(loopGain.Value / 100); };
        var loop = Card(Stack(0, ToggleRow("Ouvir minha voz no fone", "Retorno do microfone, enquanto o app estiver aberto", loopSw), Sep(), Stack(0, new Border { Height = 10 }, Field("Volume do retorno", loopOut, loopGain))), "Retorno");
        return Stack(16, level, TwoCols(vol, 470, Stack(16, loop, Card(micDefault, "Padrão do Windows"))));
    }
    void UpdateCapture()
    {
        bool want = mic != null && (page == "mic" || monitor.Loopback);
        if (want && !monitor.Running) monitor.Start(mic.Id, spk != null ? spk.Id : null);
        else if (!want && monitor.Running) monitor.Stop();
    }
    void UpdateMeter()
    {
        if (page != "mic") return;
        var st = (ScaleTransform)meterFill.RenderTransform;
        double v = Math.Min(1, Math.Sqrt(monitor.Peak) * 1.1);
        st.ScaleX = v; meterFill.Background = v > .9 ? T.Danger : T.Accent;
        if (!string.IsNullOrEmpty(monitor.Error)) { Toast(monitor.Error, true); monitor.Error = ""; }
    }

    // ============================================================ LABORATÓRIO
    TextBox logBox, readId, sendHex; ToggleButton riskSw; Button sendBtn; TextBlock labInfo;
    FrameworkElement BuildLab()
    {
        var warn = new Border { CornerRadius = new CornerRadius(12), BorderBrush = T.B("#66FF5A5F"), BorderThickness = new Thickness(1), Background = T.B("#14FF5A5F"), Padding = new Thickness(16),
            Child = Stack(4, Txt("Área de testes", 14, T.B("#FFB4B6"), FontWeights.SemiBold),
                Txt("Esta porta do headset não tem documentação e parece ser a de atualização de firmware. Escutar e ler é seguro. Enviar comandos pode deixar o headset travado até ser desligado ou, no pior caso, sem funcionar.", 13, T.B("#F3C9CB"))) };
        labInfo = Txt("Procurando a porta interna…", 13, T.Muted);
        logBox = new TextBox { Style = S("Input"), Height = 240, IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap };
        var clear = Btn("Limpar", "Ghost"); clear.HorizontalAlignment = HorizontalAlignment.Left; clear.Click += (s, e) => logBox.Clear();
        var listen = Card(Stack(10, Txt("Tudo que o headset mandar aparece aqui. Aperte os botões dele (volume, mudo) para ver.", 13, T.Muted), logBox, clear), "Mensagens recebidas");
        readId = new TextBox { Style = S("Input"), Width = 70, Text = "0" };
        var read = Btn("Ler relatório"); read.Click += (s, e) => DoRead();
        var reader = Card(Stack(10, Txt("Só leitura: pede um relatório de entrada ao headset pelo número (report ID).", 13, T.Muted), Row(8, Txt("ID", 13, T.Muted), readId, read)), "Ler");
        sendHex = new TextBox { Style = S("Input"), Text = "00 00 00 00", FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        riskSw = new SwitchButton(); riskSw.Click += (s, e) => sendBtn.IsEnabled = riskSw.IsChecked == true;
        sendBtn = Btn("Enviar comando", "Danger"); sendBtn.IsEnabled = false; sendBtn.HorizontalAlignment = HorizontalAlignment.Left; sendBtn.Click += (s, e) => DoSend();
        var sender = Card(Stack(10, Txt("Bytes em hexadecimal. O primeiro é o report ID; o resto é completado com zeros.", 13, T.Muted), sendHex,
            ToggleRow("Entendo o risco", "Libera o botão de envio", riskSw), sendBtn), "Enviar (experimental)");
        return Stack(16, warn, Card(labInfo, "Porta interna"), listen, TwoCols(reader, 380, sender));
    }
    void ShowLabInfo()
    {
        if (lab == null) { labInfo.Text = "Porta interna não encontrada."; return; }
        labInfo.Text = string.Format("VID 0x{0:X4} · PID 0x{1:X4} · coleção 0x{2:X4} · entrada {3} bytes · saída {4} bytes", HeadsetLab.VID, HeadsetLab.PID, lab.Vendor.UsagePage, lab.InLen, lab.OutLen);
    }
    void Log(string what, byte[] data)
    {
        var sb = new StringBuilder(DateTime.Now.ToString("HH:mm:ss.fff")).Append("  ").Append(what);
        if (data != null) { sb.Append("  "); foreach (var b in data) sb.Append(b.ToString("x2")).Append(' '); }
        logBox.AppendText(sb.ToString().TrimEnd() + "\r\n"); logBox.ScrollToEnd();
    }
    void DoRead()
    {
        if (lab == null) { Toast("A porta interna não está aberta.", true); return; }
        byte id; if (!byte.TryParse(readId.Text.Trim(), System.Globalization.NumberStyles.Integer, null, out id) && !byte.TryParse(readId.Text.Trim().Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out id)) { Toast("ID inválido.", true); return; }
        try { Log("lido (ID " + id + ")", lab.GetInput(id)); } catch (Exception ex) { Log("leitura falhou: " + ex.Message, null); }
    }
    void DoSend()
    {
        if (lab == null) { Toast("A porta interna não está aberta.", true); return; }
        byte[] data;
        try { data = sendHex.Text.Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToByte(x.Replace("0x", ""), 16)).ToArray(); }
        catch { Toast("Escreva os bytes em hexadecimal, separados por espaço.", true); return; }
        if (data.Length == 0) return;
        if (MessageBox.Show(this, "Enviar " + data.Length + " byte(s) para a porta interna do headset?\n\n" + string.Join(" ", data.Select(b => b.ToString("x2")).ToArray()) + "\n\nIsto pode deixar o headset travado.", "H510 Studio", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { lab.SendOutput(data); Log("→ enviado", data); } catch (Exception ex) { Log("envio falhou: " + ex.Message, null); }
    }

    // ---------- avisos
    void Toast(string msg, bool err)
    {
        var tb = Txt(msg, 13); tb.MaxWidth = 340;
        var b = new Border { CornerRadius = new CornerRadius(12), Background = T.B("#22303F"), BorderBrush = T.Line2, BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 8, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Opacity = .5, Direction = 270 }, RenderTransform = new TranslateTransform(0, 40), Opacity = 0,
            Child = Row(10, new Ellipse { Width = 8, Height = 8, Fill = err ? T.Danger : T.B("#57CBDE") }, tb) };
        toasts.Children.Add(b);
        T.Anim((TranslateTransform)b.RenderTransform, TranslateTransform.YProperty, 0, 300, T.EaseOut); T.Anim(b, OpacityProperty, 1, 300, T.EaseOut);
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(err ? 6000 : 3000) };
        b.MouseEnter += (s, e) => t.Stop(); b.MouseLeave += (s, e) => t.Start();
        t.Tick += (s, e) => { t.Stop(); T.Anim((TranslateTransform)b.RenderTransform, TranslateTransform.YProperty, 40, 200, T.EaseOut); T.Anim(b, OpacityProperty, 0, 200, T.EaseOut);
            var rm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) }; rm.Tick += (s2, e2) => { rm.Stop(); toasts.Children.Remove(b); }; rm.Start(); };
        t.Start();
    }
}

static class H510Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool fresh; var mutex = new Mutex(true, "H510Studio-single", out fresh);
        if (!fresh) { MessageBox.Show("O H510 Studio já está aberto.", "H510 Studio"); return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(T.Styles());
        var log = new StringBuilder();
        app.DispatcherUnhandledException += (s, e) => { log.AppendLine(e.Exception.ToString()); if (args.Length == 0) MessageBox.Show("Erro inesperado: " + e.Exception.Message, "H510 Studio"); e.Handled = true; };
        var w = new H510Window();
        if (args.Length == 2 && args[0] == "--shot")
        {
            w.Loaded += async (s, e) =>
            {
                await Task.Delay(2500);
                foreach (var pg in new[] { "som", "mic", "lab" })
                {
                    try { typeof(H510Window).GetMethod("Go", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(w, new object[] { pg }); } catch (Exception ex) { log.AppendLine(pg + ": " + (ex.InnerException ?? ex)); }
                    await Task.Delay(1500);
                    var rt = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); rt.Render((Visual)w.Content);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rt));
                    using (var fs = File.Create(System.IO.Path.Combine(args[1], "h510_" + pg + ".png"))) enc.Save(fs);
                }
                File.WriteAllText(System.IO.Path.Combine(args[1], "h510_log.txt"), log.ToString());
                w.Close();
            };
        }
        app.Run(w);
        GC.KeepAlive(mutex);
    }
}
