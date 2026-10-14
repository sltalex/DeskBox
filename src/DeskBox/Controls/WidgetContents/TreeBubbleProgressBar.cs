using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace DeskBox.Controls.WidgetContents;

/// <summary>A rectangular growth track with a bounded, transient bubble layer.</summary>
internal sealed partial class TreeBubbleProgressBar : UserControl, IDisposable
{
    private const int MaximumBubbles = 10;
    private readonly Grid _viewport = new();
    private readonly Grid _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Canvas _bubbles = new() { IsHitTestVisible = false };
    private readonly RectangleGeometry _fillClip = new();
    private readonly DispatcherTimer _spawnTimer = new();
    private readonly List<Storyboard> _animations = [];
    private double _value;
    private bool _animationEnabled;
    private bool _disposed;

    public TreeBubbleProgressBar()
    {
        Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        _fill.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = ColorHelper.FromArgb(255, 79, 155, 99) },
                new GradientStop { Offset = 1, Color = ColorHelper.FromArgb(255, 132, 199, 137) }
            }
        };
        _fill.Clip = _fillClip;
        _fill.Children.Add(_bubbles);
        _viewport.Children.Add(_fill);
        Content = new Border
        {
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(105, 88, 139, 94)),
            Background = new SolidColorBrush(ColorHelper.FromArgb(150, 195, 225, 187)),
            Child = _viewport
        };
        _viewport.SizeChanged += (_, _) => UpdateFill();
        _spawnTimer.Tick += SpawnTimer_Tick;
        Loaded += (_, _) => UpdateFill();
        Unloaded += (_, _) => StopBubbles();
    }

    public double Value
    {
        get => _value;
        set
        {
            double next = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
            if (_value == next) return;
            double previous = _value;
            _value = next;
            UpdateFill();
            FrameworkElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
                RangeValuePatternIdentifiers.ValueProperty, previous, next);
        }
    }

    public void SetAnimationEnabled(bool enabled)
    {
        _animationEnabled = enabled;
        UpdateAnimationState();
    }

    private void UpdateFill()
    {
        _fill.Width = Math.Max(0, _viewport.ActualWidth) * _value / 100;
        var fillRect = new Rect(0, 0, _fill.Width, Math.Max(0, _viewport.ActualHeight));
        _fillClip.Rect = fillRect;
        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        if (_disposed || !_animationEnabled || !IsLoaded || _fill.Width < 2 || _viewport.ActualHeight <= 0)
        {
            StopBubbles();
            return;
        }
        if (_spawnTimer.IsEnabled) return;
        SpawnBubble();
        ScheduleNextBubble();
        _spawnTimer.Start();
    }

    private void SpawnTimer_Tick(object? sender, object e)
    {
        SpawnBubble();
        ScheduleNextBubble();
    }

    private void ScheduleNextBubble() =>
        _spawnTimer.Interval = TimeSpan.FromMilliseconds(Random.Shared.Next(450, 1000));

    private void SpawnBubble()
    {
        if (_disposed || !_animationEnabled || !IsLoaded || _fill.Width < 2 ||
            _viewport.ActualHeight <= 0 || _animations.Count >= MaximumBubbles) return;

        double diameter = Math.Min(_fill.Width, 2.5 + Random.Shared.NextDouble() * 3.5);
        double seconds = 3.6 + Random.Shared.NextDouble() * 2.2;
        double drift = (1.5 + Random.Shared.NextDouble() * 2.5) * (Random.Shared.Next(2) == 0 ? -1 : 1);
        double opacity = .45 + Random.Shared.NextDouble() * .25;
        var transform = new CompositeTransform();
        var bubble = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(100, 255, 255, 255)),
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(200, 255, 255, 255)),
            StrokeThickness = .65,
            Opacity = 0,
            RenderTransform = transform,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(bubble, Random.Shared.NextDouble() * Math.Max(0, _fill.Width - diameter));
        Canvas.SetTop(bubble, _viewport.ActualHeight - diameter * .15);
        _bubbles.Children.Add(bubble);

        var animation = new Storyboard { FillBehavior = FillBehavior.Stop };
        var rise = new DoubleAnimation
        {
            From = 0,
            To = -(_viewport.ActualHeight + diameter),
            Duration = new Duration(TimeSpan.FromSeconds(seconds))
        };
        Storyboard.SetTarget(rise, transform);
        Storyboard.SetTargetProperty(rise, "TranslateY");
        animation.Children.Add(rise);
        AddFrames(animation, transform, "TranslateX", seconds,
            (0, 0), (.35, drift), (.7, -drift * .3), (1, drift * .5));
        AddFrames(animation, bubble, "Opacity", seconds,
            (0, 0), (.18, opacity), (.68, opacity), (1, 0));
        animation.Completed += (_, _) =>
        {
            _animations.Remove(animation);
            _bubbles.Children.Remove(bubble);
        };
        _animations.Add(animation);
        animation.Begin();
    }

    private static void AddFrames(Storyboard storyboard, DependencyObject target,
        string property, double seconds, params (double Progress, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromSeconds(seconds))
        };
        foreach (var frame in frames)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * frame.Progress)),
                Value = frame.Value,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void StopBubbles()
    {
        _spawnTimer.Stop();
        foreach (Storyboard animation in _animations) animation.Stop();
        _animations.Clear();
        _bubbles.Children.Clear();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new GrowthProgressAutomationPeer(this);

    private sealed partial class GrowthProgressAutomationPeer(TreeBubbleProgressBar owner)
        : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => nameof(ProgressBar);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPatternCore(patternInterface);
        public bool IsReadOnly => true;
        public double Value => owner.Value;
        public double Minimum => 0;
        public double Maximum => 100;
        public double SmallChange => double.NaN;
        public double LargeChange => double.NaN;
        public void SetValue(double value) => throw new InvalidOperationException("Progress is read-only.");
    }

    public void Dispose()
    {
        _disposed = true;
        StopBubbles();
        _spawnTimer.Tick -= SpawnTimer_Tick;
    }
}
