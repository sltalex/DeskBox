using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Views.SettingsSections;

public sealed class TreeSettingsSection : UserControl
{
    private readonly TreeCounterService _counter = TreeCounterService.Instance;
    private readonly NumberBox _goal = new() { Minimum = 1, Maximum = 1000000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 180 };
    private readonly Button _saveGoal = new();
    private readonly ToggleSwitch _pause = new() { OnContent = "", OffContent = "" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private LocalizationService? _localization;
    private bool _updating;

    public TreeSettingsSection()
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        var title = new TextBlock { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var explanation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var goalLabel = new TextBlock();
        var pauseLabel = new TextBlock();
        var reset = new Button();
        panel.Children.Add(title);
        panel.Children.Add(explanation);
        panel.Children.Add(goalLabel);
        panel.Children.Add(_goal);
        panel.Children.Add(_saveGoal);
        panel.Children.Add(pauseLabel);
        panel.Children.Add(_pause);
        panel.Children.Add(_status);
        panel.Children.Add(reset);
        Content = panel;
        Loaded += (_, _) => { _counter.Changed += OnChanged; Refresh(); };
        _saveGoal.Click += (_, _) =>
        {
            if (double.IsNaN(_goal.Value)) return;
            int value = (int)Math.Clamp(Math.Round(_goal.Value), 1, 1000000);
            _counter.SetGoal(value);
            Refresh();
        };
        _pause.Toggled += (_, _) => { if (!_updating) { _counter.SetPaused(_pause.IsOn); Refresh(); } };
        reset.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Text("Tree.Settings.ResetTitle"),
                Content = Text("Tree.Settings.ResetBody"),
                PrimaryButtonText = Text("Tree.Settings.ResetConfirm"),
                CloseButtonText = Text("Tree.Settings.Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) _counter.Reset();
            Refresh();
        };
        Unloaded += (_, _) => _counter.Changed -= OnChanged;
        void RenderLabels()
        {
            title.Text = Text("Tree.Title");
            explanation.Text = Text("Tree.Settings.Intro");
            goalLabel.Text = Text("Tree.Settings.Goal");
            _saveGoal.Content = Text("Tree.Settings.Save");
            pauseLabel.Text = Text("Tree.Settings.Pause");
            reset.Content = Text("Tree.Settings.Reset");
        }
        Loaded += (_, _) => RenderLabels();
    }

    private string Text(string key) => _localization?.T(key) ?? LocalizationService.DefaultText(key);
    public void Configure(LocalizationService localization) { _localization = localization; Refresh(); }
    private void OnChanged()
    {
        if (!IsLoaded || Visibility != Visibility.Visible) return;
        if (DispatcherQueue.HasThreadAccess) Refresh(controls: false);
        else DispatcherQueue.TryEnqueue(() => Refresh(controls: false));
    }
    private void Refresh(bool controls = true)
    {
        TreeProgress p = _counter.Snapshot;
        if (controls)
        {
            _updating = true;
            _goal.Value = p.DailyGoal;
            _pause.IsOn = p.Paused;
            _updating = false;
        }
        _status.Text = _localization?.Format("Tree.Settings.Status", p.Stage, p.TotalEnergy, p.TodayKeys, p.FruitInventory, p.HarvestedFruit)
            ?? LocalizationService.DefaultFormat("Tree.Settings.Status", p.Stage, p.TotalEnergy, p.TodayKeys, p.FruitInventory, p.HarvestedFruit);
    }
}
