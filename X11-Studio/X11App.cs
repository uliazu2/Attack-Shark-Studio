// X11 Studio — app nativo (WPF) para o mouse Attack Shark X11.
//
// Compilar:
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:X11Studio.exe
//     /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:WindowsBase.dll /r:PresentationCore.dll
//     /r:PresentationFramework.dll /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
//     X11App.cs UiKit.cs HidCore.cs X11Core.cs
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
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

// ======================================================================= operações no mouse
class MouseState { public int Profile = 1, PollHz = 1000; public DpiSettings Dpi; public LightSettings Light; public ButtonSlot[] Buttons; }

class X11Backend
{
    readonly object lk = new object(); X11Device dev;
    public volatile int Battery = -1, BatteryStatus;   // 1 normal, 2 carregado, 3 carregando
    public event Action BatteryChanged = delegate { };
    readonly AutoResetEvent ack = new AutoResetEvent(false); volatile int ackReport = -1, ackStatus = -1;

    X11Device Need()
    {
        if (dev == null)
        {
            dev = X11Device.Open();
            if (dev == null) throw new Exception("X11 não encontrado. Ligue o receptor (ou o cabo) e mexa o mouse para acordar.");
            dev.OnMessage = OnMessage;
        }
        return dev;
    }
    void Drop() { if (dev != null) { try { dev.Dispose(); } catch { } dev = null; } }
    void OnMessage(byte[] m)
    {
        if (m.Length < 5) return;
        int type = m[2], p1 = m[3], p2 = m[4];
        if (type == 0x40 || type == 0x41) { if (p1 >= 1 && p1 <= 3 && p2 <= 100) { BatteryStatus = p1; Battery = p2; BatteryChanged(); } }
        else if (type == 0x50) { ackStatus = p1; ackReport = p2; ack.Set(); }
    }
    T Do<T>(Func<X11Device, T> f) { lock (lk) { try { return f(Need()); } catch { Drop(); throw; } } }

    public bool Connected() { lock (lk) { try { Need(); return true; } catch { Drop(); return false; } } }
    public bool Wireless { get { return dev != null && dev.Pid == X11.PID_WIRELESS; } }

    public MouseState Load()
    {
        return Do(d =>
        {
            var st = new MouseState();
            var p = d.Read(X11.R_PROFILE, X11.L_PROFILE, 0); st.Profile = p[2] >= 1 && p[2] <= 5 ? p[2] : 1;
            st.Dpi = DpiSettings.Parse(d.Read(X11.R_DPI, X11.L_DPI, st.Profile));
            st.Light = LightSettings.Parse(d.Read(X11.R_LIGHT, X11.L_LIGHT, st.Profile));
            st.PollHz = Poll.Parse(d.Read(X11.R_POLL, X11.L_POLL, st.Profile));
            st.Buttons = Buttons.Parse(d.Read(X11.R_BUTTONS, X11.L_BUTTONS, st.Profile));
            return st;
        });
    }
    // envia e espera a confirmação do mouse (mensagem 0x50)
    public void Write(byte[] packet)
    {
        Do<int>(d =>
        {
            ack.Reset(); ackReport = -1;
            d.Send(packet);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1200)
            {
                if (ack.WaitOne(100) && ackReport == packet[0]) { if (ackStatus != 0) throw new Exception("O mouse recusou a configuração."); return 0; }
            }
            return 0; // sem confirmação (cabo/firmwares antigos): segue como enviado
        });
        Thread.Sleep(60);
    }
}

// ======================================================================= desenho do mouse
class MouseView : Viewbox
{
    public readonly Dictionary<int, Shape> Parts = new Dictionary<int, Shape>(); // slot -> forma clicável
    public readonly SolidColorBrush Glow = new SolidColorBrush(Colors.Transparent), Logo = new SolidColorBrush(Colors.Transparent);
    public event Action<int> PartClicked = delegate { };
    readonly bool interactive;
    public MouseView(bool interactive)
    {
        this.interactive = interactive; Stretch = Stretch.Uniform;
        var cv = new Canvas { Width = 260, Height = 430 };
        var glow = new Path { Data = Geometry.Parse("M130,12 C60,12 30,82 30,172 L30,300 C30,372 75,414 130,414 C185,414 230,372 230,300 L230,172 C230,82 200,12 130,12 Z"), Stroke = Glow, StrokeThickness = 14, Effect = new BlurEffect { Radius = 18 } };
        cv.Children.Add(glow);
        var body = new Path { Data = glow.Data, Fill = new LinearGradientBrush(T.C("#1E1E25"), T.C("#121217"), 90), Stroke = T.Line2, StrokeThickness = 1.5, Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 12, Opacity = .55, Direction = 270 } };
        cv.Children.Add(body);
        var logo = new Ellipse { Width = 54, Height = 54, Fill = Logo, Effect = new BlurEffect { Radius = 16 } }; Canvas.SetLeft(logo, 103); Canvas.SetTop(logo, 300); cv.Children.Add(logo);
        var mark = new Path { Data = Geometry.Parse("M118,330 L130,312 L142,330 L136,330 L130,321 L124,330 Z"), Fill = T.B("#3A3A44") }; cv.Children.Add(mark);
        Part(cv, 1, new Path { Data = Geometry.Parse("M126,16 C72,20 42,82 40,172 L126,172 Z") });
        Part(cv, 2, new Path { Data = Geometry.Parse("M134,16 C188,20 218,82 220,172 L134,172 Z") });
        Part(cv, 3, Place(new Rectangle { Width = 18, Height = 52, RadiusX = 9, RadiusY = 9 }, 121, 52));
        Part(cv, 6, Place(new Rectangle { Width = 14, Height = 22, RadiusX = 5, RadiusY = 5 }, 123, 128));
        Part(cv, 7, Place(new Rectangle { Width = 13, Height = 48, RadiusX = 5, RadiusY = 5 }, 19, 190));
        Part(cv, 8, Place(new Rectangle { Width = 13, Height = 48, RadiusX = 5, RadiusY = 5 }, 19, 244));
        Child = cv;
    }
    static Shape Place(Shape s, double x, double y) { Canvas.SetLeft(s, x); Canvas.SetTop(s, y); return s; }
    void Part(Canvas cv, int slot, Shape s)
    {
        s.Fill = new LinearGradientBrush(T.C("#26262E"), T.C("#1A1A20"), 90); s.Stroke = T.B("#2E2E38"); s.StrokeThickness = 1.2;
        if (interactive)
        {
            s.Cursor = Cursors.Hand;
            s.MouseEnter += (a, b) => { if (s.Tag == null) s.Stroke = T.B("#5A5A68"); };
            s.MouseLeave += (a, b) => { if (s.Tag == null) s.Stroke = T.B("#2E2E38"); };
            s.MouseLeftButtonDown += (a, b) => PartClicked(slot);
        }
        Parts[slot] = s; cv.Children.Add(s);
    }
    public void Select(int slot)
    {
        foreach (var kv in Parts) { bool on = kv.Key == slot; kv.Value.Tag = on ? (object)true : null; kv.Value.Stroke = on ? T.Accent : T.B("#2E2E38"); kv.Value.StrokeThickness = on ? 2.5 : 1.2; }
    }
}

// ======================================================================= janela
class X11Window : Window
{
    readonly X11Backend be = new X11Backend();
    MouseState st; bool connected, showing;
    readonly Grid pages = new Grid(); readonly Dictionary<string, FrameworkElement> pageMap = new Dictionary<string, FrameworkElement>();
    readonly StackPanel toasts = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 20, 20) };
    TextBlock title, subtitle, stTitle, stSub, bannerTxt, battTxt; Ellipse dot; Border banner, battBar; Border navInd; readonly List<Button> navBtns = new List<Button>(); string page = "dpi";
    MouseView mouseDpi, mouseLight, mouseBtn;

    static readonly int[] DpiSteps = BuildSteps();
    static int[] BuildSteps() { var l = new List<int>(); for (int v = 50; v <= 10000; v += 50) l.Add(v); for (int v = 10100; v <= 12000; v += 100) l.Add(v); for (int v = 12200; v <= 22000; v += 200) l.Add(v); return l.ToArray(); }
    static int StepIndex(int dpi) { int best = 0; for (int i = 0; i < DpiSteps.Length; i++) if (Math.Abs(DpiSteps[i] - dpi) < Math.Abs(DpiSteps[best] - dpi)) best = i; return best; }

    // ---------- helpers (mesmo visual do K86 Studio)
    static Style S(string k) { return (Style)Application.Current.Resources[k]; }
    static TextBlock Txt(string t, double size = 14, Brush fg = null, FontWeight? w = null) { return new TextBlock { Text = t, FontSize = size, Foreground = fg ?? T.Text, FontWeight = w ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap }; }
    static Border Card(UIElement child, string heading = null)
    {
        var sp = new StackPanel();
        if (heading != null) sp.Children.Add(new TextBlock { Text = heading.ToUpper(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = T.Muted, Margin = new Thickness(0, 0, 0, 12) });
        sp.Children.Add(child);
        return new Border { Background = T.Panel, BorderBrush = T.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Child = sp };
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
        head.Children.Add(Txt(label, 12.5, T.Muted)); if (value != null) { value.HorizontalAlignment = HorizontalAlignment.Right; value.FontSize = 12.5; head.Children.Add(value); }
        dp.Children.Add(head); dp.Children.Add(control); return dp;
    }
    static Grid ToggleRow(string t, string sub, UIElement right)
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 10) }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel(); sp.Children.Add(Txt(t, 14, T.Text, FontWeights.Medium)); if (sub != null) sp.Children.Add(Txt(sub, 12.5, T.Muted));
        g.Children.Add(sp); Grid.SetColumn(right, 1); g.Children.Add(right); return g;
    }
    static Border Sep() { return new Border { Height = 1, Background = T.Line }; }
    static Grid TwoCols(UIElement left, double leftWidth, UIElement right)
    {
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(left); Grid.SetColumn(right, 2); g.Children.Add(right); return g;
    }
    static readonly string[] Palette = { "#FF3355", "#FF8A00", "#FFD400", "#22E06B", "#00D5FF", "#3B6BFF", "#9B5CFF", "#FF4FD8", "#FFFFFF" };
    Action<int> Swatches(WrapPanel panel, Action<int> pick)
    {
        var rings = new List<Ellipse>();
        Action<int> mark = c => { foreach (var r in rings) r.Stroke = (int)r.Tag == (c & 0xFFFFFF) ? Brushes.White : Brushes.Transparent; };
        foreach (var hex in Palette)
        {
            int c = Convert.ToInt32(hex.Substring(1), 16);
            var ring = new Ellipse { Width = 32, Height = 32, StrokeThickness = 2, Tag = c, Stroke = Brushes.Transparent };
            var g = new Grid(); g.Children.Add(ring); g.Children.Add(new Ellipse { Width = 26, Height = 26, Fill = T.B(hex), Stroke = T.Line2, StrokeThickness = 1 });
            var b = new Button { Style = S("Plain"), Content = g, Margin = new Thickness(0, 0, 6, 6) }; b.Click += (s, e) => { mark(c); pick(c); };
            rings.Add(ring); panel.Children.Add(b);
        }
        var cg = new Grid(); cg.Children.Add(new Ellipse { Width = 26, Height = 26, Fill = new LinearGradientBrush(new GradientStopCollection { new GradientStop(T.C("#F43F5E"), 0), new GradientStop(T.C("#F59E0B"), .25), new GradientStop(T.C("#84CC16"), .5), new GradientStop(T.C("#06B6D4"), .75), new GradientStop(T.C("#8B5CF6"), 1) }, 0) });
        cg.Children.Add(new TextBlock { Text = "+", FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var cb = new Button { Style = S("Plain"), Content = cg, Margin = new Thickness(0, 0, 6, 6), Width = 32, Height = 32 };
        cb.Click += (s, e) => { int c; if (PickColor(-1, out c)) { mark(-1); pick(c); } };
        panel.Children.Add(cb); return mark;
    }
    static bool PickColor(int initial, out int color)
    {
        color = 0;
        using (var d = new System.Windows.Forms.ColorDialog { FullOpen = true })
        {
            if (initial >= 0) d.Color = System.Drawing.Color.FromArgb(initial >> 16 & 255, initial >> 8 & 255, initial & 255);
            if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
            color = (d.Color.R << 16) | (d.Color.G << 8) | d.Color.B; return true;
        }
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    public X11Window()
    {
        Title = "X11 Studio"; Width = 1180; Height = 820; MinWidth = 980; MinHeight = 680; Background = T.Bg; Foreground = T.Text; FontFamily = T.Font; FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SourceInitialized += (s, e) => { try { int on = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, 4); } catch { } };
        var root = new Grid { Background = T.Bg }; Content = root;
        var shell = new Grid(); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) }); shell.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(shell); root.Children.Add(toasts);
        shell.Children.Add(BuildSidebar());
        var main = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(34, 26, 34, 50) }; Grid.SetColumn(main, 1); shell.Children.Add(main);
        var mainStack = new StackPanel { MaxWidth = 1060 }; main.Content = mainStack;
        banner = new Border { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(12), BorderBrush = T.B("#4DFBBF24"), BorderThickness = new Thickness(1), Background = T.B("#14FBBF24"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 18) };
        var bg = new DockPanel(); var retry = Btn("Tentar de novo"); retry.Click += (s, e) => Connect(); DockPanel.SetDock(retry, Dock.Right); bg.Children.Add(retry);
        bannerTxt = Txt("O X11 não foi encontrado. Ligue o receptor sem fio (ou o cabo) e mexa o mouse para ele acordar.", 13.5, T.B("#FDE68A")); bannerTxt.VerticalAlignment = VerticalAlignment.Center; bg.Children.Add(bannerTxt); banner.Child = bg;
        mainStack.Children.Add(banner);
        title = new TextBlock { FontFamily = T.Display, FontSize = 27, FontWeight = FontWeights.SemiBold }; subtitle = Txt("", 14, T.Muted); subtitle.Margin = new Thickness(0, 4, 0, 0);
        var header = Stack(0, title, subtitle); header.Margin = new Thickness(0, 0, 0, 22); mainStack.Children.Add(header); mainStack.Children.Add(pages);
        AddPage("dpi", BuildDpi()); AddPage("luz", BuildLight()); AddPage("botoes", BuildButtons()); AddPage("desempenho", BuildPerf());
        be.BatteryChanged += () => Dispatcher.BeginInvoke(new Action(ShowBattery));
        PreviewKeyDown += HandleKey;
        CompositionTarget.Rendering += OnFrame;
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; poll.Tick += (s, e) => { if (!connected) Connect(); }; poll.Start();
        Loaded += (s, e) => { Go("dpi"); Connect(); };
    }
    void AddPage(string k, FrameworkElement el) { el.Visibility = Visibility.Collapsed; pages.Children.Add(el); pageMap[k] = el; }

    FrameworkElement BuildSidebar()
    {
        var side = new Border { BorderBrush = T.Line, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(14, 20, 14, 20), Background = new LinearGradientBrush(T.C("#0D0D10"), T.C("#09090B"), 90) };
        var dp = new DockPanel(); side.Child = dp;
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 24) }; DockPanel.SetDock(brand, Dock.Top);
        var mark = new Grid { Width = 30, Height = 30 };
        mark.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(T.C("#5EEAD4"), 0), new GradientStop(T.C("#8B7CFF"), .5), new GradientStop(T.C("#F472B6"), 1) }, 45), Effect = new DropShadowEffect { Color = T.C("#5EEAD4"), BlurRadius = 18, ShadowDepth = 0, Opacity = .45 } });
        mark.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = T.B("#0D0D10"), Margin = new Thickness(3) });
        mark.Children.Add(new Path { Data = Geometry.Parse("M15,8 C11,8 10,11 10,15 L10,19 C10,22 12,24 15,24 C18,24 20,22 20,19 L20,15 C20,11 19,8 15,8 Z M15,8 L15,14"), Stroke = T.B("#CFF7EF"), StrokeThickness = 1.5 });
        brand.Children.Add(mark);
        var bt = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bt.Children.Add(new TextBlock { Text = "X11 Studio", FontFamily = T.Display, FontWeight = FontWeights.SemiBold, FontSize = 15 }); bt.Children.Add(Txt("Attack Shark X11", 11.5, T.Muted));
        brand.Children.Add(bt); dp.Children.Add(brand);
        var nav = new Grid(); DockPanel.SetDock(nav, Dock.Top);
        navInd = new Border { Height = 38, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(10), Background = T.Panel3, BorderBrush = T.Line, BorderThickness = new Thickness(1), RenderTransform = new TranslateTransform() };
        nav.Children.Add(navInd); var ns = new StackPanel(); nav.Children.Add(ns);
        var items = new[] {
            new[] { "dpi", "DPI", "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7v5l3 2" },
            new[] { "luz", "Iluminação", "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" },
            new[] { "botoes", "Botões", "M12 3C8 3 6 6 6 10v5c0 3.5 2.5 6 6 6s6-2.5 6-6v-5c0-4-2-7-6-7zM12 3v6M6 10h12" },
            new[] { "desempenho", "Desempenho", "M3 17l5-5 4 4 8-8M15 8h5v5" } };
        foreach (var it in items)
        {
            var path = new Path { Data = Geometry.Parse(it[2]), Stroke = T.Muted, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 18, Height = 18, Stretch = Stretch.Uniform };
            var b = new Button { Style = S("Plain"), Height = 38, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 0, 0, 0), Foreground = T.Muted, Tag = it[0],
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { path, new TextBlock { Text = it[1], Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } } } };
            string key = it[0]; b.Click += (s, e) => Go(key); ns.Children.Add(b); navBtns.Add(b);
        }
        dp.Children.Add(nav);
        var status = new Border { CornerRadius = new CornerRadius(12), BorderBrush = T.Line, BorderThickness = new Thickness(1), Background = T.Panel, Padding = new Thickness(12), VerticalAlignment = VerticalAlignment.Bottom }; DockPanel.SetDock(status, Dock.Bottom);
        dot = new Ellipse { Width = 8, Height = 8, Fill = T.Faint, Margin = new Thickness(0, 0, 9, 0) };
        stTitle = new TextBlock { Text = "Procurando…", FontWeight = FontWeights.Medium }; stSub = Txt("Ligue o receptor ou o cabo", 12, T.Muted); stSub.Margin = new Thickness(0, 4, 0, 0);
        battBar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = T.Ok, RenderTransformOrigin = new Point(0, .5), RenderTransform = new ScaleTransform(0, 1) };
        battTxt = Txt("Bateria: aguardando o mouse", 12, T.Muted);
        var battTrack = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = T.B("#2A2A32"), Child = battBar, Margin = new Thickness(0, 10, 0, 6) };
        status.Child = Stack(0, Row(0, dot, stTitle), stSub, battTrack, battTxt);
        dp.Children.Add(status); dp.Children.Add(new Border());
        return side;
    }
    void Go(string key)
    {
        page = key;
        var info = new Dictionary<string, string[]> {
            { "dpi", new[] { "DPI", "Ajuste a sensibilidade de cada estágio. O botão de DPI do mouse alterna entre os estágios ligados." } },
            { "luz", new[] { "Iluminação", "Efeito, cor e brilho da luz do mouse. Muda na hora." } },
            { "botoes", new[] { "Botões", "Clique num botão no desenho e escolha o que ele faz." } },
            { "desempenho", new[] { "Desempenho", "Taxa de resposta, tempo de clique e economia de bateria." } } };
        title.Text = info[key][0]; subtitle.Text = info[key][1];
        for (int i = 0; i < navBtns.Count; i++)
        {
            var b = navBtns[i]; bool on = (string)b.Tag == key; b.Foreground = on ? T.Text : T.Muted; ((Path)((StackPanel)b.Content).Children[0]).Stroke = on ? T.Text : T.Muted;
            if (on) T.Anim((TranslateTransform)navInd.RenderTransform, TranslateTransform.YProperty, i * 38, 160, T.InOut);
        }
        foreach (var kv in pageMap) { bool on = kv.Key == key; kv.Value.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }
    }

    // ---------- conexão
    async void Connect()
    {
        bool ok = await Task.Run(() => be.Connected());
        connected = ok; dot.Fill = ok ? T.Ok : T.Faint;
        stTitle.Text = ok ? "X11 conectado" : "Desconectado"; stSub.Text = ok ? (be.Wireless ? "Pelo receptor 2.4G" : "Pelo cabo USB") : "Ligue o receptor ou o cabo";
        banner.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        if (!ok) return;
        try { st = await Task.Run(() => be.Load()); ShowAll(); }
        catch (Exception ex) { connected = false; Toast(ex.Message, true); }
    }
    void ShowBattery()
    {
        if (be.Battery < 0) return;
        battTxt.Text = "Bateria: " + be.Battery + "%" + (be.BatteryStatus == 3 ? " · carregando" : be.BatteryStatus == 2 ? " · carregada" : "");
        battBar.Background = be.Battery <= 15 ? T.Danger : be.Battery <= 35 ? T.B("#FBBF24") : T.Ok;
        T.Anim((ScaleTransform)battBar.RenderTransform, ScaleTransform.ScaleXProperty, be.Battery / 100.0, 250, T.EaseOut);
    }
    async Task<bool> Save(byte[] packet, string ok)
    {
        if (!connected) { Toast("O X11 não está conectado.", true); return false; }
        try { await Task.Run(() => be.Write(packet)); if (ok != null) Toast(ok, false); return true; }
        catch (Exception ex) { Toast(ex.Message, true); return false; }
    }
    void Toast(string msg, bool err)
    {
        var tb = Txt(msg, 13.5); tb.MaxWidth = 340;
        var b = new Border { CornerRadius = new CornerRadius(12), Background = T.B("#1D1D24"), BorderBrush = T.Line2, BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 8, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Opacity = .5, Direction = 270 }, RenderTransform = new TranslateTransform(0, 40), Opacity = 0,
            Child = Row(10, new Ellipse { Width = 8, Height = 8, Fill = err ? T.Danger : T.Ok }, tb) };
        toasts.Children.Add(b);
        T.Anim((TranslateTransform)b.RenderTransform, TranslateTransform.YProperty, 0, 300, T.EaseOut); T.Anim(b, OpacityProperty, 1, 300, T.EaseOut);
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(err ? 6000 : 3000) };
        b.MouseEnter += (s, e) => t.Stop(); b.MouseLeave += (s, e) => t.Start();
        t.Tick += (s, e) => { t.Stop(); T.Anim((TranslateTransform)b.RenderTransform, TranslateTransform.YProperty, 40, 200, T.EaseOut); T.Anim(b, OpacityProperty, 0, 200, T.EaseOut);
            var rm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) }; rm.Tick += (s2, e2) => { rm.Stop(); toasts.Children.Remove(b); }; rm.Start(); };
        t.Start();
    }
    void ShowAll() { showing = true; ShowDpi(); ShowLight(); ShowButtons(); ShowPerf(); showing = false; }

    // ============================================================ DPI
    class StageRow { public Border Card; public Ellipse Dot, Current; public Slider Slider; public TextBlock Value; public ToggleButton On; }
    readonly List<StageRow> stages = new List<StageRow>(); Seg indicatorSeg, lodSeg; ToggleButton motionSw, snapSw, rippleSw; DispatcherTimer dpiTimer;
    FrameworkElement BuildDpi()
    {
        mouseDpi = new MouseView(false);
        var list = new StackPanel();
        for (int i = 0; i < 8; i++)
        {
            int k = i; var r = new StageRow();
            r.Current = new Ellipse { Width = 10, Height = 10, Stroke = T.Line2, StrokeThickness = 1.5, Fill = Brushes.Transparent };
            r.Dot = new Ellipse { Width = 22, Height = 22, Stroke = T.Line2, StrokeThickness = 1 };
            var dotBtn = new Button { Style = S("Plain"), Content = r.Dot, ToolTip = "Cor deste estágio" };
            dotBtn.Click += (s, e) => { int c; if (st != null && PickColor(st.Dpi.Color[k], out c)) { st.Dpi.Color[k] = c; ShowDpi(); PushDpi(); } };
            r.Slider = Range(0, DpiSteps.Length - 1); r.Slider.Margin = new Thickness(14, 0, 14, 0);
            r.Value = new TextBlock { Width = 74, TextAlignment = TextAlignment.Right, FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            r.Slider.ValueChanged += (s, e) => { r.Value.Text = DpiSteps[(int)r.Slider.Value].ToString("N0", new System.Globalization.CultureInfo("pt-BR")); if (!showing && st != null) { st.Dpi.Dpi[k] = DpiSteps[(int)r.Slider.Value]; PushDpi(); } };
            r.On = new SwitchButton { VerticalAlignment = VerticalAlignment.Center };
            r.On.Click += (s, e) => { if (st == null) return; int mask = st.Dpi.ActiveStages; mask = r.On.IsChecked == true ? mask | (1 << k) : mask & ~(1 << k); if (mask == 0) { r.On.IsChecked = true; return; } st.Dpi.ActiveStages = mask; if ((mask & (1 << (st.Dpi.CurrentStage - 1))) == 0) st.Dpi.CurrentStage = Enumerable.Range(0, 8).First(x => (mask & (1 << x)) != 0) + 1; ShowDpi(); PushDpi(); };
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var num = new TextBlock { Text = (i + 1).ToString(), Foreground = T.Muted, Width = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
            var curBtn = new Button { Style = S("Plain"), Content = r.Current, Width = 22, Height = 22, ToolTip = "Usar este estágio agora" };
            curBtn.Click += (s, e) => { if (st == null || (st.Dpi.ActiveStages & (1 << k)) == 0) return; st.Dpi.CurrentStage = k + 1; ShowDpi(); PushDpi(); };
            UIElement[] cells = { curBtn, num, dotBtn, r.Slider, r.Value, r.On };
            for (int c = 0; c < cells.Length; c++) { Grid.SetColumn(cells[c], c); if (cells[c] is FrameworkElement) ((FrameworkElement)cells[c]).VerticalAlignment = VerticalAlignment.Center; g.Children.Add(cells[c]); }
            r.On.Margin = new Thickness(14, 0, 0, 0);
            r.Card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 9, 14, 9), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Child = g };
            stages.Add(r); list.Children.Add(r.Card);
        }
        indicatorSeg = new Seg(true).Items("Piscando", "Fixa", "Desligada"); indicatorSeg.Changed += i => { if (st != null) { st.Dpi.Indicator = i + 1; PushDpi(); } };
        lodSeg = new Seg(true).Items("Baixa", "Média", "Alta"); lodSeg.Changed += i => { if (st != null) { st.Dpi.LiftOff = i; PushDpi(); } };
        motionSw = new SwitchButton(); motionSw.Click += (s, e) => { if (st != null) { st.Dpi.MotionSync = motionSw.IsChecked == true; PushDpi(); } };
        snapSw = new SwitchButton(); snapSw.Click += (s, e) => { if (st != null) { st.Dpi.AngleSnap = snapSw.IsChecked == true; PushDpi(); } };
        rippleSw = new SwitchButton(); rippleSw.Click += (s, e) => { if (st != null) { st.Dpi.Ripple = rippleSw.IsChecked == true; PushDpi(); } };
        var sensor = Card(Stack(14, Field("Luz indicadora ao trocar o DPI", null, indicatorSeg), Field("Distância de levantamento (LOD)", null, lodSeg),
            Stack(0, ToggleRow("Motion Sync", "Movimento mais consistente, com um pouco mais de latência", motionSw), Sep(),
                ToggleRow("Correção de ângulo", "Endireita linhas retas (ruim para jogos de mira)", snapSw), Sep(),
                ToggleRow("Controle de ondulação", "Suaviza tremidas em DPI alto", rippleSw))), "Sensor");
        dpiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        dpiTimer.Tick += async (s, e) => { dpiTimer.Stop(); await Save(st.Dpi.Build(), null); };
        var left = Stack(16, Card(mouseDpi), sensor);
        return TwoCols(left, 330, Card(list, "Estágios de DPI"));
    }
    void PushDpi() { if (showing) return; dpiTimer.Stop(); dpiTimer.Start(); }
    void ShowDpi()
    {
        if (st == null) return; bool prev = showing; showing = true;
        for (int i = 0; i < 8; i++)
        {
            var r = stages[i]; bool on = (st.Dpi.ActiveStages & (1 << i)) != 0, cur = st.Dpi.CurrentStage == i + 1;
            r.On.IsChecked = on; r.Slider.Value = StepIndex(Math.Max(50, st.Dpi.Dpi[i])); r.Value.Text = DpiSteps[(int)r.Slider.Value].ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
            r.Dot.Fill = new SolidColorBrush(T.Rgb(st.Dpi.Color[i], 1));
            r.Current.Fill = cur ? T.Accent : Brushes.Transparent; r.Current.Stroke = cur ? T.Accent : T.Line2;
            r.Card.Background = cur ? T.B("#14FFFFFF") : Brushes.Transparent; r.Card.BorderBrush = cur ? T.Line2 : Brushes.Transparent;
            r.Slider.IsEnabled = on; r.Value.Opacity = on ? 1 : .4;
        }
        indicatorSeg.Select(Math.Max(0, Math.Min(2, st.Dpi.Indicator - 1)), false); lodSeg.Select(Math.Max(0, Math.Min(2, st.Dpi.LiftOff)), false);
        motionSw.IsChecked = st.Dpi.MotionSync; snapSw.IsChecked = st.Dpi.AngleSnap; rippleSw.IsChecked = st.Dpi.Ripple;
        showing = prev;
    }

    // ============================================================ ILUMINAÇÃO
    readonly List<ToggleButton> modeBtns = new List<ToggleButton>(); Slider brightR, speedR; TextBlock brOut, spOut; Action<int> lightMark; FrameworkElement colorCard, speedField; DispatcherTimer lightTimer;
    static readonly string[] LightNames = { "Desligada", "Fixa", "Respiração", "Neon", "Respiração colorida", "Cor do DPI", "Cor do DPI respirando" };
    FrameworkElement BuildLight()
    {
        mouseLight = new MouseView(false);
        var wrap = new WrapPanel();
        for (int i = 0; i < LightNames.Length; i++)
        {
            int k = i;
            var name = new TextBlock { Text = LightNames[i], FontSize = 12.5, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap };
            var b = new ToggleButton { Style = S("Fx"), Content = name, Width = 150 };
            b.Click += (s, e) => { if (st == null) return; st.Light.Mode = k; ShowLight(); PushLight(); };
            modeBtns.Add(b); wrap.Children.Add(b);
        }
        brightR = Range(1, 8); speedR = Range(1, 5); brOut = Txt(""); spOut = Txt("");
        brightR.ValueChanged += (s, e) => { brOut.Text = ((int)brightR.Value * 100 / 8) + "%"; if (!showing && st != null) { st.Light.Brightness = (int)brightR.Value; PushLight(); } };
        speedR.ValueChanged += (s, e) => { spOut.Text = new[] { "", "Bem lento", "Lento", "Médio", "Rápido", "Bem rápido" }[(int)speedR.Value]; if (!showing && st != null) { st.Light.Speed = (int)speedR.Value; PushLight(); } };
        speedField = Field("Velocidade", spOut, speedR);
        var sw = new WrapPanel(); lightMark = Swatches(sw, c => { if (st == null) return; st.Light.Color = c; PushLight(); });
        colorCard = Card(sw, "Cor");
        lightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        lightTimer.Tick += async (s, e) => { lightTimer.Stop(); await Save(st.Light.Build(), null); };
        var right = Stack(16, Card(wrap, "Efeito"), Card(Stack(18, Field("Brilho", brOut, brightR), speedField)), colorCard);
        return TwoCols(Card(mouseLight), 330, right);
    }
    void PushLight() { if (showing) return; lightTimer.Stop(); lightTimer.Start(); }
    void ShowLight()
    {
        if (st == null) return; bool prev = showing; showing = true;
        for (int i = 0; i < modeBtns.Count; i++) modeBtns[i].IsChecked = i == st.Light.Mode;
        brightR.Value = Math.Max(1, Math.Min(8, st.Light.Brightness)); speedR.Value = Math.Max(1, Math.Min(5, st.Light.Speed));
        brOut.Text = ((int)brightR.Value * 100 / 8) + "%"; spOut.Text = new[] { "", "Bem lento", "Lento", "Médio", "Rápido", "Bem rápido" }[(int)speedR.Value];
        lightMark(st.Light.Color);
        colorCard.Visibility = st.Light.Mode == 1 || st.Light.Mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        speedField.Visibility = st.Light.Mode == 2 || st.Light.Mode == 3 || st.Light.Mode == 4 || st.Light.Mode == 6 ? Visibility.Visible : Visibility.Collapsed;
        showing = prev;
    }
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    void OnFrame(object s, EventArgs e)
    {
        if (st == null || page == "desempenho" || WindowState == WindowState.Minimized) return;
        double t = T.Reduce ? 0 : clock.Elapsed.TotalSeconds, br = st.Light.Brightness / 8.0, sp = .3 + st.Light.Speed * .35;
        int stageColor = st.Dpi.Color[Math.Max(0, Math.Min(7, st.Dpi.CurrentStage - 1))];
        Color c;
        switch (st.Light.Mode)
        {
            case 0: c = Colors.Transparent; break;
            case 1: c = T.Rgb(st.Light.Color, br); break;
            case 2: c = T.Rgb(st.Light.Color, br * (.15 + .85 * (.5 - .5 * Math.Cos(t * sp * 1.6)))); break;
            case 3: c = T.Hsl(t * 60 * sp, 100, 55, br); break;
            case 4: c = T.Hsl(Math.Floor(t * sp * .8) * 70, 100, 55, br * (.15 + .85 * (.5 - .5 * Math.Cos(t * sp * 1.6 * Math.PI / Math.PI)))); break;
            case 5: c = T.Rgb(stageColor, br); break;
            default: c = T.Rgb(stageColor, br * (.15 + .85 * (.5 - .5 * Math.Cos(t * sp * 1.6)))); break;
        }
        var view = page == "dpi" ? mouseDpi : page == "luz" ? mouseLight : page == "botoes" ? mouseBtn : null; // só o desenho que está na tela
        if (view != null && view.Glow.Color != c) { view.Glow.Color = c; view.Logo.Color = c; }
    }

    // ============================================================ BOTÕES
    Button saveBtn; int selSlot = 1; ButtonSlot pending; TextBlock btnTitle, btnNow, pendTxt; WrapPanel actionList; Button capBtn; bool capturing; int capMods; StackPanel btnList;
    FrameworkElement BuildButtons()
    {
        mouseBtn = new MouseView(true); mouseBtn.PartClicked += SelectSlot;
        btnList = new StackPanel();
        btnTitle = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold }; btnNow = Txt("", 12.5, T.Muted);
        actionList = new WrapPanel();
        foreach (var a in Buttons.Actions)
        {
            int code = (int)a[0]; var b = new ToggleButton { Style = S("Chip"), Content = (string)a[1], Tag = code };
            b.Click += (s, e) => { foreach (var o in actionList.Children.OfType<ToggleButton>()) o.IsChecked = o == b; SetPending(new ButtonSlot(code, 0, 0)); };
            actionList.Children.Add(b);
        }
        capBtn = new Button { Style = S("Btn"), Height = 56, HorizontalAlignment = HorizontalAlignment.Stretch, Background = T.Panel2, BorderBrush = T.Line2, Content = Txt("Ou clique aqui e aperte uma tecla/atalho do teclado", 14, T.Muted) };
        capBtn.Click += (s, e) => { capturing = true; capMods = 0; capBtn.BorderBrush = T.Accent; capBtn.Background = T.AccentSoft; capBtn.Content = Txt("Aperte a tecla ou o atalho…", 14); Keyboard.Focus(this); };
        pendTxt = Txt("", 12.5, T.Muted); pendTxt.VerticalAlignment = VerticalAlignment.Center;
        var save = Btn("Salvar no mouse", "Primary"); saveBtn = save; save.Click += async (s, e) => await SaveButton(false);
        var restore = Btn("Voltar ao padrão", "Ghost"); restore.Click += async (s, e) => await SaveButton(true);
        var actions = new DockPanel(); DockPanel.SetDock(pendTxt, Dock.Right); actions.Children.Add(pendTxt); actions.Children.Add(Row(8, save, restore));
        var insp = Card(Stack(14, Stack(2, Txt("Botão escolhido", 12.5, T.Muted), btnTitle, btnNow), Sep(), actionList, capBtn, actions));
        return TwoCols(Stack(16, Card(mouseBtn), Card(btnList, "Botões")), 330, insp);
    }
    static string Describe(ButtonSlot s)
    {
        if (s.Action == Buttons.KEYBOARD)
        {
            var parts = new List<string>(); if ((s.Mods & 1) != 0) parts.Add("Ctrl"); if ((s.Mods & 2) != 0) parts.Add("Shift"); if ((s.Mods & 4) != 0) parts.Add("Alt"); if ((s.Mods & 8) != 0) parts.Add("Win");
            if (s.Key != 0) parts.Add(KeyName(s.Key)); return "Teclado: " + string.Join(" + ", parts.ToArray());
        }
        var a = Buttons.Actions.FirstOrDefault(x => (int)x[0] == s.Action); return a != null ? (string)a[1] : "Ação 0x" + s.Action.ToString("x2");
    }
    static string KeyName(int hid)
    {
        if (hid >= 4 && hid <= 29) return ((char)('A' + hid - 4)).ToString();
        if (hid >= 30 && hid <= 38) return (hid - 29).ToString(); if (hid == 39) return "0";
        if (hid >= 58 && hid <= 69) return "F" + (hid - 57);
        var n = new Dictionary<int, string> { { 40, "Enter" }, { 41, "Esc" }, { 42, "Backspace" }, { 43, "Tab" }, { 44, "Espaço" }, { 57, "Caps" }, { 70, "PrtSc" }, { 73, "Insert" }, { 74, "Home" }, { 75, "PgUp" }, { 76, "Delete" }, { 77, "End" }, { 78, "PgDn" }, { 79, "→" }, { 80, "←" }, { 81, "↓" }, { 82, "↑" } };
        string s; return n.TryGetValue(hid, out s) ? s : "0x" + hid.ToString("x2");
    }
    static int KeyToHid(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return 4 + (k - Key.A);
        if (k >= Key.D1 && k <= Key.D9) return 30 + (k - Key.D1); if (k == Key.D0) return 39;
        if (k >= Key.F1 && k <= Key.F12) return 58 + (k - Key.F1);
        switch (k)
        {
            case Key.Enter: return 40; case Key.Escape: return 41; case Key.Back: return 42; case Key.Tab: return 43; case Key.Space: return 44; case Key.CapsLock: return 57;
            case Key.PrintScreen: return 70; case Key.Insert: return 73; case Key.Home: return 74; case Key.PageUp: return 75; case Key.Delete: return 76; case Key.End: return 77;
            case Key.PageDown: return 78; case Key.Right: return 79; case Key.Left: return 80; case Key.Down: return 81; case Key.Up: return 82;
            case Key.OemMinus: return 45; case Key.OemPlus: return 46; case Key.OemOpenBrackets: return 47; case Key.OemCloseBrackets: return 48; case Key.OemPipe: return 49;
            case Key.OemSemicolon: return 51; case Key.OemQuotes: return 52; case Key.OemTilde: return 53; case Key.OemComma: return 54; case Key.OemPeriod: return 55; case Key.OemQuestion: return 56;
        }
        return 0;
    }
    void HandleKey(object s, KeyEventArgs e)
    {
        if (!capturing) return; e.Handled = true;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (k == Key.LeftCtrl || k == Key.RightCtrl) { capMods |= 1; return; }
        if (k == Key.LeftShift || k == Key.RightShift) { capMods |= 2; return; }
        if (k == Key.LeftAlt || k == Key.RightAlt) { capMods |= 4; return; }
        if (k == Key.LWin || k == Key.RWin) { capMods |= 8; return; }
        int hid = KeyToHid(k); if (hid == 0) return;
        capturing = false; capBtn.BorderBrush = T.Line2; capBtn.Background = T.Panel2;
        var slot = new ButtonSlot(Buttons.KEYBOARD, capMods, hid);
        capBtn.Content = new Border { CornerRadius = new CornerRadius(7), Background = T.Panel3, BorderBrush = T.Line2, BorderThickness = new Thickness(1, 1, 1, 2), Padding = new Thickness(10, 3, 10, 3), Child = Txt(Describe(slot).Replace("Teclado: ", ""), 13, T.Text, FontWeights.Medium) };
        foreach (var o in actionList.Children.OfType<ToggleButton>()) o.IsChecked = false;
        SetPending(slot);
    }
    void SetPending(ButtonSlot s) { pending = s; pendTxt.Text = "Vai virar: " + Describe(s); }
    void SelectSlot(int slot)
    {
        selSlot = slot; pending = null; pendTxt.Text = ""; mouseBtn.Select(slot);
        int idx = Array.IndexOf(Buttons.PhysicalSlots, slot);
        btnTitle.Text = idx >= 0 ? Buttons.PhysicalNames[idx] : "Botão " + slot;
        btnNow.Text = st != null ? "Agora faz: " + Describe(st.Buttons[slot - 1]) : "";
        foreach (var o in actionList.Children.OfType<ToggleButton>()) o.IsChecked = st != null && (int)o.Tag == st.Buttons[slot - 1].Action;
        capBtn.Content = Txt("Ou clique aqui e aperte uma tecla/atalho do teclado", 14, T.Muted);
        RenderBtnList();
    }
    void RenderBtnList()
    {
        btnList.Children.Clear(); if (st == null) return;
        for (int i = 0; i < Buttons.PhysicalSlots.Length; i++)
        {
            int slot = Buttons.PhysicalSlots[i]; bool on = slot == selSlot;
            var dp = new DockPanel(); var v = Txt(Describe(st.Buttons[slot - 1]), 12, T.Muted); v.TextWrapping = TextWrapping.NoWrap; v.TextTrimming = TextTrimming.CharacterEllipsis; v.MaxWidth = 150; v.Margin = new Thickness(8, 0, 0, 0); v.ToolTip = Describe(st.Buttons[slot - 1]); DockPanel.SetDock(v, Dock.Right); dp.Children.Add(v);
            dp.Children.Add(new TextBlock { Text = Buttons.PhysicalNames[i], Foreground = on ? T.Text : T.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Button { Style = S("Plain"), Height = 38, Padding = new Thickness(12, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = dp, Margin = new Thickness(0, 0, 0, 4),
                Background = on ? T.Panel3 : Brushes.Transparent, BorderBrush = on ? T.Line : Brushes.Transparent, BorderThickness = new Thickness(1) };
            b.Click += (s, e) => SelectSlot(slot); btnList.Children.Add(b);
        }
    }
    void ShowButtons() { SelectSlot(selSlot); }
    async Task SaveButton(bool restore)
    {
        if (st == null) return;
        var slot = restore ? Buttons.Defaults()[selSlot - 1] : pending;
        if (slot == null) { Toast("Escolha primeiro o que o botão deve fazer.", true); return; }
        if (selSlot == 1 && slot.Action != 0x02 && !restore)
        { if (MessageBox.Show(this, "Você está trocando o clique esquerdo. Se ele deixar de clicar, use o teclado (Tab/Enter) para voltar aqui e restaurar. Continuar?", "X11 Studio", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; }
        var copy = st.Buttons.Select(b => new ButtonSlot(b.Action, b.Mods, b.Key)).ToArray(); copy[selSlot - 1] = slot;
        if (!await Save(Buttons.Build(st.Profile, copy), restore ? "Botão voltou ao padrão" : "Botão salvo no mouse")) return;
        st.Buttons = copy; SelectSlot(selSlot); if (!restore) T.Confirm(saveBtn, "Salvo");
    }

    // ============================================================ DESEMPENHO
    Seg pollSeg; Slider respR, sleepR, deepR; TextBlock respOut, sleepOut, deepOut; DispatcherTimer perfTimer;
    FrameworkElement BuildPerf()
    {
        pollSeg = new Seg(true).Items("125 Hz", "250 Hz", "500 Hz", "1000 Hz");
        pollSeg.Changed += async i => { if (st == null) return; st.PollHz = Poll.Rates[i]; await Save(Poll.Build(st.Profile, st.PollHz), "Taxa de resposta: " + st.PollHz + " Hz"); };
        respR = Range(2, 25); sleepR = Range(1, 60); deepR = Range(1, 60); respOut = Txt(""); sleepOut = Txt(""); deepOut = Txt("");
        respR.ValueChanged += (s, e) => { respOut.Text = ((int)respR.Value * 2) + " ms"; if (!showing && st != null) { st.Light.KeyResponseMs = (int)respR.Value * 2; PushPerf(); } };
        sleepR.ValueChanged += (s, e) => { double m = sleepR.Value / 2.0; sleepOut.Text = (m < 1 ? "30 segundos" : m.ToString("0.#", new System.Globalization.CultureInfo("pt-BR")) + " min"); if (!showing && st != null) { st.Light.SleepMin = m; PushPerf(); } };
        deepR.ValueChanged += (s, e) => { deepOut.Text = (int)deepR.Value + " min"; if (!showing && st != null) { st.Light.DeepSleepMin = (int)deepR.Value; PushPerf(); } };
        perfTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        perfTimer.Tick += async (s, e) => { perfTimer.Stop(); await Save(st.Light.Build(), null); };
        var rate = Card(Stack(10, pollSeg, Txt("1000 Hz responde mais rápido; 125 Hz gasta menos bateria.", 12.5, T.Muted)), "Taxa de resposta");
        var click = Card(Field("Tempo de resposta do clique (debounce)", respOut, respR), "Clique");
        var energy = Card(Stack(18, Field("Apagar a luz depois de", sleepOut, sleepR), Field("Dormir de vez depois de", deepOut, deepR)), "Bateria");
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = Stack(16, rate, click); g.Children.Add(l); Grid.SetColumn(energy, 2); g.Children.Add(energy);
        return g;
    }
    void PushPerf() { if (showing) return; perfTimer.Stop(); perfTimer.Start(); }
    void ShowPerf()
    {
        if (st == null) return; bool prev = showing; showing = true;
        pollSeg.Select(Array.IndexOf(Poll.Rates, st.PollHz), false);
        respR.Value = Math.Max(2, Math.Min(25, st.Light.KeyResponseMs / 2)); respOut.Text = ((int)respR.Value * 2) + " ms";
        sleepR.Value = Math.Max(1, Math.Min(60, (int)Math.Round(st.Light.SleepMin * 2))); double m = sleepR.Value / 2.0; sleepOut.Text = m < 1 ? "30 segundos" : m.ToString("0.#", new System.Globalization.CultureInfo("pt-BR")) + " min";
        deepR.Value = Math.Max(1, Math.Min(60, st.Light.DeepSleepMin)); deepOut.Text = (int)deepR.Value + " min";
        showing = prev;
    }
}

static class X11Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool fresh; var mutex = new Mutex(true, "X11Studio-single", out fresh);
        if (!fresh) { MessageBox.Show("O X11 Studio já está aberto.", "X11 Studio"); return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(T.Styles());
        var log = new StringBuilder();
        app.DispatcherUnhandledException += (s, e) => { log.AppendLine(e.Exception.ToString()); if (args.Length == 0) MessageBox.Show("Erro inesperado: " + e.Exception.Message, "X11 Studio"); e.Handled = true; };
        var w = new X11Window();
        if (args.Length == 2 && args[0] == "--shot")
        {
            w.Loaded += async (s, e) =>
            {
                await Task.Delay(3500);
                foreach (var pg in new[] { "dpi", "luz", "botoes", "desempenho" })
                {
                    try { typeof(X11Window).GetMethod("Go", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(w, new object[] { pg }); } catch (Exception ex) { log.AppendLine(pg + ": " + (ex.InnerException ?? ex)); }
                    await Task.Delay(900);
                    var rt = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); rt.Render((Visual)w.Content);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rt));
                    using (var fs = File.Create(System.IO.Path.Combine(args[1], "x11_" + pg + ".png"))) enc.Save(fs);
                }
                File.WriteAllText(System.IO.Path.Combine(args[1], "x11_log.txt"), log.ToString());
                w.Close();
            };
        }
        app.Run(w);
        GC.KeepAlive(mutex);
    }
}
