using DeskBox.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

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
    private readonly StackPanel _disks = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Canvas _cpuChart = Chart();
    private readonly Canvas _memoryChart = Chart();
    private readonly Canvas _networkChart = Chart();
    private readonly List<TextBlock> _diskNames = [];
    private readonly List<TextBlock> _diskPercents = [];
    private readonly List<TextBlock> _diskCapacities = [];
    private readonly List<TextBlock> _diskRates = [];
    private IDisposable? _subscription;
    private bool _visible = true;
    private bool _disposed;

    public SystemMonitorWidgetContent(LocalizationService localization)
    {
        _localization = localization;
        var root = new StackPanel { Spacing = 9, Margin = new Thickness(12, 8, 12, 10) };

        var primary = new Grid { ColumnSpacing = 18 };
        primary.ColumnDefinitions.Add(new ColumnDefinition());
        primary.ColumnDefinitions.Add(new ColumnDefinition());
        primary.Children.Add(PrimaryMetric("SystemMonitor.Cpu", _cpu));
        var memoryMetric = PrimaryMetric("SystemMonitor.Memory", _memory);
        Grid.SetColumn(memoryMetric, 1);
        primary.Children.Add(memoryMetric);
        root.Children.Add(primary);
        root.Children.Add(_memoryDetail);

        var temperatures = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < 3; i++) temperatures.ColumnDefinitions.Add(new ColumnDefinition());
        TextBlock[] values = [_cpuTemperature, _gpuTemperature, _diskTemperature];
        string[] labels = ["SystemMonitor.CpuTemperature", "SystemMonitor.GpuTemperature", "SystemMonitor.DiskTemperature"];
        for (int i = 0; i < 3; i++)
        {
            var cell = new StackPanel { Spacing = 2 };
            cell.Children.Add(Label(_localization.T(labels[i])));
            cell.Children.Add(values[i]);
            Grid.SetColumn(cell, i);
            temperatures.Children.Add(cell);
        }
        root.Children.Add(temperatures);

        root.Children.Add(Label(_localization.T("SystemMonitor.Disks")));
        root.Children.Add(new ScrollViewer
        {
            Content = _disks,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Height = 70
        });

        var trendHeading = Label(_localization.T("SystemMonitor.LastMinute"));
        trendHeading.Margin = new Thickness(0, 1, 0, 0);
        root.Children.Add(trendHeading);
        root.Children.Add(ChartRow("SystemMonitor.Cpu", _cpuChart));
        root.Children.Add(ChartRow("SystemMonitor.Memory", _memoryChart));
        root.Children.Add(ChartRow("SystemMonitor.Network", _networkChart));
        var rates = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        rates.Children.Add(Label(_localization.T("SystemMonitor.Download")));
        rates.Children.Add(_download);
        rates.Children.Add(Label(_localization.T("SystemMonitor.Upload")));
        rates.Children.Add(_upload);
        root.Children.Add(rates);

        Content = new ScrollViewer
        {
            Content = root,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Loaded += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => Stop();
        ShowUnavailable();
    }

    private Grid PrimaryMetric(string key, TextBlock value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Label(_localization.T(key)));
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private Grid ChartRow(string key, Canvas canvas)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var label = Label(_localization.T(key));
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
            _diskNames[diskIndex].Text = disk.Name;
            _diskPercents[diskIndex].Text = Percent(disk.UsedPercent);
            _diskCapacities[diskIndex].Text = disk.TotalBytes is long bytes
                ? FormatSize(bytes) : _localization.T("SystemMonitor.Unavailable");
            _diskRates[diskIndex].Text = $"↓ {SystemMonitorMath.FormatRate(disk.ReadBytesPerSecond)}   ↑ {SystemMonitorMath.FormatRate(disk.WriteBytesPerSecond)}";
            diskIndex++;
        }

        Draw(_cpuChart, history.Select(item => item.CpuPercent), null, 100);
        Draw(_memoryChart, history.Select(item => item.MemoryPercent), null, 100);
        double scale = Math.Max(1, history.SelectMany(item => new[]
            { item.DownloadBytesPerSecond ?? 0, item.UploadBytesPerSecond ?? 0 }).DefaultIfEmpty().Max());
        Draw(_networkChart, history.Select(item => item.DownloadBytesPerSecond),
            history.Select(item => item.UploadBytesPerSecond), scale);
    }

    private void EnsureDiskCards(int count)
    {
        if (_diskNames.Count == count && (count != 0 || _disks.Children.Count > 0)) return;
        _disks.Children.Clear();
        _diskNames.Clear();
        _diskPercents.Clear();
        _diskCapacities.Clear();
        _diskRates.Clear();
        if (count == 0)
        {
            _disks.Children.Add(Label(_localization.T("SystemMonitor.Unavailable")));
            return;
        }
        for (int index = 0; index < count; index++)
        {
            var card = new StackPanel { Width = 118, Spacing = 2, Padding = new Thickness(4, 1, 4, 1) };
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
            card.Children.Add(title);
            card.Children.Add(capacity);
            card.Children.Add(rates);
            _diskNames.Add(name);
            _diskPercents.Add(percent);
            _diskCapacities.Add(capacity);
            _diskRates.Add(rates);
            _disks.Children.Add(card);
        }
    }

    private static void Draw(Canvas canvas, IEnumerable<double?> primary, IEnumerable<double?>? secondary, double scale)
    {
        canvas.Children.Clear();
        AddLine(canvas, primary.ToArray(), scale, Color(44, 142, 228));
        if (secondary is not null) AddLine(canvas, secondary.ToArray(), scale, Color(70, 174, 133));
    }

    private static void AddLine(Canvas canvas, double?[] samples, double scale, SolidColorBrush brush)
    {
        if (samples.Length < 2) return;
        const double width = 230, height = 25;
        var line = new Polyline { Stroke = brush, StrokeThickness = 1.6, IsHitTestVisible = false };
        for (int i = 0; i < samples.Length; i++)
        {
            if (samples[i] is not double value) continue;
            line.Points.Add(new Point(i * width / 29, height - Math.Clamp(value / scale, 0, 1) * (height - 2)));
        }
        canvas.Children.Add(line);
    }

    private string Percent(double? value) => value.HasValue ? $"{value.Value:0}%" : _localization.T("SystemMonitor.Unavailable");
    private string Temperature(double? value) => value.HasValue ? $"{value.Value:0.#} °C" : _localization.T("SystemMonitor.Unavailable");
    private static string FormatSize(long bytes) => $"{bytes / 1073741824d:0.#} GB";
    private static Canvas Chart() => new() { Height = 26, MinWidth = 230, IsHitTestVisible = false };
    private static SolidColorBrush Color(byte red, byte green, byte blue) => new(Windows.UI.Color.FromArgb(255, red, green, blue));
    private static TextBlock Label(string text = "") => new() { Text = text, FontSize = 11, Opacity = 0.68, TextTrimming = TextTrimming.CharacterEllipsis };
    private static TextBlock Value(double size = 17) => new() { FontSize = size, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };

    public void Dispose() { _disposed = true; Stop(); }
}
