using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Mura_Converter;

/// <summary>Slider with two draggable handles that select a [Start, End] range inside [0, Maximum].</summary>
internal sealed class RangeSlider : Canvas
{
    private const double ThumbSize = 14;

    private readonly Rectangle track = new() { Height = 4, RadiusX = 2, RadiusY = 2 };
    private readonly Rectangle selection = new() { Height = 4, RadiusX = 2, RadiusY = 2 };
    private readonly Ellipse startThumb = CreateThumb();
    private readonly Ellipse endThumb = CreateThumb();
    private readonly TextBlock bubbleText = new() { FontSize = 12, Foreground = Brushes.White };
    private readonly Popup bubble;
    private Ellipse? dragging;
    private Ellipse active;

    public double Maximum { get; private set; } = 1;
    public double Start { get; private set; }
    public double End { get; private set; } = 1;

    /// <summary>Raised while the user drags a handle.</summary>
    public event EventHandler? RangeChanged;

    public RangeSlider()
    {
        Height = 22;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        Children.Add(track);
        Children.Add(selection);
        Children.Add(startThumb);
        Children.Add(endThumb);
        active = startThumb;
        Focusable = true;
        FocusVisualStyle = null;
        bubble = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Top,
            VerticalOffset = -4,
            IsHitTestVisible = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x3E)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Child = bubbleText,
            },
        };
        GotKeyboardFocus += (_, _) => UpdateActiveLook();
        LostKeyboardFocus += (_, _) => { UpdateActiveLook(); bubble.IsOpen = false; };
        MouseLeave += (_, _) => { if (dragging == null && !IsKeyboardFocused) bubble.IsOpen = false; };
        Loaded += (_, _) =>
        {
            track.Fill = (Brush)FindResource("InputBorderBrush");
            selection.Fill = (Brush)FindResource("AccentBrush");
        };
        SizeChanged += (_, _) => Layout();
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.4;
    }

    private static Ellipse CreateThumb() => new()
    {
        Width = ThumbSize,
        Height = ThumbSize,
        Fill = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
        Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
        StrokeThickness = 2,
    };

    public void SetMaximum(double maximum)
    {
        Maximum = Math.Max(maximum, 1);
        SetRange(0, Maximum);
    }

    public void SetRange(double start, double end)
    {
        End = Math.Clamp(end, 0, Maximum);
        Start = Math.Clamp(start, 0, End);
        Layout();
    }

    private double Usable => Math.Max(ActualWidth - ThumbSize, 1);
    private double ToX(double value) => value / Maximum * Usable;
    private double ToValue(double x) => Math.Clamp((x - ThumbSize / 2) / Usable, 0, 1) * Maximum;

    private void Layout()
    {
        var mid = Height / 2;
        track.Width = Usable;
        SetLeft(track, ThumbSize / 2);
        SetTop(track, mid - 2);

        var x1 = ToX(Start);
        var x2 = ToX(End);
        selection.Width = Math.Max(x2 - x1, 0);
        SetLeft(selection, x1 + ThumbSize / 2);
        SetTop(selection, mid - 2);

        SetLeft(startThumb, x1);
        SetLeft(endThumb, x2);
        SetTop(startThumb, mid - ThumbSize / 2);
        SetTop(endThumb, mid - ThumbSize / 2);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var value = ToValue(e.GetPosition(this).X);
        // Grab whichever handle is closer; on a tie prefer the one that can actually move that way.
        var toStart = Math.Abs(value - Start);
        var toEnd = Math.Abs(value - End);
        dragging = toStart < toEnd || toStart == toEnd && value < Start ? startThumb : endThumb;
        active = dragging;
        Focus();
        UpdateActiveLook();
        CaptureMouse();
        Move(value);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging != null)
            Move(ToValue(e.GetPosition(this).X));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        dragging = null;
        ReleaseMouseCapture();
        if (!IsMouseOver)
            bubble.IsOpen = false;
    }

    private void Move(double value)
    {
        value = Math.Clamp(Math.Round(value), 0, Maximum);
        if (dragging == startThumb)
            Start = Math.Min(value, End);
        else
            End = Math.Max(value, Start);
        Layout();
        ShowBubble();
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Arrows nudge the active handle by 1 s (10 s with Shift); Home/End pick which handle.
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left: Nudge(-step); break;
            case Key.Right: Nudge(step); break;
            case Key.Home: active = startThumb; UpdateActiveLook(); ShowBubble(); break;
            case Key.End: active = endThumb; UpdateActiveLook(); ShowBubble(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Nudge(double delta)
    {
        dragging = active;
        Move((active == startThumb ? Start : End) + delta);
        dragging = null;
    }

    private void ShowBubble()
    {
        var value = active == startThumb ? Start : End;
        bubbleText.Text = YtDlpService.FormatDuration(TimeSpan.FromSeconds(value));
        bubble.PlacementTarget = active;
        // Reopening makes the popup follow the handle's new position.
        bubble.IsOpen = false;
        bubble.IsOpen = true;
    }

    private void UpdateActiveLook()
    {
        startThumb.StrokeThickness = IsKeyboardFocused && active == startThumb ? 3.5 : 2;
        endThumb.StrokeThickness = IsKeyboardFocused && active == endThumb ? 3.5 : 2;
    }
}
