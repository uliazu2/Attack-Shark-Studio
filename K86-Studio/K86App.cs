// K86 Studio — app nativo (WPF) para o Attack Shark K86.
//
// Compilar (tudo já vem com o Windows):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:K86Studio.exe
//     /r:WindowsBase.dll /r:PresentationCore.dll /r:PresentationFramework.dll /r:System.Xaml.dll
//     /r:System.Drawing.dll /r:System.Windows.Forms.dll K86App.cs UiKit.cs HidCore.cs K86Core.cs K86Backend.cs
//
// Protocolo do teclado (do software oficial da Attack Shark para o K86 e de gravações USB):
//   Relatório de feature de 64 bytes, report ID 0, coleção vendor (UsagePage 0xFFFF), só pelo cabo (PID 0x4015).
//   Checksum "Bit7": byte[7] = 0xFF - soma(0..6). O efeito de luz usa "Bit8": byte[8] = 0xFF - soma(0..7).
//   0x02 reset · 0x05/0x85 perfil · 0x06/0x86 opções · 0x07/0x87 efeito [modo,vel,brilho,flags,R,G,B]
//   0x0B macro · 0x0C cores por tecla (LED = coluna*6+linha) · 0x0D música (nível 0..6 por coluna)
//   0x0E cor da tela [R,G,B,FF] · 0x11/0x91 debounce · 0x12/0x92 suspensão · 0x13 troca 1 tecla
//   0x89 lê mapa de teclas (8 páginas) · 0x28 relógio · 0xA5/0x25 imagem RGB565 240x135 · 0x2C apaga imagens
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
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using D = System.Drawing;

// ======================================================================= desenho do teclado
class KeyVis { public int Idx; public double X, Y, W; public Grid Root; public Border Cap, Led; public SolidColorBrush LedBrush; public TextBlock Label, Sub; }

class KbView : Viewbox
{
    public const double U = 48, W = 18.25, TOP = 1.75, H = TOP + 6.25;
    public readonly Dictionary<int, KeyVis> Keys = new Dictionary<int, KeyVis>();
    public readonly List<KeyVis> List = new List<KeyVis>();
    public Ellipse Knob; public SolidColorBrush KnobGlow = new SolidColorBrush(Colors.Transparent); public Image Screen;
    public event Action<int> KeyHit = delegate { }; public event Action<int> KeyEnterPressed = delegate { };
    readonly bool interactive;

    static readonly object[][][] Rows = {
        new[] { K(41,"Esc"), G(1), K(58), K(59), K(60), K(61), G(.5), K(62), K(63), K(64), K(65), G(.5), K(66), K(67), K(68), K(69), G(.25), K(70,"PrtSc"), K(71,"ScrLk"), K(72,"Pause") },
        new[] { K(53), K(30), K(31), K(32), K(33), K(34), K(35), K(36), K(37), K(38), K(39), K(45), K(46), K(42,"Backspace",2), G(.25), K(73,"Ins"), K(74,"Home"), K(75,"PgUp") },
        new[] { K(43,"Tab",1.5), K(20), K(26), K(8), K(21), K(23), K(28), K(24), K(12), K(18), K(19), K(47), K(48), K(49,"\\",1.5), G(.25), K(76,"Del"), K(77,"End"), K(78,"PgDn") },
        new[] { K(57,"Caps",1.75), K(4), K(22), K(7), K(9), K(10), K(11), K(13), K(14), K(15), K(51), K(52), K(40,"Enter",2.25) },
        new[] { K(225,"Shift",2.25), K(29), K(27), K(6), K(25), K(5), K(17), K(16), K(54), K(55), K(56), K(229,"Shift",2.75), G(1.25), K(82,"↑") },
        new[] { K(224,"Ctrl",1.25), K(227,"Win",1.25), K(226,"Alt",1.25), K(44,"",6.25), K(230,"Alt",1.25), K(-1,"Fn",1.25), K(101,"Menu",1.25), K(228,"Ctrl",1.25), G(.25), K(80,"←"), K(81,"↓"), K(79,"→") } };
    static object[] K(int code, string label = null, double w = 1) { return new object[] { code, label, w }; }
    static object[] G(double w) { return new object[] { null, null, w }; }

    public static int IdxOf(byte a, byte b, byte c, byte d)
    {
        for (int i = 0; i < 128; i++) if (P.DefaultMatrix[i * 4] == a && P.DefaultMatrix[i * 4 + 1] == b && P.DefaultMatrix[i * 4 + 2] == c && P.DefaultMatrix[i * 4 + 3] == d) return i;
        return -1;
    }
    public static readonly int KnobPress = IdxOf(3, 0, 226, 0), KnobDown = IdxOf(3, 0, 234, 0), KnobUp = IdxOf(3, 0, 233, 0);

    public KbView(bool interactive)
    {
        this.interactive = interactive; Stretch = Stretch.Uniform; StretchDirection = StretchDirection.Both;
        var shell = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(16), BorderBrush = T.Line2, BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(T.C("#18181E"), T.C("#101014"), 90), Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 14, Opacity = .55, Direction = 270 } };
        var cv = new Canvas { Width = W * U, Height = H * U };
        shell.Child = cv; Child = shell;
        for (int r = 0; r < Rows.Length; r++)
        {
            double x = 0, y = TOP + r + (r > 0 ? .25 : 0);
            foreach (var k in Rows[r])
            {
                double w = Convert.ToDouble(k[2]);
                if (k[0] != null)
                {
                    int code = (int)k[0]; int idx = code == -1 ? IdxOf(10, 1, 0, 0) : IdxOf(0, 0, (byte)code, 0);
                    string label = (string)k[1] ?? (code == 44 ? "" : Names.Hid(code));
                    AddKey(cv, idx, x, y, w, label);
                }
                x += w;
            }
        }
        // telinha e botão giratório
        var scrBorder = new Border { Width = 2.7 * U, Height = 1.52 * U, CornerRadius = new CornerRadius(6), Background = Brushes.Black, BorderBrush = T.B("#26262E"), BorderThickness = new Thickness(2), ClipToBounds = true };
        Screen = new Image { Stretch = Stretch.UniformToFill }; RenderOptions.SetBitmapScalingMode(Screen, BitmapScalingMode.HighQuality); scrBorder.Child = Screen;
        Canvas.SetLeft(scrBorder, 11.2 * U); Canvas.SetTop(scrBorder, .05 * U); cv.Children.Add(scrBorder);
        var glow = new Ellipse { Width = 1.5 * U, Height = 1.5 * U, Fill = KnobGlow, Effect = new BlurEffect { Radius = 22 } };
        Canvas.SetLeft(glow, 16.15 * U); Canvas.SetTop(glow, .02 * U); cv.Children.Add(glow);
        Knob = new Ellipse { Width = 1.5 * U, Height = 1.5 * U, Stroke = T.Line2, StrokeThickness = 1, Fill = new RadialGradientBrush(T.C("#3A3A44"), T.C("#15151A")) { GradientOrigin = new Point(.35, .3), Center = new Point(.4, .35), RadiusX = .7, RadiusY = .7 } };
        Canvas.SetLeft(Knob, 16.15 * U); Canvas.SetTop(Knob, .02 * U); cv.Children.Add(Knob);
        var notch = new Rectangle { Width = 2, Height = .33 * U, Fill = T.B("#9A9AA6"), RadiusX = 1, RadiusY = 1 };
        Canvas.SetLeft(notch, 16.9 * U - 1); Canvas.SetTop(notch, .17 * U); cv.Children.Add(notch);
        if (interactive) { Knob.Cursor = Cursors.Hand; Knob.MouseLeftButtonDown += (s, e) => KeyHit(KnobPress); }
    }

    void AddKey(Canvas cv, int idx, double x, double y, double w, string label)
    {
        var kv = new KeyVis { Idx = idx, X = x, Y = y, W = w };
        var root = new Grid { Width = w * U - 4, Height = U - 4, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1), Background = Brushes.Transparent };
        var shadow = new Border { CornerRadius = new CornerRadius(7), Background = T.B("#0B0B0E"), Margin = new Thickness(0, 2, 0, -2) };
        var cap = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), BorderBrush = T.B("#10FFFFFF"), Background = new LinearGradientBrush(T.C("#22222A"), T.C("#18181E"), 90) };
        kv.LedBrush = new SolidColorBrush(Colors.Transparent);
        var led = new Border { CornerRadius = new CornerRadius(7), Background = kv.LedBrush };
        var lbl = new TextBlock { Text = label, FontSize = 11, Foreground = T.B("#C9C9D3"), Margin = new Thickness(7, 5, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var sub = new TextBlock { FontSize = 9.5, Foreground = T.Accent, Margin = new Thickness(7, 0, 4, 4), VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis };
        root.Children.Add(shadow); root.Children.Add(cap); root.Children.Add(led); root.Children.Add(lbl); root.Children.Add(sub);
        kv.Root = root; kv.Cap = cap; kv.Led = led; kv.Label = lbl; kv.Sub = sub;
        Canvas.SetLeft(root, x * U + 2); Canvas.SetTop(root, y * U + 2); cv.Children.Add(root);
        if (interactive)
        {
            root.Cursor = Cursors.Hand;
            root.MouseLeftButtonDown += (s, e) => { PressAnim(root, .97, 100); KeyHit(idx); };
            root.MouseLeftButtonUp += (s, e) => PressAnim(root, 1, 160);
            root.MouseLeave += (s, e) => PressAnim(root, 1, 160);
            root.MouseEnter += (s, e) => { if (Mouse.LeftButton == MouseButtonState.Pressed) KeyEnterPressed(idx); };
        }
        if (idx >= 0) Keys[idx] = kv; List.Add(kv);
    }
    static void PressAnim(Grid g, double to, int ms) { var st = (ScaleTransform)g.RenderTransform; T.Anim(st, ScaleTransform.ScaleXProperty, to, ms, T.EaseOut); T.Anim(st, ScaleTransform.ScaleYProperty, to, ms, T.EaseOut); }
    public void Mark(int idx, bool selected, bool modified, string sub)
    {
        KeyVis k; if (!Keys.TryGetValue(idx, out k)) return;
        k.Cap.BorderBrush = selected ? T.Accent : modified ? T.B("#808B7CFF") : T.B("#10FFFFFF");
        k.Cap.BorderThickness = new Thickness(selected ? 2 : 1);
        k.Sub.Text = sub ?? "";
    }
}

// ======================================================================= nomes
static class Names
{
    static readonly Dictionary<int, string> hid = Build();
    static Dictionary<int, string> Build()
    {
        var d = new Dictionary<int, string>();
        for (int i = 0; i < 26; i++) d[4 + i] = ((char)('A' + i)).ToString();
        for (int i = 1; i <= 9; i++) d[29 + i] = i.ToString(); d[39] = "0";
        var n = new[] { "Enter", "Esc", "Backspace", "Tab", "Espaço", "-", "=", "[", "]", "\\", "#", ";", "'", "`", ",", ".", "/", "Caps Lock" };
        for (int i = 0; i < n.Length; i++) d[40 + i] = n[i];
        for (int i = 1; i <= 12; i++) d[57 + i] = "F" + i;
        var m = new[] { "Print Screen", "Scroll Lock", "Pause", "Insert", "Home", "Page Up", "Delete", "End", "Page Down", "→", "←", "↓", "↑", "Num Lock", "Num /", "Num *", "Num -", "Num +", "Num Enter" };
        for (int i = 0; i < m.Length; i++) d[70 + i] = m[i];
        for (int i = 1; i <= 9; i++) d[88 + i] = "Num " + i; d[98] = "Num 0"; d[99] = "Num ."; d[100] = "\\ ISO"; d[101] = "Menu";
        for (int i = 13; i <= 24; i++) d[104 + i - 13] = "F" + i;
        d[224] = "Ctrl"; d[225] = "Shift"; d[226] = "Alt"; d[227] = "Win"; d[228] = "Ctrl dir."; d[229] = "Shift dir."; d[230] = "Alt Gr"; d[231] = "Win dir.";
        return d;
    }
    public static IEnumerable<KeyValuePair<int, string>> AllHid { get { return hid; } }
    public static string Hid(int k) { string s; return hid.TryGetValue(k, out s) ? s : "0x" + k.ToString("x2"); }
    public static readonly object[][] Media = { new object[] { "Mudo", 0xE2 }, new object[] { "Volume +", 0xE9 }, new object[] { "Volume −", 0xEA }, new object[] { "Play/Pause", 0xCD },
        new object[] { "Próxima", 0xB5 }, new object[] { "Anterior", 0xB6 }, new object[] { "Parar", 0xB7 }, new object[] { "Calculadora", 0x192 }, new object[] { "Meu computador", 0x194 },
        new object[] { "Navegador", 0x223 }, new object[] { "E-mail", 0x18A }, new object[] { "Brilho da tela +", 0x6F }, new object[] { "Brilho da tela −", 0x70 } };
    public static readonly object[][] Mouse = { new object[] { "Clique esquerdo", new byte[] { 1, 0, 240, 0 } }, new object[] { "Clique direito", new byte[] { 1, 0, 241, 0 } },
        new object[] { "Clique do meio", new byte[] { 1, 0, 242, 0 } }, new object[] { "Voltar", new byte[] { 1, 0, 243, 0 } }, new object[] { "Avançar", new byte[] { 1, 0, 244, 0 } },
        new object[] { "Rolar para cima", new byte[] { 1, 0, 245, 1 } }, new object[] { "Rolar para baixo", new byte[] { 1, 0, 245, 255 } } };
    public static bool Same(byte[] a, byte[] b) { for (int i = 0; i < 4; i++) if (a[i] != b[i]) return false; return true; }
    public static string Describe(byte[] c)
    {
        if ((c[0] | c[1] | c[2] | c[3]) == 0) return "Nada";
        if (c[0] == 0) return string.Join(" + ", new[] { c[1], c[2], c[3] }.Where(x => x != 0).Select(x => Hid(x)).ToArray());
        if (c[0] == 3) { int code = c[2] | (c[3] << 8); foreach (var m in Media) if ((int)m[1] == code) return (string)m[0]; return "Mídia"; }
        if (c[0] == 1) { foreach (var m in Mouse) if (Same((byte[])m[1], c)) return (string)m[0]; return "Mouse"; }
        if (c[0] == 9) return "Macro " + (c[2] + 1);
        if (c[0] == 10 && c[1] == 1) return "Fn";
        return "Especial";
    }
    public static byte[] Default(int i) { var r = new byte[4]; if (i < 0) return r; Array.Copy(P.DefaultMatrix, i * 4, r, 0, 4); return r; }
    public static int KeyToHid(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return 4 + (k - Key.A);
        if (k >= Key.D1 && k <= Key.D9) return 30 + (k - Key.D1);
        if (k >= Key.F1 && k <= Key.F12) return 58 + (k - Key.F1);
        if (k >= Key.F13 && k <= Key.F24) return 104 + (k - Key.F13);
        if (k >= Key.NumPad1 && k <= Key.NumPad9) return 89 + (k - Key.NumPad1);
        switch (k)
        {
            case Key.D0: return 39; case Key.NumPad0: return 98; case Key.Enter: return 40; case Key.Escape: return 41; case Key.Back: return 42; case Key.Tab: return 43; case Key.Space: return 44;
            case Key.OemMinus: return 45; case Key.OemPlus: return 46; case Key.OemOpenBrackets: return 47; case Key.OemCloseBrackets: return 48; case Key.OemPipe: return 49;
            case Key.OemSemicolon: return 51; case Key.OemQuotes: return 52; case Key.OemTilde: return 53; case Key.OemComma: return 54; case Key.OemPeriod: return 55; case Key.OemQuestion: return 56;
            case Key.CapsLock: return 57; case Key.PrintScreen: return 70; case Key.Scroll: return 71; case Key.Pause: return 72; case Key.Insert: return 73; case Key.Home: return 74;
            case Key.PageUp: return 75; case Key.Delete: return 76; case Key.End: return 77; case Key.PageDown: return 78; case Key.Right: return 79; case Key.Left: return 80;
            case Key.Down: return 81; case Key.Up: return 82; case Key.NumLock: return 83; case Key.Divide: return 84; case Key.Multiply: return 85; case Key.Subtract: return 86;
            case Key.Add: return 87; case Key.Decimal: return 99; case Key.OemBackslash: return 100; case Key.Apps: return 101;
            case Key.LeftCtrl: return 224; case Key.LeftShift: return 225; case Key.LeftAlt: return 226; case Key.LWin: return 227;
            case Key.RightCtrl: return 228; case Key.RightShift: return 229; case Key.RightAlt: return 230; case Key.RWin: return 231;
        }
        return 0;
    }
}

// ======================================================================= janela principal
class MainWindow : Window
{
    readonly Backend be = new Backend();
    readonly string dataDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K86Studio");
    bool connected; int profile; EffectState effect = new EffectState { Mode = 4, Bright = 4, Speed = 2, Rainbow = true, Color = 0x8B7CFF };
    readonly Grid pages = new Grid(); readonly Dictionary<string, FrameworkElement> pageMap = new Dictionary<string, FrameworkElement>();
    readonly StackPanel toasts = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 20, 20) };
    TextBlock title, subtitle, stTitle, stSub, bannerTxt; Ellipse dot; Border banner; StackPanel headerActions; Border navInd; readonly List<Button> navBtns = new List<Button>(); string page = "luz";
    Seg profileSeg;

    static Style S(string k) { return (Style)Application.Current.Resources[k]; }
    static TextBlock Txt(string t, double size = 14, Brush fg = null, FontWeight? w = null)
    { return new TextBlock { Text = t, FontSize = size, Foreground = fg ?? T.Text, FontWeight = w ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap }; }
    static Border Card(UIElement child, string heading = null)
    {
        var sp = new StackPanel();
        if (heading != null) sp.Children.Add(new TextBlock { Text = heading.ToUpper(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = T.Muted, Margin = new Thickness(0, 0, 0, 12) });
        sp.Children.Add(child);
        return new Border { Background = T.Panel, BorderBrush = T.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Child = sp };
    }
    static Button Btn(string text, string style = "Btn") { return new Button { Content = text, Style = S(style) }; }
    static ToggleButton Switch() { return new SwitchButton { VerticalAlignment = VerticalAlignment.Center }; }
    static Slider Range(int min, int max) { return new Slider { Style = S("Range"), Minimum = min, Maximum = max }; }
    static Grid Cols(params object[] parts) // números = largura em estrelas
    {
        var g = new Grid(); int c = 0;
        for (int i = 0; i < parts.Length; i += 2)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Convert.ToDouble(parts[i]), GridUnitType.Star) });
            if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            var el = (UIElement)parts[i + 1]; Grid.SetColumn(el, c); g.Children.Add(el); c += 2;
        }
        // reorganiza: colunas separadoras entre as de conteúdo
        g.ColumnDefinitions.Clear();
        for (int i = 0; i < parts.Length / 2; i++) { if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Convert.ToDouble(parts[i * 2]), GridUnitType.Star) }); }
        return g;
    }
    static StackPanel Stack(double gap, params UIElement[] items)
    {
        var sp = new StackPanel();
        for (int i = 0; i < items.Length; i++) { if (items[i] == null) continue; var fe = items[i] as FrameworkElement; if (i > 0 && fe != null) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + gap, fe.Margin.Right, fe.Margin.Bottom); sp.Children.Add(items[i]); }
        return sp;
    }
    static StackPanel Row(double gap, params UIElement[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < items.Length; i++) { if (i > 0 && items[i] is FrameworkElement) { var fe = (FrameworkElement)items[i]; fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom); } if (items[i] is FrameworkElement) ((FrameworkElement)items[i]).VerticalAlignment = VerticalAlignment.Center; sp.Children.Add(items[i]); }
        return sp;
    }
    static DockPanel Field(string label, TextBlock value, UIElement control)
    {
        var dp = new DockPanel { LastChildFill = true };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(head, Dock.Top);
        var l = Txt(label, 12.5, T.Muted); head.Children.Add(l);
        if (value != null) { value.HorizontalAlignment = HorizontalAlignment.Right; value.FontSize = 12.5; head.Children.Add(value); }
        dp.Children.Add(head); dp.Children.Add(control); return dp;
    }
    static Grid ToggleRow(string titleT, string sub, UIElement right)
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 10) }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel(); sp.Children.Add(Txt(titleT, 14, T.Text, FontWeights.Medium)); if (sub != null) sp.Children.Add(Txt(sub, 12.5, T.Muted));
        g.Children.Add(sp); Grid.SetColumn(right, 1); g.Children.Add(right); return g;
    }

    public MainWindow()
    {
        Title = "K86 Studio"; Width = 1260; Height = 880; MinWidth = 1000; MinHeight = 700; Background = T.Bg; Foreground = T.Text; FontFamily = T.Font; FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SourceInitialized += (s, e) => { try { int on = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, 4); } catch { } };
        Directory.CreateDirectory(dataDir);

        var root = new Grid { Background = T.Bg }; Content = root;
        var shell = new Grid(); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) }); shell.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(shell); root.Children.Add(toasts);
        var side = BuildSidebar(); shell.Children.Add(side);
        var main = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(34, 26, 34, 50) };
        Grid.SetColumn(main, 1); shell.Children.Add(main);
        var mainStack = new StackPanel { MaxWidth = 1100 }; main.Content = mainStack;

        banner = new Border { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(12), BorderBrush = T.B("#4DFBBF24"), BorderThickness = new Thickness(1), Background = T.B("#14FBBF24"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 18) };
        var bg = new DockPanel(); var retry = Btn("Tentar de novo"); retry.Click += (s, e) => RefreshStatus(true); DockPanel.SetDock(retry, Dock.Right); bg.Children.Add(retry);
        bannerTxt = Txt("", 13.5, T.B("#FDE68A")); bannerTxt.VerticalAlignment = VerticalAlignment.Center; bg.Children.Add(bannerTxt); banner.Child = bg;
        mainStack.Children.Add(banner);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom }; DockPanel.SetDock(headerActions, Dock.Right); header.Children.Add(headerActions);
        title = new TextBlock { FontFamily = T.Display, FontSize = 27, FontWeight = FontWeights.SemiBold };
        subtitle = Txt("", 14, T.Muted); subtitle.Margin = new Thickness(0, 4, 0, 0);
        header.Children.Add(Stack(0, title, subtitle));
        mainStack.Children.Add(header); mainStack.Children.Add(pages);

        AddPage("luz", BuildLight()); AddPage("pintar", BuildPaint()); AddPage("teclas", BuildKeys());
        AddPage("macros", BuildMacros()); AddPage("tela", BuildScreen()); AddPage("ajustes", BuildSettings());

        PreviewKeyDown += HandleKeyDown; PreviewKeyUp += HandleKeyUp;
        CompositionTarget.Rendering += OnFrame;
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) }; poll.Tick += (s, e) => RefreshStatus(false); poll.Start();
        Loaded += (s, e) => { Go("luz"); ShowEffect(); RefreshStatus(true); };
        Closed += (s, e) => { be.Sync.Mode = "off"; Thread.Sleep(250); };
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    void AddPage(string key, FrameworkElement el) { el.Visibility = Visibility.Collapsed; pages.Children.Add(el); pageMap[key] = el; }

    // ------------------------------------------------------------ sidebar
    FrameworkElement BuildSidebar()
    {
        var side = new Border { BorderBrush = T.Line, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(14, 20, 14, 20), Background = new LinearGradientBrush(T.C("#0D0D10"), T.C("#09090B"), 90) };
        var dp = new DockPanel(); side.Child = dp;
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 24) }; DockPanel.SetDock(brand, Dock.Top);
        var mark = new Grid { Width = 30, Height = 30 };
        mark.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(T.C("#8B7CFF"), 0), new GradientStop(T.C("#5EEAD4"), .5), new GradientStop(T.C("#F472B6"), 1) }, 45), Effect = new DropShadowEffect { Color = T.C("#8B7CFF"), BlurRadius = 18, ShadowDepth = 0, Opacity = .5 } });
        mark.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = T.B("#0D0D10"), Margin = new Thickness(3) });
        mark.Children.Add(new Border { CornerRadius = new CornerRadius(2), BorderBrush = T.B("#CFC8FF"), BorderThickness = new Thickness(1.5, 3, 1.5, 1.5), Margin = new Thickness(8, 9, 8, 9) });
        brand.Children.Add(mark);
        var bt = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bt.Children.Add(new TextBlock { Text = "K86 Studio", FontFamily = T.Display, FontWeight = FontWeights.SemiBold, FontSize = 15 }); bt.Children.Add(Txt("Attack Shark K86", 11.5, T.Muted));
        brand.Children.Add(bt); dp.Children.Add(brand);

        var nav = new Grid(); DockPanel.SetDock(nav, Dock.Top);
        navInd = new Border { Height = 38, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(10), Background = T.Panel3, BorderBrush = T.Line, BorderThickness = new Thickness(1), RenderTransform = new TranslateTransform() };
        nav.Children.Add(navInd);
        var navStack = new StackPanel(); nav.Children.Add(navStack);
        var items = new[] { new[] { "luz", "Iluminação", "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" },
            new[] { "pintar", "Pintar teclas", "M18.4 2.6a2 2 0 0 1 2.8 2.8L10 16.6 7 17l.4-3zM7 17c-1.5 0-3 1-3 3 0 1 .5 2 2 2" },
            new[] { "teclas", "Teclas", "M4.5 6h15A2.5 2.5 0 0 1 22 8.5v8a2.5 2.5 0 0 1-2.5 2.5h-15A2.5 2.5 0 0 1 2 16.5v-8A2.5 2.5 0 0 1 4.5 6zM6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 15h10" },
            new[] { "macros", "Macros", "M4 6h10M4 12h16M4 18h7M18 4a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM15 16a2 2 0 1 0 0 4 2 2 0 0 0 0-4z" },
            new[] { "tela", "Tela", "M5 5h14a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zM3 15l5-5 4 4 3-3 6 6" },
            new[] { "ajustes", "Ajustes", "M4 7h10M18 7h2M4 17h4M12 17h8M16 5a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM10 15a2 2 0 1 0 0 4 2 2 0 0 0 0-4z" } };
        foreach (var it in items)
        {
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(it[2]), Stroke = T.Muted, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 18, Height = 18, Stretch = Stretch.Uniform };
            var lbl = new TextBlock { Text = it[1], Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var b = new Button { Style = S("Plain"), Height = 38, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 0, 0, 0), Foreground = T.Muted, Tag = it[0],
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { path, lbl } } };
            string key = it[0]; b.Click += (s, e) => Go(key);
            navStack.Children.Add(b); navBtns.Add(b);
        }
        dp.Children.Add(nav);

        var prof = new StackPanel { Margin = new Thickness(0, 24, 0, 0) }; DockPanel.SetDock(prof, Dock.Top);
        prof.Children.Add(new TextBlock { Text = "PERFIL", FontSize = 11, Foreground = T.Faint, Margin = new Thickness(8, 0, 0, 8) });
        profileSeg = new Seg(true).Items("1", "2", "3"); profileSeg.Select(0, false);
        profileSeg.Changed += async i => { if (!connected) return; await Run(() => be.SetProfile(i), "Perfil " + (i + 1) + " ativado"); profile = i; await LoadAll(); };
        prof.Children.Add(profileSeg); dp.Children.Add(prof);

        var status = new Border { CornerRadius = new CornerRadius(12), BorderBrush = T.Line, BorderThickness = new Thickness(1), Background = T.Panel, Padding = new Thickness(12), VerticalAlignment = VerticalAlignment.Bottom };
        DockPanel.SetDock(status, Dock.Bottom);
        dot = new Ellipse { Width = 8, Height = 8, Fill = T.Faint, Margin = new Thickness(0, 0, 9, 0) };
        stTitle = new TextBlock { Text = "Procurando…", FontWeight = FontWeights.Medium }; stSub = Txt("Ligue o K86 no cabo USB", 12, T.Muted); stSub.Margin = new Thickness(0, 4, 0, 0);
        status.Child = Stack(0, Row(0, dot, stTitle), stSub);
        dp.Children.Add(status);
        dp.Children.Add(new Border());
        return side;
    }

    void Go(string key)
    {
        page = key;
        var info = new Dictionary<string, string[]> {
            { "luz", new[] { "Iluminação", "Escolha um efeito: ele já aparece no teclado enquanto você mexe." } },
            { "pintar", new[] { "Pintar teclas", "Clique ou arraste sobre as teclas para pintar cada uma de uma cor." } },
            { "teclas", new[] { "Teclas", "Troque o que cada tecla faz. Fica salvo dentro do teclado, no perfil escolhido." } },
            { "macros", new[] { "Macros", "Grave sequências de teclas e coloque numa tecla só." } },
            { "tela", new[] { "Tela", "Coloque uma foto ou GIF na telinha do K86." } },
            { "ajustes", new[] { "Ajustes", "Energia, travas e opções do teclado." } } };
        title.Text = info[key][0]; subtitle.Text = info[key][1];
        for (int i = 0; i < navBtns.Count; i++)
        {
            var b = navBtns[i]; bool on = (string)b.Tag == key;
            b.Foreground = on ? T.Text : T.Muted; ((System.Windows.Shapes.Path)((StackPanel)b.Content).Children[0]).Stroke = on ? T.Text : T.Muted;
            if (on) T.Anim((TranslateTransform)navInd.RenderTransform, TranslateTransform.YProperty, i * 38, 220, T.InOut);
        }
        foreach (var kv in pageMap)
        {
            bool on = kv.Key == key; kv.Value.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) { kv.Value.Opacity = 0; T.Anim(kv.Value, OpacityProperty, 1, 160, T.EaseOut); }
        }
        headerActions.Children.Clear();
        if (key == "teclas") { var b = Btn("Restaurar todas", "Ghost"); b.Click += async (s, e) => { await Run(() => be.ResetKeys(profile), "Todas as teclas voltaram ao original"); await LoadMatrix(); }; headerActions.Children.Add(b); }
        if (key == "luz" && (effect.Mode == 20 || effect.Mode == 21 || effect.Mode == 22))
            headerActions.Children.Add(new Border { CornerRadius = new CornerRadius(99), Background = T.B("#145EEAD4"), BorderBrush = T.B("#335EEAD4"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 3, 10, 3), Child = Txt("● sincronizando com o PC", 12, T.Accent2) });
        if (key == "pintar") PaintRender();
    }

    // ------------------------------------------------------------ conexão
    async void RefreshStatus(bool first)
    {
        var s = await Task.Run(() => be.Status());
        bool was = connected; connected = s.Connected;
        dot.Fill = s.Connected ? T.Ok : T.Faint;
        if (s.Connected && !T.Reduce) { if (dot.Effect == null) dot.Effect = new DropShadowEffect { Color = T.C("#4ADE80"), BlurRadius = 10, ShadowDepth = 0, Opacity = .8 }; } else dot.Effect = null;
        stTitle.Text = s.Connected ? "K86 conectado" : "Desconectado";
        stSub.Text = s.Connected ? "Pelo cabo USB" : s.Dongle ? "Só o receptor sem fio" : "Ligue o K86 no cabo USB";
        banner.Visibility = s.Connected ? Visibility.Collapsed : Visibility.Visible;
        bannerTxt.Text = s.Dongle ? "O K86 está conectado só pelo receptor sem fio. Para configurar, ligue o cabo USB (as configurações ficam salvas no teclado, depois pode voltar ao sem fio)."
                                  : "O K86 não foi encontrado. Ligue o teclado no cabo USB e feche o app oficial da Attack Shark.";
        if (s.Connected && (!was || first)) { profile = s.Profile; profileSeg.Select(profile, false); await LoadAll(); }
    }
    async Task LoadAll()
    {
        var e = await Get(() => be.GetEffect()); if (e != null) { effect = e; ShowEffect(); }
        await LoadMatrix();
        var st = await Get(() => be.GetSettings()); if (st != null) ShowSettings(st);
    }
    async Task Run(Action a, string ok) { try { await Task.Run(a); if (ok != null) Toast(ok, false); } catch (Exception ex) { Toast(ex.Message, true); } }
    async Task<TR> Get<TR>(Func<TR> f) where TR : class { try { return await Task.Run(f); } catch (Exception ex) { Toast(ex.Message, true); return null; } }

    // ------------------------------------------------------------ toasts
    public static readonly StringBuilder ToastLog = new StringBuilder();
    void Toast(string msg, bool err)
    {
        ToastLog.AppendLine((err ? "ERR " : "OK ") + msg);
        var tb = Txt(msg, 13.5); tb.MaxWidth = 340;
        var b = new Border { CornerRadius = new CornerRadius(12), Background = T.B("#1D1D24"), BorderBrush = T.Line2, BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 8, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Opacity = .5, Direction = 270 }, RenderTransform = new TranslateTransform(0, 40), Opacity = 0,
            Child = Row(10, new Ellipse { Width = 8, Height = 8, Fill = err ? T.Danger : T.Ok, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) }, tb) };
        toasts.Children.Add(b);
        T.Anim((TranslateTransform)b.RenderTransform, TranslateTransform.YProperty, 0, 300, T.EaseOut); T.Anim(b, OpacityProperty, 1, 300, T.EaseOut);
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(err ? 6000 : 3200) };
        b.MouseEnter += (s, e) => t.Stop(); b.MouseLeave += (s, e) => t.Start(); // pausa enquanto o mouse está em cima
        t.Tick += (s, e) =>
        {
            t.Stop(); T.Anim((TranslateTransform)b.RenderTransform, TranslateTransform.YProperty, 40, 200, T.EaseOut); T.Anim(b, OpacityProperty, 0, 200, T.EaseOut);
            var rm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) }; rm.Tick += (s2, e2) => { rm.Stop(); toasts.Children.Remove(b); }; rm.Start();
        };
        t.Start();
    }

    // ============================================================ ILUMINAÇÃO
    class Mode { public int Id; public string Name; public bool Color; public string[] Opts; public string Sync; }
    static readonly Mode[] Modes = {
        M(1,"Fixo",true), M(2,"Respiração",true), M(4,"Onda",true,"→","←","↓","↑"), M(3,"Neon",false), M(5,"Ondulação",true), M(6,"Gotas",true),
        M(7,"Cobra",true,"Zigue-zague","Espiral"), M(8,"Acende ao tocar",true), M(19,"Apaga ao tocar",true), M(9,"Convergir",true), M(10,"Senoide",true),
        M(11,"Caleidoscópio",true,"Para fora","Para dentro"), M(12,"Linha",true,"→","←"), M(14,"Laser",true), M(15,"Círculo",true,"Anti-horário","Horário"),
        M(16,"Cintilante",true), M(17,"Chuva",true), M(18,"Meteoro",true), M(23,"Trem",true), M(24,"Fogos",true),
        new Mode { Id = 22, Name = "Música", Color = true, Opts = new[] { "Vertical", "Separado", "Cruzado" }, Sync = "music" },
        new Mode { Id = 20, Name = "Música 2", Color = true, Opts = new[] { "Vertical", "Separado", "Cruzado" }, Sync = "music" },
        new Mode { Id = 21, Name = "Cor da tela", Color = false, Opts = new string[0], Sync = "screen" },
        M(13,"Pintura",false,"1","2","3","4","5"), M(0,"Desligado",false) };
    static Mode M(int id, string n, bool c, params string[] o) { return new Mode { Id = id, Name = n, Color = c, Opts = o }; }
    static readonly string[] Palette = { "#FF3355", "#FF8A00", "#FFD400", "#22E06B", "#00D5FF", "#3B6BFF", "#9B5CFF", "#FF4FD8", "#FFFFFF" };

    KbView kbLuz; readonly List<ToggleButton> fxButtons = new List<ToggleButton>(); Slider bright, speed; TextBlock brOut, spOut; Seg optSeg; FrameworkElement optField, speedField, colorCard;
    ToggleButton rainbowSw; Action<int> fxMark; Border syncCard; TextBlock syncTxt; bool showing; DispatcherTimer pushTimer;
    static readonly string[] BrTxt = { "Apagado", "Baixo", "Médio", "Alto", "Máximo" }, SpTxt = { "Bem lento", "Lento", "Médio", "Rápido", "Bem rápido" };

    FrameworkElement BuildLight()
    {
        kbLuz = new KbView(false);
        var wrap = new WrapPanel();
        foreach (var m in Modes)
        {
            var chip = new Border { Height = 6, Width = 64, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = FxChip(m.Id) };
            var name = new TextBlock { Text = m.Name, FontSize = 12.5, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Bottom };
            var g = new Grid(); g.Children.Add(chip); g.Children.Add(name);
            var b = new ToggleButton { Style = S("Fx"), Content = g, Tag = m.Id };
            b.Click += (s, e) => { effect.Mode = (int)b.Tag; effect.Option = 0; ShowEffect(); Push(); };
            fxButtons.Add(b); wrap.Children.Add(b);
        }
        bright = Range(0, 4); speed = Range(0, 4); brOut = Txt(""); spOut = Txt("");
        bright.ValueChanged += (s, e) => { brOut.Text = BrTxt[(int)bright.Value]; Push(); };
        speed.ValueChanged += (s, e) => { spOut.Text = SpTxt[(int)speed.Value]; Push(); };
        optSeg = new Seg(true); optSeg.Changed += i => Push();
        speedField = Field("Velocidade", spOut, speed); optField = Field("Variação", null, optSeg);
        var controls = Card(Stack(18, Field("Brilho", brOut, bright), speedField, optField));
        rainbowSw = Switch(); rainbowSw.Click += (s, e) => Push();
        var sw = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        fxMark = Swatches(sw, c => { effect.Color = c; rainbowSw.IsChecked = false; Push(); });
        colorCard = Card(Stack(0, ToggleRow("Arco-íris", "Todas as cores ao mesmo tempo", rainbowSw), new Border { Height = 1, Background = T.Line }, sw));
        syncTxt = Txt("", 13, T.Muted); syncCard = Card(syncTxt);
        var right = Stack(16, controls, colorCard, syncCard);
        var grid = Cols(1.35, Card(wrap, "Efeito"), 1, right);
        pushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        pushTimer.Tick += async (s, e) =>
        {
            pushTimer.Stop(); if (!connected) return;
            var snap = new EffectState { Mode = effect.Mode, Bright = (int)bright.Value, Speed = (int)speed.Value, Option = Math.Max(0, optSeg.Index), Rainbow = rainbowSw.IsChecked == true, Color = effect.Color };
            effect = snap;
            await Run(() => be.SetEffect(snap), null);
            if (snap.Mode == 13) await SendPaint(false);
        };
        return Stack(16, kbLuz, grid);
    }
    void Push() { if (showing) return; pushTimer.Stop(); pushTimer.Start(); }
    static Brush FxChip(int id)
    {
        Func<string[], Brush> grad = cs => { var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) }; for (int i = 0; i < cs.Length; i++) g.GradientStops.Add(new GradientStop(T.C(cs[i]), i / (double)(cs.Length - 1))); return g; };
        switch (id)
        {
            case 1: return T.Accent;
            case 2: return grad(new[] { "#1E1B4B", "#8B7CFF", "#1E1B4B" });
            case 3: return grad(new[] { "#F43F5E", "#A855F7", "#06B6D4" });
            case 0: return T.B("#2A2A32");
            case 21: return grad(new[] { "#0EA5E9", "#22C55E" });
            case 13: return grad(new[] { "#F472B6", "#FACC15", "#5EEAD4" });
            case 20: case 22: return grad(new[] { "#5EEAD4", "#09090B", "#5EEAD4", "#09090B", "#5EEAD4", "#09090B", "#5EEAD4" });
            default: return grad(new[] { "#F43F5E", "#F59E0B", "#84CC16", "#06B6D4", "#8B5CF6" });
        }
    }
    Action<int> Swatches(WrapPanel panel, Action<int> pick)
    {
        var rings = new List<Ellipse>();
        Action<int> mark = c => { foreach (var r in rings) r.Stroke = (int)r.Tag == (c & 0xFFFFFF) ? Brushes.White : Brushes.Transparent; };
        foreach (var hex in Palette)
        {
            int c = Convert.ToInt32(hex.Substring(1), 16);
            var ring = new Ellipse { Width = 32, Height = 32, StrokeThickness = 2, Tag = c, Stroke = Brushes.Transparent };
            var g = new Grid(); g.Children.Add(ring); g.Children.Add(new Ellipse { Width = 26, Height = 26, Fill = T.B(hex), Stroke = T.Line2, StrokeThickness = 1 });
            var b = new Button { Style = S("Plain"), Content = g, Margin = new Thickness(0, 0, 6, 6), ToolTip = hex };
            b.Click += (s, e) => { mark(c); pick(c); };
            rings.Add(ring); panel.Children.Add(b);
        }
        var cg = new Grid(); cg.Children.Add(new Ellipse { Width = 26, Height = 26, Fill = new LinearGradientBrush(new GradientStopCollection { new GradientStop(T.C("#F43F5E"), 0), new GradientStop(T.C("#F59E0B"), .25), new GradientStop(T.C("#84CC16"), .5), new GradientStop(T.C("#06B6D4"), .75), new GradientStop(T.C("#8B5CF6"), 1) }, 0) });
        cg.Children.Add(new TextBlock { Text = "+", FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var cb = new Button { Style = S("Plain"), Content = cg, Margin = new Thickness(0, 0, 6, 6), ToolTip = "Outra cor", Width = 32, Height = 32 };
        cb.Click += (s, e) =>
        {
            using (var d = new System.Windows.Forms.ColorDialog { FullOpen = true })
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { int c = (d.Color.R << 16) | (d.Color.G << 8) | d.Color.B; mark(-1); pick(c); }
        };
        panel.Children.Add(cb);
        return mark;
    }
    Mode CurMode { get { return Modes.FirstOrDefault(m => m.Id == effect.Mode) ?? Modes[0]; } }
    void ShowEffect()
    {
        showing = true;
        var m = CurMode;
        foreach (var b in fxButtons) b.IsChecked = (int)b.Tag == effect.Mode;
        bright.Value = effect.Bright; speed.Value = effect.Speed; brOut.Text = BrTxt[effect.Bright]; spOut.Text = SpTxt[effect.Speed];
        if (effect.Color == 0xFAFFFA) effect.Color = 0xFFFFFF;
        fxMark(effect.Color); rainbowSw.IsChecked = effect.Rainbow;
        colorCard.Visibility = m.Color ? Visibility.Visible : Visibility.Collapsed;
        speedField.Visibility = new[] { 0, 1, 13, 21 }.Contains(m.Id) ? Visibility.Collapsed : Visibility.Visible;
        optField.Visibility = m.Opts.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (m.Opts.Length > 0) { optSeg.Items(m.Opts); optSeg.Select(Math.Min(effect.Option, m.Opts.Length - 1), false); }
        syncCard.Visibility = m.Sync != null ? Visibility.Visible : Visibility.Collapsed;
        syncTxt.Text = m.Sync == "music" ? "As luzes seguem o som que está tocando no computador. Deixe o K86 Studio aberto enquanto quiser o efeito."
                     : "As luzes seguem a cor média do seu monitor. Deixe o K86 Studio aberto enquanto quiser o efeito.";
        showing = false;
        if (page == "luz") Go("luz");
    }

    // prévia animada no desenho do teclado (desligada com "reduzir animações")
    readonly Dictionary<int, double> sparks = new Dictionary<int, double>(); readonly Random rnd = new Random(); readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    void OnFrame(object sender, EventArgs e)
    {
        if (page != "luz" || !IsActive && WindowState == WindowState.Minimized) return;
        double t = T.Reduce ? 0 : clock.Elapsed.TotalSeconds;
        double sp = .4 + speed.Value * .45, br = bright.Value / 4.0; bool rb = rainbowSw.IsChecked == true; int col = effect.Color; int opt = Math.Max(0, optSeg.Index);
        var lv = be.Sync.Levels; var srgb = be.Sync.ScreenRgb;
        foreach (var k in kbLuz.List)
        {
            double cx = (k.X + k.W / 2) / KbView.W, cy = (k.Y - KbView.TOP + .5) / 6.25; Color c = Colors.Transparent;
            Func<double, double, Color> pick = (a, hs) => rb ? T.Hsl(cx * 360 + hs + t * 40 * sp, 100, 55, a * br) : T.Rgb(col, a * br);
            switch (effect.Mode)
            {
                case 0: break;
                case 1: c = pick(1, 0); break;
                case 2: c = pick(.15 + .85 * (.5 - .5 * Math.Cos(t * sp * 1.6)), 0); break;
                case 3: c = T.Hsl(t * 60 * sp, 100, 55, br); break;
                case 4: case 12: case 23:
                    { double p = opt == 0 ? cx : opt == 1 ? 1 - cx : opt == 2 ? cy : 1 - cy;
                      c = rb ? T.Hsl(p * 300 - t * 120 * sp, 100, 55, br) : T.Rgb(col, br * (.25 + .75 * Math.Max(0, Math.Cos(p * 6 - t * sp * 2)))); break; }
                case 5: case 9: case 11: case 15:
                    { double dd = Math.Sqrt((cx - .5) * (cx - .5) + (cy - .5) * (cy - .5) * .2); double ph = effect.Mode == 9 || (effect.Mode == 11 && opt == 1) ? dd * 14 + t * sp * 3 : dd * 14 - t * sp * 3;
                      c = rb ? T.Hsl(dd * 500 + t * 80 * sp, 100, 55, br) : T.Rgb(col, br * (.2 + .8 * Math.Max(0, Math.Sin(ph)))); break; }
                case 10: { double v = .5 + .5 * Math.Sin(cx * 10 - t * sp * 3 + cy * 2); c = rb ? T.Hsl(cx * 360 + t * 50, 100, 55, br * v) : T.Rgb(col, br * v); break; }
                case 20: case 22:
                    { int ci = Math.Min(20, (int)(cx * 21)); int row = (int)Math.Round(cy * 5); double l = lv.Length > ci ? lv[ci] * 6 : 0;
                      if (5 - row < l) c = rb ? T.Hsl(ci * 17 + 200, 100, 55, br) : T.Rgb(col, br); break; }
                case 21: c = Color.FromArgb((byte)(br * 255), (byte)srgb[0], (byte)srgb[1], (byte)srgb[2]); break;
                case 13: { int pc; if (paint.TryGetValue(k.Idx, out pc)) c = T.Rgb(pc, br); break; }
                default:
                    { double s; sparks.TryGetValue(k.Idx, out s); s *= .9; if (!T.Reduce && rnd.NextDouble() < .012 * sp) s = 1; sparks[k.Idx] = s;
                      double baseV = effect.Mode == 19 ? 1 - s : s; c = rb ? T.Hsl(k.Idx * 47 + t * 30, 100, 55, br * baseV) : T.Rgb(col, br * baseV); break; }
            }
            if (k.LedBrush.Color != c) k.LedBrush.Color = c;
        }
        kbLuz.KnobGlow.Color = effect.Mode == 0 ? Colors.Transparent : rb ? T.Hsl(t * 50, 100, 55, .5 * br) : T.Rgb(col, .5 * br);
    }

    // ============================================================ PINTAR
    KbView kbPaint; int paintCol = 0xFF3355; Seg toolSeg; ToggleButton liveSw; Action<int> paintMark; Dictionary<int, int> paint = new Dictionary<int, int>(); DispatcherTimer paintTimer;
    FrameworkElement BuildPaint()
    {
        LoadPaint();
        kbPaint = new KbView(true);
        kbPaint.KeyHit += i => PaintKey(i, true); kbPaint.KeyEnterPressed += i => { if (toolSeg.Index == 0) PaintKey(i, false); };
        var sw = new WrapPanel(); paintMark = Swatches(sw, c => { paintCol = c; if (toolSeg.Index == 2) toolSeg.Select(0, false); });
        paintMark(paintCol);
        toolSeg = new Seg(false).Items("Pincel", "Balde", "Conta-gotas"); toolSeg.Select(0, false);
        var grad = Btn("Degradê", "Ghost"); grad.Click += (s, e) => { foreach (var k in kbPaint.List) { var c = T.Hsl(k.X / KbView.W * 300 + 200, 100, 55, 1); paint[k.Idx] = (c.R << 16) | (c.G << 8) | c.B; } AfterPaint(); };
        var clear = Btn("Apagar", "Ghost"); clear.Click += (s, e) => { paint.Clear(); AfterPaint(); };
        liveSw = Switch(); liveSw.IsChecked = true;
        var tools = Stack(12, Row(8, toolSeg, grad, clear), ToggleRow("Ao vivo", "Cada pincelada já aparece no teclado", liveSw));
        paintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        paintTimer.Tick += async (s, e) => { paintTimer.Stop(); await SendPaint(true); };
        return Stack(16, kbPaint, Cols(1, Card(sw, "Cor"), 1, Card(tools, "Ferramenta")));
    }
    void PaintKey(int idx, bool click)
    {
        if (idx < 0) return;
        if (toolSeg.Index == 2) { int c; if (paint.TryGetValue(idx, out c)) { paintCol = c; paintMark(c); toolSeg.Select(0, false); } return; }
        if (toolSeg.Index == 1 && click) { foreach (var k in kbPaint.List) paint[k.Idx] = paintCol; }
        else { int cur; if (paint.TryGetValue(idx, out cur) && cur == paintCol) return; paint[idx] = paintCol; }
        AfterPaint();
    }
    void AfterPaint() { SavePaint(); PaintRender(); if (liveSw.IsChecked == true) { paintTimer.Stop(); paintTimer.Start(); } }
    void PaintRender() { foreach (var k in kbPaint.List) { int c; k.LedBrush.Color = paint.TryGetValue(k.Idx, out c) ? T.Rgb(c, .92) : Colors.Transparent; } }
    async Task SendPaint(bool enter)
    {
        if (!connected) return;
        var arr = new int[126];
        foreach (var kv in paint) if (kv.Key >= 0 && kv.Key < 126) arr[kv.Key] = kv.Value;
        int knob; if (paint.TryGetValue(KbView.KnobPress, out knob)) foreach (var i in new[] { KbView.KnobDown, KbView.KnobUp }) if (i >= 0) arr[i] = knob;
        bool enterMode = enter && effect.Mode != 13; int b = Math.Max(1, effect.Bright);
        await Run(() => be.SetPerKey(arr, enterMode, b), null);
        if (enterMode) { effect.Mode = 13; effect.Option = 0; ShowEffect(); }
    }
    void LoadPaint() { try { foreach (var l in File.ReadAllLines(System.IO.Path.Combine(dataDir, "pintura.txt"))) { var p = l.Split('='); paint[int.Parse(p[0])] = Convert.ToInt32(p[1], 16); } } catch { } }
    void SavePaint() { try { File.WriteAllLines(System.IO.Path.Combine(dataDir, "pintura.txt"), paint.Select(kv => kv.Key + "=" + kv.Value.ToString("X6")).ToArray()); } catch { } }

    // ============================================================ TECLAS
    KbView kbKeys; byte[] matrix; int sel = -1; byte[] pending; Seg actSeg; TextBlock insTitle, insNow, pendingTxt; FrameworkElement insBody;
    readonly Dictionary<string, FrameworkElement> actPanels = new Dictionary<string, FrameworkElement>(); Button capKey, capCombo; WrapPanel keyList, macroPick; Seg macroMode;
    string capturing; readonly List<int> capMods = new List<int>(); Button capBtn; int macroPickIdx;
    static readonly string[] Acts = { "key", "combo", "media", "mouse", "macro", "off" };
    FrameworkElement BuildKeys()
    {
        kbKeys = new KbView(true); kbKeys.KeyHit += SelectKey;
        insTitle = new TextBlock { Text = "Clique numa tecla acima", FontSize = 17, FontWeight = FontWeights.SemiBold };
        insNow = Txt("", 12.5, T.Muted); insNow.HorizontalAlignment = HorizontalAlignment.Right; insNow.VerticalAlignment = VerticalAlignment.Bottom;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) }; DockPanel.SetDock(insNow, Dock.Right); head.Children.Add(insNow);
        head.Children.Add(Stack(0, Txt("Tecla escolhida", 12.5, T.Muted), insTitle));
        actSeg = new Seg(false).Items("Outra tecla", "Atalho", "Mídia", "Mouse", "Macro", "Desativar");
        actSeg.Changed += i => ShowAct(Acts[i], true);
        // outra tecla
        capKey = CaptureBtn("Clique aqui e aperte a tecla que ela deve virar"); capKey.Click += (s, e) => StartCapture("key", capKey);
        var search = new TextBox { Style = S("Input"), Margin = new Thickness(0, 10, 0, 0) };
        keyList = new WrapPanel(); var keyScroll = new ScrollViewer { Content = keyList, MaxHeight = 170, Margin = new Thickness(0, 10, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        search.TextChanged += (s, e) => RenderKeyList(search.Text); RenderKeyList("");
        actPanels["key"] = Stack(0, capKey, Labeled(search, "…ou procure pelo nome"), keyScroll);
        capCombo = CaptureBtn("Clique aqui e aperte o atalho (ex.: Ctrl + Shift + Esc)"); capCombo.Click += (s, e) => StartCapture("combo", capCombo);
        actPanels["combo"] = Stack(8, capCombo, Txt("Até 2 modificadores mais uma tecla.", 12.5, T.Muted));
        var media = new WrapPanel(); foreach (var m in Names.Media) { int code = (int)m[1]; media.Children.Add(ChipBtn((string)m[0], media, () => SetPending(new byte[] { 3, 0, (byte)(code & 255), (byte)(code >> 8) }))); }
        actPanels["media"] = media;
        var mouse = new WrapPanel(); foreach (var m in Names.Mouse) { var code = (byte[])m[1]; mouse.Children.Add(ChipBtn((string)m[0], mouse, () => SetPending((byte[])code.Clone()))); }
        actPanels["mouse"] = mouse;
        macroPick = new WrapPanel(); macroMode = new Seg(false).Items("Repetir N vezes", "Liga/desliga", "Enquanto segura"); macroMode.Select(0, false);
        macroMode.Changed += i => { if (actSeg.Index == 4) SetPending(new byte[] { 9, (byte)i, (byte)macroPickIdx, 0 }); };
        actPanels["macro"] = Stack(12, macroPick, macroMode);
        actPanels["off"] = Txt("A tecla não vai fazer nada.", 14, T.Muted);
        var actHost = new Grid(); foreach (var p in actPanels.Values) actHost.Children.Add(p);
        var save = Btn("Salvar no teclado", "Primary"); save.Click += async (s, e) => await SaveKey();
        var restore = Btn("Voltar ao original", "Ghost"); restore.Click += async (s, e) => { if (sel < 0) return; int i = sel; await Run(() => be.SetKey(profile, i, Names.Default(i)), "Tecla voltou ao original"); await LoadMatrix(); SelectKey(i); };
        pendingTxt = Txt("", 12.5, T.Muted); pendingTxt.VerticalAlignment = VerticalAlignment.Center;
        var actions = new DockPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(pendingTxt, Dock.Right); actions.Children.Add(pendingTxt); actions.Children.Add(Row(8, save, restore));
        insBody = Stack(14, actSeg, actHost, actions); insBody.Visibility = Visibility.Collapsed;
        ShowAct("key", false);
        return Stack(16, kbKeys, Card(Stack(0, head, insBody)));
    }
    static FrameworkElement Labeled(TextBox tb, string hint)
    {
        var g = new Grid(); var h = new TextBlock { Text = hint, Foreground = T.Faint, Margin = new Thickness(12, 10, 0, 0), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        tb.TextChanged += (s, e) => h.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        g.Children.Add(tb); g.Children.Add(h); return g;
    }
    Button CaptureBtn(string text)
    {
        var b = new Button { Style = S("Btn"), Height = 64, Content = Txt(text, 14, T.Muted), Background = T.Panel2, HorizontalAlignment = HorizontalAlignment.Stretch };
        b.BorderBrush = T.Line2; return b;
    }
    ToggleButton ChipBtn(string text, Panel group, Action onPick)
    {
        var b = new ToggleButton { Style = S("Chip"), Content = text };
        b.Click += (s, e) => { foreach (var o in group.Children.OfType<ToggleButton>()) o.IsChecked = o == b; onPick(); };
        return b;
    }
    void RenderKeyList(string f)
    {
        keyList.Children.Clear();
        foreach (var kv in Names.AllHid.Where(x => x.Value.IndexOf(f ?? "", StringComparison.OrdinalIgnoreCase) >= 0))
        { int code = kv.Key; keyList.Children.Add(ChipBtn(kv.Value, keyList, () => SetPending(new byte[] { 0, 0, (byte)code, 0 }))); }
    }
    void ShowAct(string a, bool user)
    {
        foreach (var kv in actPanels) kv.Value.Visibility = kv.Key == a ? Visibility.Visible : Visibility.Collapsed;
        if (a == "off" && user) SetPending(new byte[] { 0, 0, 0, 0 });
        if (a == "macro") RenderMacroPick();
    }
    void SetPending(byte[] code) { pending = code; pendingTxt.Text = "Vai virar: " + Names.Describe(code); }
    byte[] Cur(int i) { if (i < 0) return new byte[4]; if (matrix == null) return Names.Default(i); var r = new byte[4]; Array.Copy(matrix, i * 4, r, 0, 4); return r; }
    async Task LoadMatrix()
    {
        var m = await Get(() => be.GetMatrix(profile)); if (m == null) return; matrix = m; RenderKeys();
    }
    void RenderKeys()
    {
        foreach (var k in kbKeys.List)
        {
            if (k.Idx < 0) continue;
            var cur = Cur(k.Idx); bool mod = k.Idx >= 0 && !Names.Same(cur, Names.Default(k.Idx));
            kbKeys.Mark(k.Idx, k.Idx == sel, mod, mod ? Names.Describe(cur) : "");
        }
        kbKeys.Knob.Stroke = sel == KbView.KnobPress ? T.Accent : T.Line2; kbKeys.Knob.StrokeThickness = sel == KbView.KnobPress ? 2 : 1;
    }
    void SelectKey(int i)
    {
        if (i < 0) return;
        sel = i; pending = null; pendingTxt.Text = ""; RenderKeys();
        insTitle.Text = i == KbView.KnobPress ? "Botão giratório (apertar)" : Names.Describe(Names.Default(i));
        insNow.Text = "Agora faz: " + Names.Describe(Cur(i));
        insBody.Visibility = Visibility.Visible;
        var c = Cur(i);
        string a = c[0] == 9 ? "macro" : c[0] == 3 ? "media" : c[0] == 1 ? "mouse" : (c[0] | c[1] | c[2] | c[3]) == 0 ? "off" : (c[1] != 0 || c[3] != 0) ? "combo" : "key";
        actSeg.Select(Array.IndexOf(Acts, a), false); ShowAct(a, false);
    }
    void StartCapture(string kind, Button b)
    {
        if (capturing != null) return; capturing = kind; capBtn = b; capMods.Clear();
        b.BorderBrush = T.Accent; b.Background = T.AccentSoft; b.Content = Txt(kind == "key" ? "Aperte a tecla…" : "Aperte o atalho…", 14);
        Keyboard.Focus(this);
    }
    void EndCapture(string text)
    {
        var b = capBtn; capturing = null; b.BorderBrush = T.Line2; b.Background = T.Panel2;
        b.Content = new Border { CornerRadius = new CornerRadius(7), Background = T.Panel3, BorderBrush = T.Line2, BorderThickness = new Thickness(1, 1, 1, 2), Padding = new Thickness(10, 3, 10, 3), Child = Txt(text, 13, T.Text, FontWeights.Medium) };
    }
    void RenderMacroPick()
    {
        macroPick.Children.Clear(); var ms = Macros();
        for (int i = 0; i < ms.Count; i++)
        {
            int k = i; var b = ChipBtn(ms[i].Name, macroPick, () => { macroPickIdx = k; SetPending(new byte[] { 9, (byte)Math.Max(0, macroMode.Index), (byte)k, 0 }); });
            b.IsEnabled = ms[i].Events.Count > 0; macroPick.Children.Add(b);
        }
    }
    async Task SaveKey()
    {
        if (sel < 0 || pending == null) { Toast("Escolha primeiro o que a tecla deve fazer.", true); return; }
        var code = pending; int idx = sel, prof = profile;
        if (code[0] == 9) { var m = Macros()[code[2]]; var evs = m.Events; int rep = m.Repeat; await Run(() => be.SetMacro(code[2], rep, evs), null); }
        await Run(() => be.SetKey(prof, idx, code), "Tecla salva no teclado");
        await LoadMatrix(); SelectKey(idx);
    }

    // ============================================================ MACROS
    class MacroData { public string Name; public int Repeat = 1; public List<P.MacroEv> Events = new List<P.MacroEv>(); }
    List<MacroData> macroCache;
    List<MacroData> Macros()
    {
        if (macroCache != null) return macroCache;
        macroCache = Enumerable.Range(0, 10).Select(i => new MacroData { Name = "Macro " + (i + 1) }).ToList();
        try
        {
            foreach (var line in File.ReadAllLines(System.IO.Path.Combine(dataDir, "macros2.txt")))
            {
                var p = line.Split('|'); int i = int.Parse(p[0]); var m = macroCache[i]; m.Name = p[1]; m.Repeat = int.Parse(p[2]);
                for (int k = 3; k < p.Length; k++) { var f = p[k].Split(','); if (f.Length == 3) m.Events.Add(new P.MacroEv { Hid = int.Parse(f[0]), Down = f[1] == "1", Delay = int.Parse(f[2]) }); }
            }
        }
        catch { }
        return macroCache;
    }
    void SaveMacros()
    {
        try
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 10; i++)
            {
                var m = Macros()[i]; sb.Append(i).Append('|').Append(m.Name.Replace("|", " ")).Append('|').Append(m.Repeat);
                foreach (var e in m.Events) sb.Append('|').Append(e.Hid).Append(',').Append(e.Down ? 1 : 0).Append(',').Append(e.Delay);
                sb.AppendLine();
            }
            File.WriteAllText(System.IO.Path.Combine(dataDir, "macros2.txt"), sb.ToString());
        }
        catch { }
    }
    int curMacro; bool recording; long lastT; StackPanel slots; TextBox macroName, fixedMs, repeatBox; WrapPanel timeline; ToggleButton fixedSw; Button recBtn; Ellipse recDot; TextBlock recTxt;
    DispatcherTimer blink;
    FrameworkElement BuildMacros()
    {
        slots = new StackPanel();
        macroName = new TextBox { Style = S("Input") }; macroName.LostFocus += (s, e) => { var m = Macros()[curMacro]; m.Name = macroName.Text.Trim() == "" ? "Macro " + (curMacro + 1) : macroName.Text.Trim(); SaveMacros(); RenderSlots(); };
        recDot = new Ellipse { Width = 12, Height = 12, Fill = T.Danger }; recTxt = new TextBlock { Text = "Gravar", Margin = new Thickness(8, 0, 0, 0) };
        recBtn = new Button { Style = S("Btn"), Content = Row(0, recDot, recTxt), Margin = new Thickness(8, 0, 0, 0) }; recBtn.Click += (s, e) => { if (recording) StopRec(); else StartRec(); };
        var clr = Btn("Limpar", "Ghost"); clr.Margin = new Thickness(6, 0, 0, 0); clr.Click += (s, e) => { Macros()[curMacro].Events.Clear(); SaveMacros(); RenderMacro(); RenderSlots(); };
        var top = new DockPanel(); var tr = Row(0, recBtn, clr); DockPanel.SetDock(tr, Dock.Right); top.Children.Add(tr); top.Children.Add(macroName);
        timeline = new WrapPanel();
        var tlBorder = new Border { MinHeight = 130, CornerRadius = new CornerRadius(12), Background = T.Panel2, BorderBrush = T.Line, BorderThickness = new Thickness(1), Padding = new Thickness(12), Child = timeline };
        fixedSw = Switch(); fixedMs = new TextBox { Style = S("Input"), Width = 70, Text = "20" }; repeatBox = new TextBox { Style = S("Input"), Width = 70, Text = "1" };
        repeatBox.LostFocus += (s, e) => { int r; Macros()[curMacro].Repeat = int.TryParse(repeatBox.Text, out r) ? Math.Max(1, Math.Min(999, r)) : 1; repeatBox.Text = Macros()[curMacro].Repeat.ToString(); SaveMacros(); };
        var opts = new DockPanel(); var rep = Row(8, Txt("Repetir", 14, T.Muted), repeatBox, Txt("vez(es)", 14, T.Muted)); DockPanel.SetDock(rep, Dock.Right); opts.Children.Add(rep);
        opts.Children.Add(Row(8, fixedSw, Txt("Atraso fixo de", 14), fixedMs, Txt("ms", 14, T.Muted)));
        var send = Btn("Enviar ao teclado", "Primary");
        send.Click += async (s, e) => { var m = Macros()[curMacro]; if (m.Events.Count == 0) { Toast("Essa macro está vazia.", true); return; } int idx = curMacro; await Run(() => be.SetMacro(idx, m.Repeat, m.Events), "Macro enviada ao teclado"); };
        var hint = Txt("Depois, em Teclas, escolha \"Macro\" para colocar numa tecla.", 12.5, T.Muted);
        var editor = Card(Stack(16, top, tlBorder, opts, Row(12, send, hint)));
        blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; blink.Tick += (s, e) => recDot.Opacity = recDot.Opacity > .5 ? .25 : 1;
        RenderSlots(); RenderMacro();
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var left = Card(slots, "Macros"); g.Children.Add(left); Grid.SetColumn(editor, 2); g.Children.Add(editor);
        return g;
    }
    void RenderSlots()
    {
        slots.Children.Clear(); var ms = Macros();
        for (int i = 0; i < ms.Count; i++)
        {
            int k = i; bool on = i == curMacro;
            var dp = new DockPanel(); var cnt = Txt(ms[i].Events.Count > 0 ? (ms[i].Events.Count + 1) / 2 + " teclas" : "vazia", 11.5, T.Faint); DockPanel.SetDock(cnt, Dock.Right); cnt.VerticalAlignment = VerticalAlignment.Center;
            dp.Children.Add(cnt); dp.Children.Add(new TextBlock { Text = ms[i].Name, VerticalAlignment = VerticalAlignment.Center, Foreground = on ? T.Text : T.Muted });
            var b = new Button { Style = S("Plain"), Height = 40, Padding = new Thickness(12, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = dp, Margin = new Thickness(0, 0, 0, 4),
                Background = on ? T.Panel3 : Brushes.Transparent, BorderBrush = on ? T.Line : Brushes.Transparent, BorderThickness = new Thickness(1) };
            b.Click += (s, e) => { if (recording) StopRec(); curMacro = k; RenderSlots(); RenderMacro(); };
            slots.Children.Add(b);
        }
    }
    void RenderMacro()
    {
        var m = Macros()[curMacro]; macroName.Text = m.Name; repeatBox.Text = m.Repeat.ToString();
        timeline.Children.Clear();
        if (m.Events.Count == 0) timeline.Children.Add(Txt(recording ? "Gravando… digite agora." : "Clique em Gravar e digite a sequência no teclado.", 13, T.Muted));
        foreach (var e in m.Events) AddEvChip(e, false);
    }
    void AddEvChip(P.MacroEv e, bool anim)
    {
        if (timeline.Children.Count == 1 && timeline.Children[0] is TextBlock) timeline.Children.Clear();
        var name = new TextBlock { Text = Names.Hid(e.Hid), FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 6, 0) };
        var del = new TextBlock { Text = e.Delay + "ms", Foreground = T.Faint };
        var b = new Border { Height = 28, CornerRadius = new CornerRadius(8), Background = T.Panel3, BorderBrush = T.Line, BorderThickness = new Thickness(1), Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(0, 0, 6, 6),
            Opacity = e.Down ? 1 : .7, Child = Row(0, Txt(e.Down ? "↓" : "↑", 12), name, del), Tag = del, RenderTransform = new TranslateTransform() };
        foreach (var t in ((StackPanel)b.Child).Children.OfType<TextBlock>()) t.FontSize = 12;
        timeline.Children.Add(b);
        if (anim) { double target = b.Opacity; b.Opacity = 0; ((TranslateTransform)b.RenderTransform).Y = 4; T.Anim(b, OpacityProperty, target, 180, T.EaseOut); T.Anim((TranslateTransform)b.RenderTransform, TranslateTransform.YProperty, 0, 180, T.EaseOut); }
    }
    void StartRec()
    {
        var m = Macros()[curMacro]; m.Events.Clear(); recording = true; lastT = Environment.TickCount; recTxt.Text = "Parar"; RenderMacro();
        if (!T.Reduce) blink.Start(); Keyboard.Focus(this);
    }
    void StopRec()
    {
        recording = false; blink.Stop(); recDot.Opacity = 1; recTxt.Text = "Gravar";
        var ev = Macros()[curMacro].Events; if (ev.Count > 0) ev[ev.Count - 1].Delay = FixedDelay() ?? 10;
        SaveMacros(); RenderMacro(); RenderSlots();
    }
    int? FixedDelay() { int v; if (fixedSw.IsChecked == true && int.TryParse(fixedMs.Text, out v)) return Math.Max(1, Math.Min(5000, v)); return null; }
    void RecKey(KeyEventArgs e, bool down)
    {
        e.Handled = true; if (down && e.IsRepeat) return;
        int h = Names.KeyToHid(e.Key == Key.System ? e.SystemKey : e.Key); if (h == 0) return;
        var ev = Macros()[curMacro].Events; long now = Environment.TickCount;
        if (ev.Count > 0) { ev[ev.Count - 1].Delay = FixedDelay() ?? (int)Math.Max(1, Math.Min(65535, now - lastT)); var last = timeline.Children[timeline.Children.Count - 1] as Border; if (last != null) ((TextBlock)last.Tag).Text = ev[ev.Count - 1].Delay + "ms"; }
        lastT = now;
        if (ev.Count >= 120) { StopRec(); Toast("Chegou no limite de tamanho da macro.", true); return; }
        var item = new P.MacroEv { Hid = h, Down = down, Delay = 10 }; ev.Add(item); AddEvChip(item, true);
    }

    void HandleKeyDown(object s, KeyEventArgs e)
    {
        if (recording) { RecKey(e, true); return; }
        if (capturing == null) return;
        e.Handled = true; int h = Names.KeyToHid(e.Key == Key.System ? e.SystemKey : e.Key); if (h == 0) return;
        if (capturing == "key") { SetPending(new byte[] { 0, 0, (byte)h, 0 }); EndCapture(Names.Hid(h)); return; }
        if (h >= 224 && h <= 231) { if (!capMods.Contains(h)) capMods.Add(h); capBtn.Content = Txt(string.Join(" + ", capMods.Select(Names.Hid).ToArray()) + " + …", 14); return; }
        var mods = capMods.Take(2).ToList();
        var code = mods.Count == 0 ? new byte[] { 0, 0, (byte)h, 0 } : mods.Count == 1 ? new byte[] { 0, 0, (byte)mods[0], (byte)h } : new byte[] { 0, (byte)mods[0], (byte)mods[1], (byte)h };
        SetPending(code); EndCapture(Names.Describe(code));
    }
    void HandleKeyUp(object s, KeyEventArgs e) { if (recording) RecKey(e, false); else if (capturing != null) e.Handled = true; }

    // ============================================================ TELA
    Image scrImg; Seg fitSeg, slotSeg; Slider gifSpeed; TextBlock gifOut, imgInfo, sendTxt; Button sendScr; Rectangle sendFill; FrameworkElement gifField; Border drop;
    List<D.Bitmap> srcFrames = new List<D.Bitmap>(); List<byte[]> frames565 = new List<byte[]>(); List<BitmapSource> previews = new List<BitmapSource>(); DispatcherTimer anim; int animI;
    FrameworkElement BuildScreen()
    {
        scrImg = new Image { Width = 480, Height = 270, Stretch = Stretch.Fill }; RenderOptions.SetBitmapScalingMode(scrImg, BitmapScalingMode.HighQuality);
        var device = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = T.Line2, BorderThickness = new Thickness(1), Background = new LinearGradientBrush(T.C("#1A1A20"), T.C("#111115"), 90),
            Child = new Border { CornerRadius = new CornerRadius(8), BorderBrush = T.B("#26262E"), BorderThickness = new Thickness(3), Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, Child = scrImg, ClipToBounds = true } };
        drop = new Border { CornerRadius = new CornerRadius(16), BorderBrush = T.Line2, BorderThickness = new Thickness(1.5), Padding = new Thickness(22), Background = Brushes.Transparent, AllowDrop = true, Cursor = Cursors.Hand,
            Child = Stack(4, new TextBlock { Text = "Arraste uma foto ou GIF aqui", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = "ou clique para escolher · 240 × 135 pixels", Foreground = T.Muted, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center }) };
        drop.DragEnter += (s, e) => { drop.BorderBrush = T.Accent; drop.Background = T.AccentSoft; };
        drop.DragLeave += (s, e) => { drop.BorderBrush = T.Line2; drop.Background = Brushes.Transparent; };
        drop.Drop += (s, e) => { drop.BorderBrush = T.Line2; drop.Background = Brushes.Transparent; var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null && f.Length > 0) LoadImage(f[0]); };
        drop.MouseLeftButtonUp += (s, e) => { var d = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos|*.*" }; if (d.ShowDialog(this) == true) LoadImage(d.FileName); };
        fitSeg = new Seg(true).Items("Preencher", "Inteira"); fitSeg.Select(0, false); fitSeg.Changed += i => Compose();
        slotSeg = new Seg(true).Items("1", "2", "3", "4", "5"); slotSeg.Select(0, false);
        gifSpeed = Range(2, 60); gifSpeed.Value = 10; gifOut = Txt("100 ms/quadro");
        gifSpeed.ValueChanged += (s, e) => { gifOut.Text = (gifSpeed.Value * 10) + " ms/quadro"; if (anim != null) anim.Interval = TimeSpan.FromMilliseconds(gifSpeed.Value * 10); };
        gifField = Field("Velocidade do GIF", gifOut, gifSpeed); gifField.Visibility = Visibility.Collapsed;
        sendFill = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, RadiusX = 9, RadiusY = 9 };
        sendTxt = new TextBlock { Text = "Enviar para a tela", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var sg = new Grid(); sg.Children.Add(sendFill); sg.Children.Add(sendTxt);
        sendScr = new Button { Style = S("Primary"), Content = sg, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, IsEnabled = false };
        sendScr.Click += async (s, e) => await SendScreen();
        imgInfo = Txt("Nenhuma imagem escolhida.", 12.5, T.Muted);
        var settings = Card(Stack(16, Field("Enquadramento", null, fitSeg), Field("Posição na tela", null, slotSeg), gifField, sendScr, imgInfo));
        var clockB = Btn("Acertar o relógio com o do PC"); clockB.HorizontalAlignment = HorizontalAlignment.Stretch; clockB.Click += async (s, e) => await Run(() => be.Clock(), "Relógio acertado");
        var erase = new HoldButton("Segure para apagar todas as imagens"); erase.Confirmed += async () => await Run(() => be.EraseScreen(), "Apagando as imagens… espere cerca de 1 minuto.");
        var tools = Card(Stack(10, clockB, erase));
        return Cols(1.3, Stack(16, device, drop), 1, Stack(16, settings, tools));
    }
    void LoadImage(string path)
    {
        try
        {
            foreach (var f in srcFrames) f.Dispose(); srcFrames.Clear();
            var img = D.Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
            int n = 1; D.Imaging.FrameDimension dim = null;
            try { dim = new D.Imaging.FrameDimension(img.FrameDimensionsList[0]); n = img.GetFrameCount(dim); } catch { }
            int step = n > 60 ? (int)Math.Ceiling(n / 60.0) : 1;
            for (int i = 0; i < n; i += step) { if (n > 1) img.SelectActiveFrame(dim, i); srcFrames.Add(new D.Bitmap(img)); }
            if (n > 1) { try { var prop = img.GetPropertyItem(0x5100); int d = BitConverter.ToInt32(prop.Value, 0) * 10 * step; if (d > 0) gifSpeed.Value = Math.Max(2, Math.Min(60, d / 10)); } catch { } }
            img.Dispose();
            imgInfo.Text = System.IO.Path.GetFileName(path) + (srcFrames.Count > 1 ? " · GIF com " + srcFrames.Count + " quadros" : "");
            gifField.Visibility = srcFrames.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            Compose(); sendScr.IsEnabled = true;
        }
        catch { Toast("Não deu para abrir essa imagem.", true); }
    }
    void Compose()
    {
        if (srcFrames.Count == 0) return;
        bool fill = fitSeg.Index == 0; frames565.Clear(); previews.Clear();
        foreach (var src in srcFrames)
        {
            using (var bmp = new D.Bitmap(P.SW, P.SH, D.Imaging.PixelFormat.Format32bppRgb))
            {
                using (var g = D.Graphics.FromImage(bmp))
                {
                    g.Clear(D.Color.Black); g.InterpolationMode = D.Drawing2D.InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = D.Drawing2D.PixelOffsetMode.HighQuality;
                    double sx = (double)P.SW / src.Width, sy = (double)P.SH / src.Height, sc = fill ? Math.Max(sx, sy) : Math.Min(sx, sy);
                    int w = (int)Math.Round(src.Width * sc), h = (int)Math.Round(src.Height * sc);
                    g.DrawImage(src, (P.SW - w) / 2, (P.SH - h) / 2, w, h);
                }
                var data = bmp.LockBits(new D.Rectangle(0, 0, P.SW, P.SH), D.Imaging.ImageLockMode.ReadOnly, D.Imaging.PixelFormat.Format32bppRgb);
                var px = new byte[data.Stride * P.SH]; Marshal.Copy(data.Scan0, px, 0, px.Length); int stride = data.Stride; bmp.UnlockBits(data);
                var out565 = new byte[P.SW * P.SH * 2]; int n = 0;
                for (int x = 0; x < P.SW; x++) for (int y = 0; y < P.SH; y++)
                    {
                        int o = y * stride + x * 4; int v = ((px[o + 2] >> 3) << 11) | ((px[o + 1] >> 2) << 5) | (px[o] >> 3);
                        out565[n++] = (byte)(v >> 8); out565[n++] = (byte)(v & 255);
                    }
                frames565.Add(out565);
                var bs = BitmapSource.Create(P.SW, P.SH, 96, 96, PixelFormats.Bgr32, null, px, stride); bs.Freeze(); previews.Add(bs);
            }
        }
        if (anim != null) anim.Stop(); animI = 0; ShowFrame();
        if (previews.Count > 1 && !T.Reduce) { anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(gifSpeed.Value * 10) }; anim.Tick += (s, e) => ShowFrame(); anim.Start(); }
    }
    void ShowFrame() { if (previews.Count == 0) return; var f = previews[animI % previews.Count]; scrImg.Source = f; kbLuz.Screen.Source = f; kbPaint.Screen.Source = f; kbKeys.Screen.Source = f; animI++; }
    async Task SendScreen()
    {
        if (frames565.Count == 0) return;
        sendScr.IsEnabled = false; sendTxt.Text = "Preparando…";
        var fr = frames565.ToList(); int slot = slotSeg.Index, delay = (int)gifSpeed.Value;
        Action<int> prog = p => Dispatcher.BeginInvoke(new Action(() => { sendTxt.Text = p >= 100 ? "Pronto!" : "Enviando… " + p + "%"; T.Anim(sendFill, WidthProperty, sendScr.ActualWidth * p / 100.0, 200, T.Linear); }));
        await Run(() => be.SendScreen(fr, slot, delay, prog), "Imagem enviada para a posição " + (slot + 1));
        await Task.Delay(600); sendFill.BeginAnimation(WidthProperty, null); sendFill.Width = 0; sendTxt.Text = "Enviar para a tela"; sendScr.IsEnabled = true;
    }

    // ============================================================ AJUSTES
    Slider sleepR, deepR; TextBlock slOut, dpOut; ToggleButton winSw, wasdSw; TextBox debounceBox;
    FrameworkElement BuildSettings()
    {
        sleepR = Range(1, 60); deepR = Range(10, 60); slOut = Txt(""); dpOut = Txt("");
        sleepR.ValueChanged += (s, e) => slOut.Text = Min((int)sleepR.Value); deepR.ValueChanged += (s, e) => dpOut.Text = Min((int)deepR.Value);
        sleepR.Value = 3; deepR.Value = 27;
        var energy = Card(Stack(18, Field("Apagar as luzes depois de", slOut, sleepR), Field("Dormir (sem fio) depois de", dpOut, deepR)), "Energia");
        winSw = Switch(); wasdSw = Switch(); debounceBox = new TextBox { Style = S("Input"), Width = 64, Text = "1" };
        var kb = Card(Stack(0, ToggleRow("Travar a tecla Windows", "Evita sair do jogo sem querer", winSw), new Border { Height = 1, Background = T.Line },
            ToggleRow("Trocar WASD com as setas", null, wasdSw), new Border { Height = 1, Background = T.Line },
            ToggleRow("Debounce (ms)", "Aumente se alguma tecla repetir sozinha", debounceBox)), "Teclado");
        var save = Btn("Salvar ajustes", "Primary");
        save.Click += async (s, e) =>
        {
            int db; int.TryParse(debounceBox.Text, out db);
            var st = new SettingsState { Sleep = (int)sleepR.Value * 60, Deep = (int)deepR.Value * 60, Debounce = Math.Max(1, Math.Min(10, db)), WinLock = winSw.IsChecked == true, Wasd = wasdSw.IsChecked == true };
            await Run(() => be.SetSettings(st), "Ajustes salvos no teclado");
        };
        var reset = new HoldButton("Segure para restaurar a fábrica");
        reset.Confirmed += async () => { await Run(() => be.Reset(), "Teclado restaurado para a fábrica"); await Task.Delay(1500); RefreshStatus(true); };
        var bottom = new DockPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(reset, Dock.Right); bottom.Children.Add(reset); bottom.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { save } });
        return Stack(0, Cols(1, energy, 1, kb), bottom);
    }
    static string Min(int v) { return v + (v == 1 ? " minuto" : " minutos"); }
    void ShowSettings(SettingsState s)
    {
        sleepR.Value = Math.Max(1, Math.Min(60, Math.Round(s.Sleep / 60.0))); deepR.Value = Math.Max(10, Math.Min(60, Math.Round(s.Deep / 60.0)));
        debounceBox.Text = Math.Max(1, s.Debounce).ToString(); winSw.IsChecked = s.WinLock; wasdSw.IsChecked = s.Wasd;
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--synctest")
        {
            var a = new Backend { DryRun = true }; var sb = new StringBuilder();
            a.Sync.Mode = "music"; Thread.Sleep(2500);
            sb.AppendLine("music err='" + a.Sync.LastError + "' levels=" + string.Join(" ", a.Sync.Levels.Select(f => f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
            a.Sync.Mode = "screen"; Thread.Sleep(1500);
            sb.AppendLine("screen err='" + a.Sync.LastError + "' rgb=" + string.Join(",", a.Sync.ScreenRgb));
            a.Sync.Mode = "off"; Thread.Sleep(300); File.WriteAllText(args[1], sb.ToString()); return;
        }
        if (args.Length == 2 && args[0] == "--read") // diagnóstico, só leitura
        {
            var b = new Backend(); var sb = new StringBuilder();
            try
            {
                var st = b.GetSettings(); var ef = b.GetEffect();
                sb.AppendLine("sleep=" + st.Sleep + " deep=" + st.Deep + " debounce=" + st.Debounce + " winLock=" + st.WinLock + " wasd=" + st.Wasd);
                sb.AppendLine("effect mode=" + ef.Mode + " bright=" + ef.Bright + " speed=" + ef.Speed + " opt=" + ef.Option + " rainbow=" + ef.Rainbow + " color=" + ef.Color.ToString("X6"));
            }
            catch (Exception ex) { sb.AppendLine("error: " + ex.Message); }
            b.Sync.Mode = "off"; File.WriteAllText(args[1], sb.ToString()); return;
        }
        bool fresh; var mutex = new Mutex(true, "K86Studio-single", out fresh);
        if (!fresh) { MessageBox.Show("O K86 Studio já está aberto.", "K86 Studio"); return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(T.Styles());
        app.DispatcherUnhandledException += (s, e) => { MainWindow.ToastLog.AppendLine("UNHANDLED " + e.Exception); if (args.Length == 0) MessageBox.Show("Erro inesperado: " + e.Exception.Message, "K86 Studio"); e.Handled = true; };
        var w = new MainWindow();
        if (args.Length == 2 && args[0] == "--shot") // gera prints de todas as páginas (para conferência)
        {
            w.Loaded += async (s, e) =>
            {
                await Task.Delay(2500);
                foreach (var pg in new[] { "luz", "pintar", "teclas", "macros", "tela", "ajustes" })
                {
                    typeof(MainWindow).GetMethod("Go", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(w, new object[] { pg });
                    await Task.Delay(900);
                    var rt = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); rt.Render((Visual)w.Content);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rt));
                    using (var fs = File.Create(System.IO.Path.Combine(args[1], "wpf_" + pg + ".png"))) enc.Save(fs);
                }
                var f = typeof(MainWindow).GetField("winSw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var sw = (ToggleButton)f.GetValue(w); var th = sw.Template.FindName("th", sw) as Ellipse;
                File.WriteAllText(System.IO.Path.Combine(args[1], "wpf_state.txt"), MainWindow.ToastLog + "semIdx=" + string.Join(",", ((KbView)typeof(MainWindow).GetField("kbKeys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(w)).List.Where(k => k.Idx < 0).Select(k => k.Label.Text).ToArray()) + " winSw.IsChecked=" + sw.IsChecked + " X=" + (th != null && th.RenderTransform is TranslateTransform ? ((TranslateTransform)th.RenderTransform).X.ToString() : "?"));
                w.Close();
            };
        }
        app.Run(w);
        GC.KeepAlive(mutex);
    }
}
