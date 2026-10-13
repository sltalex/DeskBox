using DeskBox.Helpers;
using DeskBox.Contracts;
using DeskBox.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage;
using Windows.System;

namespace DeskBox.Controls.WidgetContents;

/// <summary>Compact, live-only monitor. No per-second samples are persisted.</summary>
public sealed partial class SystemMonitorWidgetContent : UserControl, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly TextBlock _cpu = Value();
    private readonly TextBlock _memory = Value();
    private readonly TextBlock _memoryDetail = Label();
    private readonly TextBlock _cpuTemperature = Value(13);
    private readonly TextBlock _gpuTemperature = Value(13);
    private readonly TextBlock _diskTemperature = Value(13);
    private readonly TextBlock _download = Value(12);
    private readonly TextBlock _upload = Value(12);
    private readonly Grid _disks = new() { ColumnSpacing = SystemMonitorDiskLayout.CardSpacing, RowSpacing = SystemMonitorDiskLayout.CardSpacing, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Canvas _cpuChart = Chart();
    private readonly Canvas _memoryChart = Chart();
    private readonly Canvas _networkChart = Chart();
    private readonly List<TextBlock> _diskNames = [];
    private readonly List<TextBlock> _diskPercents = [];
    private readonly List<TextBlock> _diskCapacities = [];
    private readonly List<TextBlock> _diskRates = [];
    private readonly List<Border> _diskCards = [];
    private IReadOnlyList<SystemMonitorSnapshot> _latestHistory = [];
    private IDisposable? _subscription;
    private bool _visible = true;
    private bool _disposed;
    private SystemMonitorPresentationSettings _presentation = new("Bars", "Bars", "Bars", "#2C8EE4", "#46AE85", "#E6A23C", 2);

    public SystemMonitorWidgetContent(LocalizationService localization)
    {
        _localization = localization;
        if (App.Current?.SettingsService is { } settings)
        {
            _presentation = new FeatureWidgetsSettingsCoordinator(settings).ReadSystemMonitorPresentation();
            SystemMonitorService.Instance.SetRefreshInterval(_presentation.RefreshIntervalSeconds);
            settings.SettingsChanged += OnSettingsChanged;
        }
        var root = new StackPanel { Spacing = 6, Margin = new Thickness(9, 6, 9, 8) };

        var primary = new Grid { ColumnSpacing = 12 };
        primary.ColumnDefinitions.Add(new ColumnDefinition());
        primary.ColumnDefinitions.Add(new ColumnDefinition());
        primary.Children.Add(PrimaryMetric("SystemMonitor.Cpu", "\uE950", _cpu));
        var memoryMetric = PrimaryMetric("SystemMonitor.Memory", "\uE7F4", _memory);
        memoryMetric.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(memoryMetric, 1);
        primary.Children.Add(memoryMetric);
        root.Children.Add(primary);
        root.Children.Add(_memoryDetail);

        var temperatures = new Grid { ColumnSpacing = 5 };
        for (int i = 0; i < 3; i++) temperatures.ColumnDefinitions.Add(new ColumnDefinition());
        TextBlock[] values = [_cpuTemperature, _gpuTemperature, _diskTemperature];
        string[] labels = ["SystemMonitor.CpuTemperature", "SystemMonitor.GpuTemperature", "SystemMonitor.DiskTemperature"];
        for (int i = 0; i < 3; i++)
        {
            var cell = new StackPanel { Spacing = 1 };
            cell.Children.Add(IconLabel(_localization.T(labels[i]), i switch
            {
                0 => "\uE9CA", // temperature
                1 => "\uE7F4", // display
                _ => "\uEDA2"  // hard drive
            }));
            cell.Children.Add(values[i]);
            Grid.SetColumn(cell, i);
            temperatures.Children.Add(cell);
        }
        root.Children.Add(temperatures);

        root.Children.Add(IconLabel(_localization.T("SystemMonitor.Disks"), "\uEDA2"));
        root.Children.Add(_disks);

        var trendHeading = Label(_localization.T("SystemMonitor.LastMinute"));
        trendHeading.Margin = new Thickness(0, 0, 0, -1);
        root.Children.Add(trendHeading);
        root.Children.Add(ChartRow("SystemMonitor.Cpu", "\uE950", _cpuChart));
        root.Children.Add(ChartRow("SystemMonitor.Memory", "\uE7F4", _memoryChart));
        root.Children.Add(ChartRow("SystemMonitor.Network", "\uEE77", _networkChart));
        var rates = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        rates.Children.Add(IconLabel(_localization.T("SystemMonitor.Download"), "\uE896"));
        rates.Children.Add(_download);
        rates.Children.Add(IconLabel(_localization.T("SystemMonitor.Upload"), "\uE898"));
        rates.Children.Add(_upload);
        root.Children.Add(rates);

        Content = new ScrollViewer
        {
            Content = root,
            // The disk grid needs the widget's real width to decide how many
            // cards belong in a row; horizontal scrolling would measure it
            // with an effectively unbounded width.
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Loaded += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => Stop();
        ActualThemeChanged += (_, _) => UpdateDiskCardAppearance();
        _disks.SizeChanged += (_, _) => ArrangeDiskCards();
        _cpuChart.SizeChanged += Chart_SizeChanged;
        _memoryChart.SizeChanged += Chart_SizeChanged;
        _networkChart.SizeChanged += Chart_SizeChanged;
        ShowUnavailable();
    }

    private Grid PrimaryMetric(string key, string glyph, TextBlock value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(IconLabel(_localization.T(key), glyph));
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private Grid ChartRow(string key, string glyph, Canvas canvas)
    {
        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var label = IconLabel(_localization.T(key), glyph);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        Grid.SetColumn(canvas, 1);
        grid.Children.Add(canvas);
        return grid;
    }

    private void ShowUnavailable()
    {
        string unavailable = _localization.T("SystemMonitor.Unavailable");
        _cpu.Text = unavailable;
        _memory.Text = unavailable;
        _cpuTemperature.Text = unavailable;
        _gpuTemperature.Text = unavailable;
        _diskTemperature.Text = unavailable;
        _download.Text = unavailable;
        _upload.Text = unavailable;
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        UpdateSubscription();
    }

    private void UpdateSubscription()
    {
        if (_disposed || !_visible || !IsLoaded) { Stop(); return; }
        _subscription ??= SystemMonitorService.Instance.Subscribe(OnSample);
    }

    private void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _latestHistory = [];
    }

    private void OnSample(SystemMonitorSnapshot snapshot, IReadOnlyList<SystemMonitorSnapshot> history)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || !_visible || !IsLoaded) return;
            Render(snapshot, history);
        });
    }

    private void Render(SystemMonitorSnapshot snapshot, IReadOnlyList<SystemMonitorSnapshot> history)
    {
        _cpu.Text = Percent(snapshot.CpuPercent);
        _memory.Text = Percent(snapshot.MemoryPercent);
        _memoryDetail.Text = snapshot.UsedMemoryBytes is long used && snapshot.TotalMemoryBytes is long total
            ? $"{FormatSize(used)} / {FormatSize(total)}" : "";
        _cpuTemperature.Text = Temperature(snapshot.CpuTemperature);
        _gpuTemperature.Text = Temperature(snapshot.GpuTemperature);
        _diskTemperature.Text = Temperature(snapshot.DiskTemperature);
        _download.Text = SystemMonitorMath.FormatRate(snapshot.DownloadBytesPerSecond);
        _upload.Text = SystemMonitorMath.FormatRate(snapshot.UploadBytesPerSecond);

        EnsureDiskCards(snapshot.Disks.Count);
        int diskIndex = 0;
        foreach (var disk in snapshot.Disks)
        {
            _diskCards[diskIndex].Tag = disk.Name.TrimEnd('\\') + "\\";
            _diskNames[diskIndex].Text = disk.Name;
            _diskPercents[diskIndex].Text = Percent(disk.UsedPercent);
            _diskCapacities[diskIndex].Text = disk.TotalBytes is long bytes
                ? FormatSize(bytes) : _localization.T("SystemMonitor.Unavailable");
            _diskRates[diskIndex].Text = $"↓ {SystemMonitorMath.FormatRate(disk.ReadBytesPerSecond)}   ↑ {SystemMonitorMath.FormatRate(disk.WriteBytesPerSecond)}";
            diskIndex++;
        }

        _latestHistory = history;
        DrawCharts(history);
    }

    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_disposed && _visible) DrawCharts(_latestHistory);
    }

    private void DrawCharts(IReadOnlyList<SystemMonitorSnapshot> history)
    {
        DrawChart(_cpuChart, history.Select(item => item.CpuPercent), null, 100, _presentation.CpuColor, _presentation.CpuChartStyle);
        DrawChart(_memoryChart, history.Select(item => item.MemoryPercent), null, 100, _presentation.MemoryColor, _presentation.MemoryChartStyle);
        double scale = Math.Max(1, history.SelectMany(item => new[]
            { item.DownloadBytesPerSecond ?? 0, item.UploadBytesPerSecond ?? 0 }).DefaultIfEmpty().Max());
        DrawChart(_networkChart, history.Select(item => item.DownloadBytesPerSecond),
            history.Select(item => item.UploadBytesPerSecond), scale, _presentation.NetworkColor, _presentation.NetworkChartStyle);
    }

    private void OnSettingsChanged()
    {
        if (_disposed || App.Current?.SettingsService is not { } settings) return;
        _presentation = new FeatureWidgetsSettingsCoordinator(settings).ReadSystemMonitorPresentation();
        SystemMonitorService.Instance.SetRefreshInterval(_presentation.RefreshIntervalSeconds);
    }

    private void EnsureDiskCards(int count)
    {
        if (_diskNames.Count == count && (count != 0 || _disks.Children.Count > 0)) return;
        _disks.Children.Clear();
        _disks.RowDefinitions.Clear();
        _disks.ColumnDefinitions.Clear();
        _diskNames.Clear();
        _diskPercents.Clear();
        _diskCapacities.Clear();
        _diskRates.Clear();
        _diskCards.Clear();
        if (count == 0)
        {
            _disks.Children.Add(Label(_localization.T("SystemMonitor.Unavailable")));
            return;
        }
        for (int index = 0; index < count; index++)
        {
            var content = new StackPanel { Spacing = 1 };
            var title = new Grid();
            title.ColumnDefinitions.Add(new ColumnDefinition());
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Label();
            var percent = Value(12);
            title.Children.Add(name);
            Grid.SetColumn(percent, 1);
            title.Children.Add(percent);
            var capacity = Label();
            var rates = Label();
            content.Children.Add(title);
            content.Children.Add(capacity);
            content.Children.Add(rates);
            var card = new Border
            {
                Height = 54,
                Padding = new Thickness(4, 3, 4, 3),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = content
            };
            card.DoubleTapped += DiskCard_DoubleTapped;
            _diskNames.Add(name);
            _diskPercents.Add(percent);
            _diskCapacities.Add(capacity);
            _diskRates.Add(rates);
            _diskCards.Add(card);
        }
        ArrangeDiskCards();
        UpdateDiskCardAppearance();
    }

    private async void DiskCard_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (_disposed || sender is not Border { Tag: string path }) return;

        try
        {
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(path);
            if (!await Launcher.LaunchFolderAsync(folder))
                App.Log($"[SystemMonitor] Could not open disk folder: {path}");
        }
        catch (Exception ex)
        {
            App.Log($"[SystemMonitor] Open disk folder failed: {path}: {ex}");
        }
    }

    private void ArrangeDiskCards()
    {
        if (_diskCards.Count == 0) return;

        int columns = SystemMonitorDiskLayout.ColumnsForWidth(
            _disks.ActualWidth,
            _diskCards.Count);
        _disks.Children.Clear();
        _disks.RowDefinitions.Clear();
        _disks.ColumnDefinitions.Clear();
        for (int column = 0; column < columns; column++)
            _disks.ColumnDefinitions.Add(new ColumnDefinition());
        int rows = (int)Math.Ceiling((double)_diskCards.Count / columns);
        for (int row = 0; row < rows; row++)
            _disks.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int index = 0; index < _diskCards.Count; index++)
        {
            Border card = _diskCards[index];
            card.Width = double.NaN;
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(card, index / columns);
            Grid.SetColumn(card, index % columns);
            _disks.Children.Add(card);
        }
    }

    private void UpdateDiskCardAppearance()
    {
        var background = NeutralInteractionBrush.ResolveThemedResource(
            "CardBackgroundFillColorDefaultBrush", this);
        var stroke = NeutralInteractionBrush.ResolveThemedResource(
            "CardStrokeColorDefaultBrush", this);
        foreach (var card in _diskCards)
        {
            card.Background = background;
            card.BorderBrush = stroke;
            WidgetSurfaceForegroundScope.ApplyForBackground(card, background);
        }
    }

    private static void DrawChart(Canvas canvas, IEnumerable<double?> primary, IEnumerable<double?>? secondary, double scale, string color, string style)
    {
        double width = SystemMonitorChartLayout.UsableWidth(canvas.ActualWidth);
        canvas.Children.Clear();
        if (width <= 0) return;
        if (style == "Line")
        {
            DrawLines(canvas, primary, secondary, scale, color, width);
            return;
        }
        double?[] first = primary.ToArray();
        double?[]? second = secondary?.ToArray();
        int count = Math.Max(first.Length, second?.Length ?? 0);
        if (count == 0) return;
        const double height = 20;
        bool paired = second is not null;
        var (slot, barWidth, gap) = SystemMonitorChartLayout.BarMetrics(width, count, paired);
        for (int index = 0; index < count; index++)
        {
            AddBar(canvas, index, first, scale, height, slot, barWidth,
                ParseColor(color), 0);
            if (paired)
                AddBar(canvas, index, second!, scale, height, slot, barWidth,
                    ParseColor(color, 0.58), 1, gap);
        }
    }

    private static void DrawLines(Canvas canvas, IEnumerable<double?> primary, IEnumerable<double?>? secondary, double scale, string color, double width)
    {
        double?[] first = primary.ToArray();
        double?[]? second = secondary?.ToArray();
        int count = Math.Max(first.Length, second?.Length ?? 0);
        if (count < 2) return;
        const double height = 20;
        double step = SystemMonitorChartLayout.LineStep(width, count);
        AddLine(canvas, first, scale, height, step, ParseColor(color));
        if (second is not null) AddLine(canvas, second, scale, height, step, ParseColor(color, 0.58));
    }

    private static void AddLine(Canvas canvas, double?[] samples, double scale, double height, double step, SolidColorBrush brush)
    {
        for (int i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] is not double a || samples[i] is not double b) continue;
            var line = new Line { X1 = (i - 1) * step, X2 = i * step, Y1 = height - Math.Clamp(a / Math.Max(1, scale), 0, 1) * (height - 1), Y2 = height - Math.Clamp(b / Math.Max(1, scale), 0, 1) * (height - 1), Stroke = brush, StrokeThickness = 1.5 };
            canvas.Children.Add(line);
        }
    }

    private static void AddBar(Canvas canvas, int index, double?[] samples, double scale,
        double height, double slot, double barWidth, SolidColorBrush brush, int pair, double gap = 0)
    {
        if (index >= samples.Length || samples[index] is not double value) return;
        double barHeight = Math.Max(1,
            Math.Clamp(value / Math.Max(1, scale), 0, 1) * (height - 1));
        var bar = new Rectangle
        {
            Width = barWidth,
            Height = barHeight,
            Fill = brush,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(bar, index * slot + (pair == 0 ? 0 : barWidth + gap));
        Canvas.SetTop(bar, height - barHeight);
        canvas.Children.Add(bar);
    }

    private string Percent(double? value) => value.HasValue ? $"{value.Value:0}%" : _localization.T("SystemMonitor.Unavailable");
    private string Temperature(double? value) => value.HasValue ? $"{value.Value:0.#} °C" : _localization.T("SystemMonitor.Unavailable");
    private static string FormatSize(long bytes) => $"{bytes / 1073741824d:0.#} GB";
    private static Canvas Chart() => new() { Height = 21, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch, IsHitTestVisible = false };
    private static SolidColorBrush ParseColor(string value, double opacity = 1)
    {
        try
        {
            uint rgb = Convert.ToUInt32(value.TrimStart('#'), 16);
            return new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(255 * opacity), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }
        catch { return new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(255 * opacity), 44, 142, 228)); }
    }
    private static TextBlock Label(string text = "") => new() { Text = text, FontSize = 11, Opacity = 0.68, TextTrimming = TextTrimming.CharacterEllipsis };
    private StackPanel IconLabel(string text, string glyph)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            Opacity = 0.72,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, Label(text) }
        };
    }
    private static TextBlock Value(double size = 17) => new() { FontSize = size, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };

    public void Dispose() { _disposed = true; if (App.Current?.SettingsService is { } settings) settings.SettingsChanged -= OnSettingsChanged; Stop(); }
}

internal static class SystemMonitorDiskLayout
{
    public const double MinimumCardWidth = 128;
    public const double CardSpacing = 4;

    public static int ColumnsForWidth(double availableWidth, int diskCount)
    {
        if (diskCount <= 0) return 1;
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return 1;
        int columns = Math.Max(1, (int)Math.Floor(
            (availableWidth + CardSpacing) / (MinimumCardWidth + CardSpacing)));
        return Math.Min(columns, diskCount);
    }
}

internal static class SystemMonitorChartLayout
{
    public static double UsableWidth(double actualWidth) =>
        double.IsFinite(actualWidth) ? Math.Max(0, actualWidth) : 0;

    public static (double Slot, double BarWidth, double Gap) BarMetrics(
        double availableWidth, int sampleCount, bool paired)
    {
        if (sampleCount <= 0) return (0, 0, 0);
        double slot = UsableWidth(availableWidth) / sampleCount;
        double gap = Math.Min(1, slot / 4);
        return (slot, paired ? (slot - gap) / 2 : slot - gap, gap);
    }

    public static double LineStep(double availableWidth, int sampleCount) =>
        sampleCount < 2 ? 0 : Math.Max(0, UsableWidth(availableWidth) - 1) / (sampleCount - 1);
}
