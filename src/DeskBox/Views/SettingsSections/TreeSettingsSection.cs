using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Views.SettingsSections;

public sealed partial class TreeSettingsSection : UserControl
{
    private readonly TreeCounterService _counter = TreeCounterService.Instance;
    private readonly DispatcherQueue _uiQueue = DispatcherQueue.GetForCurrentThread();
    private readonly ToggleSwitch _enable = new() { OnContent = "", OffContent = "" };
    private readonly ToggleSwitch _statistics = new();
    private readonly TextBlock _title = new() { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _status = new() { FontSize = 13, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _nextStageProgress = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _nextStageBar = new() { Minimum = 0, Maximum = 100, Height = 8 };
    private readonly TextBlock _totalGrowthProgress = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _totalGrowthBar = new() { Minimum = 0, Maximum = 100, Height = 8 };
    private SettingsViewModel? _viewModel;
    private LocalizationService? _localization;
    private bool _updating;
    private int _refreshQueued;

    public TreeSettingsSection()
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        panel.Children.Add(_title);
        panel.Children.Add(_status);
        panel.Children.Add(_nextStageProgress);
        panel.Children.Add(_nextStageBar);
        panel.Children.Add(_totalGrowthProgress);
        panel.Children.Add(_totalGrowthBar);
        panel.Children.Add(_enable);
        panel.Children.Add(_statistics);
        Content = panel;

        _enable.Toggled += (_, _) =>
        {
            if (_updating || _viewModel is null) return;
            _viewModel.SetWidgetEnabled(WidgetKind.Tree, _enable.IsOn);
            _counter.SetEnabled(_enable.IsOn);
            Refresh();
        };
        _statistics.Toggled += (_, _) =>
        {
            if (_updating || !_statistics.IsEnabled) return;
            _counter.SetPaused(TreeStatisticsState.PausedFromToggle(_statistics.IsOn));
            Refresh();
        };
        Loaded += (_, _) =>
        {
            _counter.Changed += OnChanged;
            if (_localization is not null) _localization.LanguageChanged += OnLanguageChanged;
            RenderLabels();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _counter.Changed -= OnChanged;
            if (_localization is not null) _localization.LanguageChanged -= OnLanguageChanged;
        };
    }

    public void Configure(SettingsViewModel viewModel, LocalizationService localization)
    {
        _viewModel = viewModel;
        _localization = localization;
        RenderLabels();
        Refresh();
    }

    private void RenderLabels()
    {
        if (_localization is null) return;
        _title.Text = _localization.T("Tree.Title");
        _enable.Header = _localization.T("Tree.Settings.Enable");
        _statistics.Header = _localization.T("Tree.Settings.EnableStatistics");
        _statistics.OnContent = _localization.T("Tree.Settings.StatisticsRunning");
        _statistics.OffContent = _localization.T("Tree.Settings.StatisticsPaused");
    }

    private void OnChanged()
    {
        // Keyboard hooks run on their own thread. Never read a XAML property
        // there: even IsLoaded is a thread-affine COM call.
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        if (!_uiQueue.TryEnqueue(() =>
            {
                try { if (IsLoaded) Refresh(); }
                finally { Interlocked.Exchange(ref _refreshQueued, 0); }
            }))
            Interlocked.Exchange(ref _refreshQueued, 0);
    }

    private void OnLanguageChanged()
    {
        RenderLabels();
        Refresh();
    }

    private void Refresh()
    {
        if (_viewModel is null) return;
        _updating = true;
        try
        {
            _enable.IsOn = _viewModel.IsWidgetEnabled(WidgetKind.Tree);
            TreeProgress snapshot = _counter.Snapshot;
            TreeStatisticsState statistics = TreeStatisticsState.FromProgress(_enable.IsOn, snapshot.Paused);
            _statistics.IsEnabled = statistics.ToggleEnabled;
            _statistics.IsOn = statistics.ToggleOn;
            _statistics.OffContent = _localization?.T(statistics.OffContentKey) ?? string.Empty;
            _status.Text = _localization?.T($"Tree.Stage.{snapshot.Stage}") ?? string.Empty;
            _nextStageBar.Value = snapshot.NextStagePercentage;
            _totalGrowthBar.Value = snapshot.MaturityPercentage;
            _nextStageProgress.Text = snapshot.Stage >= 5
                ? _localization?.T("Tree.Settings.NextStageComplete") ?? string.Empty
                : _localization?.Format(
                    "Tree.Settings.NextStageProgress",
                    snapshot.NextStagePercentage,
                    snapshot.NextStageRemainingEnergy) ?? string.Empty;
            _totalGrowthProgress.Text = _localization?.Format(
                "Tree.Settings.TotalGrowthProgress",
                snapshot.TotalEnergy,
                snapshot.MaturityPercentage) ?? string.Empty;
        }
        finally { _updating = false; }
    }
}
