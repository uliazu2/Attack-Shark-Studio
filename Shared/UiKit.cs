// Componentes visuais compartilhados (K86 Studio e X11 Studio): tema, animações e controles.
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

// ======================================================================= tema e animação
static class T
{
    public static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    public static SolidColorBrush B(string hex) { var b = new SolidColorBrush(C(hex)); b.Freeze(); return b; }
    public static readonly SolidColorBrush Bg = B("#09090B"), Panel = B("#111114"), Panel2 = B("#16161B"), Panel3 = B("#1C1C23"),
        Line = B("#12FFFFFF"), Line2 = B("#1FFFFFFF"), Text = B("#EDEDF0"), Muted = B("#8B8B97"), Faint = B("#5D5D68"),
        Accent = B("#8B7CFF"), AccentSoft = B("#298B7CFF"), Accent2 = B("#5EEAD4"), Danger = B("#FF5D6C"), Ok = B("#4ADE80");
    public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");
    public static readonly KeySpline EaseOut = new KeySpline(0.23, 1, 0.32, 1);     // entrar/sair
    public static readonly KeySpline InOut = new KeySpline(0.77, 0, 0.175, 1);      // mover na tela
    public static readonly KeySpline Linear = new KeySpline(0, 0, 1, 1);
    public static bool Reduce { get { return !SystemParameters.ClientAreaAnimation; } }
    public static void Anim(IAnimatable o, DependencyProperty p, double to, int ms, KeySpline s)
    {
        if (Reduce && p != UIElement.OpacityProperty) ms = 0;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), s));
        o.BeginAnimation(p, a, HandoffBehavior.SnapshotAndReplace); // transições interrompíveis
    }
    public static Color Hsl(double h, double s, double l, double a)
    {
        h = ((h % 360) + 360) % 360 / 360; s /= 100; l /= 100;
        Func<double, double> f = n => { double k = (n + h * 12) % 12; return l - s * Math.Min(l, 1 - l) * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)); };
        return Color.FromArgb((byte)(Math.Max(0, Math.Min(1, a)) * 255), (byte)(f(0) * 255), (byte)(f(8) * 255), (byte)(f(4) * 255));
    }
    public static Color Rgb(int c, double a) { return Color.FromArgb((byte)(Math.Max(0, Math.Min(1, a)) * 255), (byte)(c >> 16), (byte)(c >> 8), (byte)c); }
    public static string Hex(int c) { return "#" + (c & 0xFFFFFF).ToString("X6"); }

    static string Press(string target)
    {
        return "<Trigger Property='IsPressed' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard>" +
            "<DoubleAnimationUsingKeyFrames Storyboard.TargetName='" + target + "' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleX)'><SplineDoubleKeyFrame KeyTime='0:0:0.1' Value='0.97' KeySpline='0.23,1 0.32,1'/></DoubleAnimationUsingKeyFrames>" +
            "<DoubleAnimationUsingKeyFrames Storyboard.TargetName='" + target + "' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleY)'><SplineDoubleKeyFrame KeyTime='0:0:0.1' Value='0.97' KeySpline='0.23,1 0.32,1'/></DoubleAnimationUsingKeyFrames>" +
            "</Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard>" +
            "<DoubleAnimationUsingKeyFrames Storyboard.TargetName='" + target + "' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleX)'><SplineDoubleKeyFrame KeyTime='0:0:0.16' Value='1' KeySpline='0.23,1 0.32,1'/></DoubleAnimationUsingKeyFrames>" +
            "<DoubleAnimationUsingKeyFrames Storyboard.TargetName='" + target + "' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.ScaleY)'><SplineDoubleKeyFrame KeyTime='0:0:0.16' Value='1' KeySpline='0.23,1 0.32,1'/></DoubleAnimationUsingKeyFrames>" +
            "</Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>";
    }
    static string Hover(string target)
    {
        return "<Trigger Property='IsMouseOver' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='" + target + "' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.15'/></Storyboard></BeginStoryboard></Trigger.EnterActions>" +
               "<Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='" + target + "' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.15'/></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>";
    }
    static string BtnTemplate(string type, int radius, string extraTriggers)
    {
        return "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='" + type + "'>" +
            "<Grid x:Name='root' RenderTransformOrigin='0.5,0.5'><Grid.RenderTransform><ScaleTransform/></Grid.RenderTransform>" +
            "<Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='" + radius + "'/>" +
            "<Border x:Name='hv' Background='#0DFFFFFF' CornerRadius='" + radius + "' Opacity='0'/>" +
            "<ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Grid>" +
            "<ControlTemplate.Triggers>" + Hover("hv") + Press("root") + extraTriggers +
            "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='root' Property='Opacity' Value='0.45'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>";
    }
    public static ResourceDictionary Styles()
    {
        string common = "<Setter Property='Foreground' Value='#EDEDF0'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                        "<Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/>";
        string checkedChip = "<Trigger Property='IsChecked' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#8B7CFF'/><Setter TargetName='bd' Property='Background' Value='#298B7CFF'/></Trigger>";
        string checkedFx = "<Trigger Property='IsChecked' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#8B7CFF'/><Setter TargetName='bd' Property='Background'><Setter.Value><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#248B7CFF' Offset='0'/><GradientStop Color='#0A8B7CFF' Offset='1'/></LinearGradientBrush></Setter.Value></Setter></Trigger>";
        string xaml = "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
            "<Style x:Key='Btn' TargetType='Button'>" + common + "<Setter Property='Background' Value='#1C1C23'/><Setter Property='BorderBrush' Value='#1FFFFFFF'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Height' Value='36'/><Setter Property='Padding' Value='14,0'/><Setter Property='FontWeight' Value='Medium'/>" + BtnTemplate("Button", 10, "") + "</Style>" +
            "<Style x:Key='Primary' TargetType='Button' BasedOn='{StaticResource Btn}'><Setter Property='Background' Value='#8B7CFF'/><Setter Property='BorderBrush' Value='#8B7CFF'/><Setter Property='Foreground' Value='#0B0820'/></Style>" +
            "<Style x:Key='Ghost' TargetType='Button' BasedOn='{StaticResource Btn}'><Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/></Style>" +
            "<Style x:Key='Danger' TargetType='Button' BasedOn='{StaticResource Btn}'><Setter Property='Foreground' Value='#FF5D6C'/></Style>" +
            "<Style x:Key='Plain' TargetType='Button'>" + common + "<Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='0'/>" + BtnTemplate("Button", 10, "") + "</Style>" +
            "<Style x:Key='Chip' TargetType='ToggleButton'>" + common + "<Setter Property='Background' Value='#16161B'/><Setter Property='BorderBrush' Value='#12FFFFFF'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Height' Value='30'/><Setter Property='Padding' Value='11,0'/><Setter Property='FontSize' Value='12.5'/><Setter Property='Margin' Value='0,0,6,6'/>" + BtnTemplate("ToggleButton", 9, checkedChip) + "</Style>" +
            "<Style x:Key='Fx' TargetType='ToggleButton'>" + common + "<Setter Property='Background' Value='#16161B'/><Setter Property='BorderBrush' Value='#12FFFFFF'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Height' Value='62'/><Setter Property='Width' Value='126'/><Setter Property='Padding' Value='11,10'/><Setter Property='Margin' Value='0,0,8,8'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='VerticalContentAlignment' Value='Stretch'/>" + BtnTemplate("ToggleButton", 12, checkedFx) + "</Style>" +
            "<Style x:Key='Switch' TargetType='ToggleButton'><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'>" +
                "<Grid Width='38' Height='22' Background='Transparent'><Border x:Name='tr' CornerRadius='11' Background='#2A2A32'/>" +
                "<Ellipse x:Name='th' Width='16' Height='16' Fill='White' HorizontalAlignment='Left' Margin='3,0,0,0'><Ellipse.RenderTransform><TranslateTransform/></Ellipse.RenderTransform></Ellipse></Grid>" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
            "<Style x:Key='FillRB' TargetType='RepeatButton'><Setter Property='Focusable' Value='False'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Height='6' CornerRadius='3' Background='#8B7CFF'/></ControlTemplate></Setter.Value></Setter></Style>" +
            "<Style x:Key='EmptyRB' TargetType='RepeatButton'><Setter Property='Focusable' Value='False'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter></Style>" +
            "<Style x:Key='Range' TargetType='Slider'><Setter Property='IsSnapToTickEnabled' Value='True'/><Setter Property='TickFrequency' Value='1'/><Setter Property='IsMoveToPointEnabled' Value='True'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'><Grid Height='22' Background='Transparent'><Border Height='6' CornerRadius='3' Background='#2A2A32' VerticalAlignment='Center'/>" +
                "<Track x:Name='PART_Track'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Style='{StaticResource FillRB}'/></Track.DecreaseRepeatButton>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Style='{StaticResource EmptyRB}'/></Track.IncreaseRepeatButton>" +
                "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Width='18' Height='18' Fill='White'><Ellipse.Effect><DropShadowEffect BlurRadius='6' ShadowDepth='1' Opacity='0.5'/></Ellipse.Effect></Ellipse></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid>" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
            "<Style x:Key='Input' TargetType='TextBox'><Setter Property='Foreground' Value='#EDEDF0'/><Setter Property='CaretBrush' Value='#EDEDF0'/><Setter Property='Height' Value='36'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>" +
                "<Border x:Name='b' CornerRadius='10' Background='#16161B' BorderBrush='#1FFFFFFF' BorderThickness='1' Padding='10,0'><ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#8B7CFF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>" +
            "<Style TargetType='ScrollBar'><Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>" +
                "<Track x:Name='PART_Track' IsDirectionReversed='True'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='4' Background='#26262E' Margin='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track>" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
            "</ResourceDictionary>";
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }
}

// ======================================================================= pequenos componentes
class Seg : Border
{
    readonly Grid grid = new Grid(); readonly Border thumb; readonly List<Button> items = new List<Button>();
    public int Index = -1; public event Action<int> Changed = delegate { };
    public Seg(bool stretch)
    {
        Background = T.Panel2; BorderBrush = T.Line; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(11); Padding = new Thickness(3);
        HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        thumb = new Border { Background = T.Panel3, BorderBrush = T.Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = new TranslateTransform() };
        Child = grid; grid.Children.Add(thumb);
        SizeChanged += (s, e) => Place(false);
        IsVisibleChanged += (s, e) => { if (IsVisible) Dispatcher.BeginInvoke(new Action(() => Place(false)), DispatcherPriority.Loaded); };
    }
    public Seg Items(params string[] names)
    {
        grid.Children.Clear(); grid.ColumnDefinitions.Clear(); items.Clear(); grid.Children.Add(thumb);
        for (int i = 0; i < names.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = HorizontalAlignment == HorizontalAlignment.Stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var b = new Button { Content = names[i], Style = (Style)Application.Current.Resources["Plain"], Height = 30, Padding = new Thickness(13, 0, 13, 0), Foreground = T.Muted, FontWeight = FontWeights.Medium };
            int k = i; b.Click += (s, e) => Select(k, true);
            Grid.SetColumn(b, i); grid.Children.Add(b); items.Add(b);
        }
        Grid.SetColumnSpan(thumb, Math.Max(1, names.Length));
        Index = -1; return this;
    }
    public void Select(int i, bool notify)
    {
        if (i < 0 || i >= items.Count) return;
        bool changed = i != Index; Index = i;
        for (int k = 0; k < items.Count; k++) items[k].Foreground = k == i ? T.Text : T.Muted;
        Place(true);
        if (notify && changed) Changed(i);
    }
    void Place(bool anim)
    {
        if (Index < 0 || Index >= items.Count || items[Index].ActualWidth == 0) return;
        var b = items[Index]; var p = b.TranslatePoint(new Point(0, 0), grid);
        T.Anim(thumb, WidthProperty, b.ActualWidth, anim ? 220 : 0, T.InOut);
        T.Anim((TranslateTransform)thumb.RenderTransform, TranslateTransform.XProperty, p.X, anim ? 220 : 0, T.InOut);
        thumb.Height = b.ActualHeight;
    }
}

class SwitchButton : ToggleButton
{
    public SwitchButton() { Style = (Style)Application.Current.Resources["Switch"]; }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); Sync(false); }
    protected override void OnChecked(RoutedEventArgs e) { base.OnChecked(e); Sync(true); }
    protected override void OnUnchecked(RoutedEventArgs e) { base.OnUnchecked(e); Sync(true); }
    void Sync(bool anim)
    {
        var th = GetTemplateChild("th") as Ellipse; var tr = GetTemplateChild("tr") as Border; if (th == null || tr == null) return;
        bool on = IsChecked == true;
        var tt = th.RenderTransform as TranslateTransform;
        if (tt == null || tt.IsFrozen) { tt = new TranslateTransform(); th.RenderTransform = tt; } // objetos do template vêm congelados
        if (anim) T.Anim(tt, TranslateTransform.XProperty, on ? 16 : 0, 180, T.EaseOut); else { tt.BeginAnimation(TranslateTransform.XProperty, null); tt.X = on ? 16 : 0; }
        var brush = tr.Background as SolidColorBrush; if (brush == null || brush.IsFrozen) { brush = new SolidColorBrush(brush != null ? brush.Color : T.C("#2A2A32")); tr.Background = brush; }
        var to = on ? T.C("#8B7CFF") : T.C("#2A2A32");
        if (anim && !T.Reduce) brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(160))); else { brush.BeginAnimation(SolidColorBrush.ColorProperty, null); brush.Color = to; }
    }
}

class HoldButton : Button
{
    readonly Rectangle over = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(70, 255, 93, 108)), RadiusX = 9, RadiusY = 9, RenderTransformOrigin = new Point(0, 0.5) };
    readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
    public event Action Confirmed = delegate { };
    public HoldButton(string text)
    {
        Style = (Style)Application.Current.Resources["Danger"];
        over.RenderTransform = new ScaleTransform(0, 1);
        var g = new Grid(); g.Children.Add(over); g.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) });
        Content = g; Padding = new Thickness(0); HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        timer.Tick += (s, e) => { timer.Stop(); Release(); Confirmed(); };
        PreviewMouseLeftButtonDown += (s, e) => Press(); PreviewMouseLeftButtonUp += (s, e) => Release(); MouseLeave += (s, e) => Release();
        PreviewKeyDown += (s, e) => { if ((e.Key == Key.Enter || e.Key == Key.Space) && !e.IsRepeat) Press(); };
        PreviewKeyUp += (s, e) => Release();
    }
    void Press() { timer.Start(); T.Anim((ScaleTransform)over.RenderTransform, ScaleTransform.ScaleXProperty, 1, 2000, T.Linear); } // devagar enquanto decide
    void Release() { timer.Stop(); T.Anim((ScaleTransform)over.RenderTransform, ScaleTransform.ScaleXProperty, 0, 200, T.EaseOut); } // rápido ao soltar
}

