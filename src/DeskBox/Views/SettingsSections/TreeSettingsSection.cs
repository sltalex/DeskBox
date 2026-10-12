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
    private readonly ToggleSwitch _pause = new();
    private readonly TextBlock _title = new() { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _status = new() { FontSize = 13, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private SettingsViewModel? _viewModel;
    private LocalizationService? _localization;
    private bool _updating;
    private int _refreshQueued;

    public TreeSettingsSection()
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        panel.Children.Add(_title);
        panel.Children.Add(_status);
        panel.Children.Add(_enable);
        panel.Children.Add(_pause);
        Content = panel;

        _enable.Toggled += (_, _) =>
        {
            if (_updating || _viewModel is null) return;
            _viewModel.SetWidgetEnabled(WidgetKind.Tree, _enable.IsOn);
            _counter.SetEnabled(_enable.IsOn);
        };
        _pause.Toggled += (_, _) =>
        {
            if (!_updating) _counter.SetPaused(_pause.IsOn);
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
        _pause.Header = _localization.T("Tree.Settings.StatisticsStatus");
        _pause.OnContent = _localization.T("Tree.Settings.StatisticsPaused");
        _pause.OffContent = _localization.T("Tree.Settings.StatisticsRunning");
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
            _pause.IsOn = snapshot.Paused;
            _status.Text = snapshot.Stage >= 5
                ? _localization?.Format("Tree.Settings.ProgressComplete", snapshot.TotalEnergy) ?? string.Empty
                : _localization?.Format(
                    "Tree.Settings.Progress",
                    snapshot.TotalEnergy,
                    snapshot.NextStagePercentage,
                    snapshot.NextStageRemainingEnergy) ?? string.Empty;
        }
        finally { _updating = false; }
    }
}
