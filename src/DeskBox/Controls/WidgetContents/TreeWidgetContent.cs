using DeskBox.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskBox.Controls.WidgetContents;

/// <summary>Five softly shaded illustrations of the same growing tree.</summary>
public sealed partial class TreeWidgetContent : UserControl, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly TreeCounterService _counter = TreeCounterService.Instance;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Grid _drawing = new() { Width = TreeIllustrationLayout.Width, Height = TreeIllustrationLayout.Height };
    private readonly Grid _treeSurface = new()
    {
        Width = TreeIllustrationLayout.Width,
        Height = TreeIllustrationLayout.Height,
        Background = new SolidColorBrush(Colors.Transparent),
        RenderTransformOrigin = new Windows.Foundation.Point(.5, .86)
    };
    private readonly Image _treeImage = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly Canvas _fruitLayer = new() { IsHitTestVisible = false };
    private readonly CompositeTransform _treeTransform = new();
    private readonly Storyboard _sway = new();
    private readonly Storyboard _celebration = new();
    private readonly TextBlock _stage = new() { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _fruitStats = new() { FontSize = 12, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ProgressBar _progress = new() { Height = 18 };
    private readonly TextBlock _percent = new() { FontSize = 11, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private bool _visible = true;
    private bool _disposed;
    private bool _motionRunning;
    private int _lastStage;
    private long _lastFruitInventory = -1;
    private long _lastHarvested;

    public TreeWidgetContent(LocalizationService localization)
    {
        _localization = localization;
        _drawing.Children.Add(new Image { Source = Asset("ground.svg"), Stretch = Stretch.Uniform, IsHitTestVisible = false });
        _treeSurface.RenderTransform = _treeTransform;
        _treeSurface.Children.Add(_treeImage);
        _treeSurface.Children.Add(_fruitLayer);
        _drawing.Children.Add(_treeSurface);
        ConfigureMotion();

        var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
        var progressRow = new Grid { Margin = new Thickness(16, 8, 16, 4) };
        progressRow.Children.Add(_progress);
        progressRow.Children.Add(_percent);
        stack.Children.Add(new Viewbox { Child = _drawing, Height = 220, Stretch = Stretch.Uniform });
        stack.Children.Add(_stage);
        stack.Children.Add(_fruitStats);
        stack.Children.Add(progressRow);
        Content = stack;
        _treeSurface.Tapped += (_, args) =>
        {
            TreeProgress state = _counter.Snapshot;
            Windows.Foundation.Point point = args.GetPosition(_treeSurface);
            if (TreeIllustrationLayout.CanHarvest(state.Stage, state.FruitInventory, point.X, point.Y))
            {
                _counter.Harvest();
                Refresh();
                args.Handled = true;
            }
        };
        _timer.Tick += (_, _) => { if (_visible) Refresh(); };
        Loaded += (_, _) =>
        {
            if (_visible && !_disposed) { _timer.Start(); Refresh(); }
        };
        Unloaded += (_, _) => { _timer.Stop(); StopMotion(); };
        Refresh();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (visible && IsLoaded && !_disposed) { _timer.Start(); Refresh(); }
        else { _timer.Stop(); StopMotion(); }
    }

    private void Refresh()
    {
        if (_disposed) return;
        TreeProgress p = _counter.Snapshot;
        bool celebrate = _lastStage != 0 && (p.Stage > _lastStage || p.HarvestedFruit > _lastHarvested);
        _lastHarvested = p.HarvestedFruit;
        _stage.Text = _localization.T($"Tree.Stage.{p.Stage}");
        _fruitStats.Visibility = p.Stage == 5 ? Visibility.Visible : Visibility.Collapsed;
        if (p.Stage == 5)
            _fruitStats.Text = _localization.Format("Tree.FruitStats", p.FruitInventory, p.HarvestedFruit);
        double percentage = p.MaturityPercentage;
        _progress.Value = percentage;
        _percent.Text = $"{percentage:0}%";
        if (_lastStage != p.Stage)
            _treeImage.Source = Asset(TreeIllustrationLayout.StageAssetName(p.Stage));
        if (_lastStage != p.Stage || _lastFruitInventory != p.FruitInventory)
            DrawFruit(p);
        _lastStage = p.Stage;
        _lastFruitInventory = p.FruitInventory;

        UpdateMotion();
        if (celebrate && _motionRunning)
        {
            _celebration.Stop();
            _celebration.Begin();
        }
    }

    private static SvgImageSource Asset(string fileName) => new(new Uri($"ms-appx:///Assets/Tree/{fileName}"));

    private void DrawFruit(TreeProgress state)
    {
        _fruitLayer.Children.Clear();
        if (state.Stage != 5) return;
        SvgImageSource fruitSource = Asset("fruit.svg");
        for (int i = 0; i < TreeIllustrationLayout.VisibleFruitCount(state.FruitInventory); i++)
        {
            (double x, double y) = TreeIllustrationLayout.FruitPositions[i];
            var fruit = new Image { Width = 24, Height = 28, Source = fruitSource };
            Canvas.SetLeft(fruit, x);
            Canvas.SetTop(fruit, y);
            _fruitLayer.Children.Add(fruit);
        }
        long extra = TreeIllustrationLayout.ExtraFruitCount(state.FruitInventory);
        if (extra > 0)
        {
            // Keep even a very large inventory inside the artwork's view box.
            var count = new TextBlock
            {
                Text = $"+{extra}",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            var badge = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(240, 39, 92, 61)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(7, 3, 7, 3),
                Child = count
            };
            var fitted = new Viewbox { Width = 104, Height = 25, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = badge };
            Canvas.SetLeft(fitted, 131);
            Canvas.SetTop(fitted, 19);
            _fruitLayer.Children.Add(fitted);
        }
    }

    private void ConfigureMotion()
    {
        var sway = new DoubleAnimation
        {
            From = -.85,
            To = .85,
            Duration = new Duration(TimeSpan.FromSeconds(2.6)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(sway, _treeTransform);
        Storyboard.SetTargetProperty(sway, "Rotation");
        _sway.Children.Add(sway);
        foreach (string property in new[] { "ScaleX", "ScaleY" })
        {
            var bounce = new DoubleAnimation
            {
                From = 1,
                To = 1.045,
                Duration = new Duration(TimeSpan.FromMilliseconds(240)),
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(bounce, _treeTransform);
            Storyboard.SetTargetProperty(bounce, property);
            _celebration.Children.Add(bounce);
        }
    }

    private void UpdateMotion()
    {
        bool shouldAnimate = !_disposed && _visible && IsLoaded && WindowsCompatibilityService.ShouldAnimate;
        if (shouldAnimate == _motionRunning) return;
        if (shouldAnimate) { _sway.Begin(); _motionRunning = true; }
        else StopMotion();
    }

    private void StopMotion()
    {
        _sway.Stop();
        _celebration.Stop();
        _treeTransform.Rotation = 0;
        _treeTransform.ScaleX = _treeTransform.ScaleY = 1;
        _motionRunning = false;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        StopMotion();
    }
}
