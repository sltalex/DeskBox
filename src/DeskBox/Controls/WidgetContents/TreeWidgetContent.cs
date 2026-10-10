using DeskBox.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI.ViewManagement;

namespace DeskBox.Controls.WidgetContents;

/// <summary>Five hand-drawn silhouettes of the same growing tree.</summary>
public sealed class TreeWidgetContent : UserControl, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly TreeCounterService _counter = TreeCounterService.Instance;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Canvas _drawing = new() { Width = 260, Height = 220, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _stage = new() { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _stats = new() { FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ProgressBar _progress = new() { Height = 8, Margin = new Thickness(16, 8, 16, 4) };
    private readonly TextBlock _hint = new() { FontSize = 11, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly SolidColorBrush _brown = new(ColorHelper.FromArgb(255, 142, 91, 58));
    private readonly SolidColorBrush _green = new(ColorHelper.FromArgb(255, 100, 172, 94));
    private readonly SolidColorBrush _light = new(ColorHelper.FromArgb(255, 156, 205, 105));
    private readonly SolidColorBrush _dark = new(ColorHelper.FromArgb(255, 67, 135, 81));
    private readonly SolidColorBrush _fruit = new(ColorHelper.FromArgb(255, 241, 120, 78));
    private bool _visible = true;
    private bool _disposed;
    private int _lastStage;
    private long _lastFruitInventory = -1;
    private ElementTheme _lastDrawnTheme;
    private long _lastHarvested;
    private int _effectTicks;
    private bool _sway;

    public TreeWidgetContent(LocalizationService localization)
    {
        _localization = localization;
        var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(_drawing);
        stack.Children.Add(_stage);
        stack.Children.Add(_stats);
        stack.Children.Add(_progress);
        stack.Children.Add(_hint);
        Content = stack;
        _drawing.Tapped += (_, args) =>
        {
            TreeProgress state = _counter.Snapshot;
            Windows.Foundation.Point point = args.GetPosition(_drawing);
            if (state.Stage == 5 && state.FruitInventory > 0 &&
                point.X is >= 40 and <= 220 && point.Y is >= 8 and <= 150)
            {
                _counter.Harvest();
                Refresh();
            }
        };
        _timer.Tick += (_, _) =>
        {
            if (!_visible) return;
            Refresh(); Sway();
            if (_effectTicks > 0)
            {
                _drawing.Opacity = _effectTicks-- % 2 == 0 ? 1 : .8;
                if (_effectTicks == 0) _drawing.Opacity = 1;
            }
        };
        Loaded += (_, _) => { if (_visible) { _timer.Start(); Refresh(); } };
        Unloaded += (_, _) => _timer.Stop();
        ActualThemeChanged += (_, _) => Refresh();
        Refresh();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (visible && IsLoaded) { _timer.Start(); Refresh(); }
        else _timer.Stop();
    }

    private void Refresh()
    {
        if (_disposed) return;
        TreeProgress p = _counter.Snapshot;
        if (IsLoaded && new UISettings().AnimationsEnabled &&
            ((p.Stage > _lastStage && _lastStage != 0) || p.HarvestedFruit > _lastHarvested))
            _effectTicks = 3;
        _lastHarvested = p.HarvestedFruit;
        _stage.Text = _localization.T($"Tree.Stage.{p.Stage}");
        if (p.Stage < 5)
        {
            long next = p.Stage switch { 1 => 3L * p.DailyGoal, 2 => 9L * p.DailyGoal, 3 => 18L * p.DailyGoal, _ => 30L * p.DailyGoal };
            long prior = p.Stage switch { 1 => 0, 2 => 3L * p.DailyGoal, 3 => 9L * p.DailyGoal, _ => 18L * p.DailyGoal };
            _progress.Value = Math.Clamp((double)(p.TotalEnergy - prior) * 100 / Math.Max(1, next - prior), 0, 100);
            _stats.Text = _localization.Format("Tree.TodayGrowth", p.TodayKeys, p.TotalEnergy);
            _hint.Text = _localization.Format("Tree.NextStage", next);
        }
        else
        {
            _progress.Value = 100.0 * p.FruitEnergy / p.FruitCost;
            _stats.Text = _localization.Format("Tree.TodayFruit", p.TodayKeys, p.FruitInventory, p.HarvestedFruit);
            _hint.Text = _localization.Format("Tree.NextFruit", p.FruitEnergy, p.FruitCost);
        }
        if (_lastStage != p.Stage || _lastFruitInventory != p.FruitInventory || _lastDrawnTheme != ActualTheme)
        {
            Draw(p);
            _lastStage = p.Stage;
            _lastFruitInventory = p.FruitInventory;
            _lastDrawnTheme = ActualTheme;
        }
    }

    private void Draw(TreeProgress p)
    {
        _drawing.Children.Clear();
        bool night = ActualTheme == ElementTheme.Dark;
        Brush soil = new SolidColorBrush(night ? ColorHelper.FromArgb(255, 113, 90, 78) : ColorHelper.FromArgb(255, 191, 143, 102));
        Ellipse hill = new() { Width = 160, Height = 32, Fill = soil };
        Canvas.SetLeft(hill, 50); Canvas.SetTop(hill, 186); _drawing.Children.Add(hill);
        int stage = p.Stage;
        if (stage == 1)
        {
            Branch(130, 190, 130, 145, 6);
            Leaf(112, 135, 27, 18, -32, _light); Leaf(130, 132, 27, 18, 32, _green);
        }
        else
        {
            double top = stage switch { 2 => 109, 3 => 89, 4 => 67, _ => 47 };
            Branch(130, 190, 130, top, stage switch { 2 => 8, 3 => 12, 4 => 17, _ => 23 });
            int count = stage switch { 2 => 3, 3 => 5, 4 => 8, _ => 10 };
            for (int i = 0; i < count; i++)
            {
                double side = i % 2 == 0 ? -1 : 1;
                double y = top + 18 + i * (stage == 2 ? 18 : 13);
                double length = stage switch { 2 => 23, 3 => 35, 4 => 53, _ => 67 };
                Branch(130, y + 17, 130 + side * length, y - 16, Math.Max(3, 8 - i / 2));
                if (stage == 2) Leaf(130 + side * length - 12, y - 30, 32, 25, side * 20, i % 2 == 0 ? _light : _green);
            }
            if (stage >= 3)
            {
                double radius = stage switch { 3 => 36, 4 => 49, _ => 60 };
                Crown(130, top + 18, radius, _green);
                Crown(130 - radius * .7, top + 40, radius * .75, _light);
                Crown(130 + radius * .7, top + 40, radius * .75, _dark);
                Crown(130, top - 3, radius * .75, _light);
            }
        }
        if (stage == 5)
        {
            int shown = (int)Math.Min(12, p.FruitInventory);
            for (int i = 0; i < shown; i++)
            {
                double angle = i * 2.39996;
                double radius = 14 + 10 * Math.Sqrt(i);
                Fruit(130 + Math.Cos(angle) * radius, 78 + Math.Sin(angle) * radius * .7);
            }
            if (p.FruitInventory > 12)
            {
                var more = new TextBlock { Text = $"+{p.FruitInventory - 12}", Foreground = new SolidColorBrush(Colors.White), FontWeight = Microsoft.UI.Text.FontWeights.Bold };
                Canvas.SetLeft(more, 181); Canvas.SetTop(more, 71); _drawing.Children.Add(more);
            }
        }
    }

    private void Branch(double x1, double y1, double x2, double y2, double width)
    {
        _drawing.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = _brown, StrokeThickness = width, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
    }
    private void Leaf(double x, double y, double w, double h, double angle, Brush brush)
    {
        var leaf = new Ellipse { Width = w, Height = h, Fill = brush, RenderTransform = new RotateTransform { Angle = angle }, RenderTransformOrigin = new Windows.Foundation.Point(.5, .5) };
        Canvas.SetLeft(leaf, x); Canvas.SetTop(leaf, y); _drawing.Children.Add(leaf);
    }
    private void Crown(double x, double y, double radius, Brush brush)
    {
        var circle = new Ellipse
        {
            Width = radius * 2, Height = radius * 1.7, Fill = brush,
            RenderTransform = new RotateTransform(),
            RenderTransformOrigin = new Windows.Foundation.Point(.5, .9)
        };
        Canvas.SetLeft(circle, x - radius); Canvas.SetTop(circle, y - radius); _drawing.Children.Add(circle);
    }
    private void Fruit(double x, double y)
    {
        var fruit = new Ellipse { Width = 12, Height = 12, Fill = _fruit, Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 249, 209, 135)), StrokeThickness = 1 };
        Canvas.SetLeft(fruit, x); Canvas.SetTop(fruit, y); _drawing.Children.Add(fruit);
    }
    private void Sway()
    {
        if (!new UISettings().AnimationsEnabled) return;
        _sway = !_sway;
        foreach (UIElement element in _drawing.Children)
            if (element is Ellipse { RenderTransform: RotateTransform rotate }) rotate.Angle += _sway ? 2 : -2;
    }
    public void Dispose() { _disposed = true; _timer.Stop(); }
}
