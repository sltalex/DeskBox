using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DeskBox.Controls;

/// <summary>
/// Per-widget background customizer: pick an image (relayed through the
/// owning window because the system picker dismisses the flyout), choose
/// fill/contain fit, adjust the readability scrim strength, and clear the
/// override. Fit and dim apply live; the widget behind the flyout is the
/// preview.
/// </summary>
public sealed partial class WidgetBackgroundCustomizer : UserControl
{
    private WidgetConfig? _config;
    private bool _isSyncingControls;

    /// <summary>Raised after the config was mutated and persisted.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when the user asked for the system image picker; the hosting
    /// flyout has already been hidden.
    /// </summary>
    public event EventHandler? PickImageRequested;

    public FlyoutBase? HostFlyout { get; set; }

    public WidgetBackgroundCustomizer()
    {
        InitializeComponent();

        FitSegmented.SelectionChanged += (_, _) =>
        {
            if (_isSyncingControls)
            {
                return;
            }

            SetFit(FitSegmented.SelectedIndex == 0
                ? WidgetBackgroundCustomization.FitFill
                : WidgetBackgroundCustomization.FitContain);
        };
        DimSlider.ValueChanged += DimSlider_ValueChanged;
        ClearButton.Click += (_, _) => ResetToDefault();
        ChooseImageButton.Click += (_, _) =>
        {
            HostFlyout?.Hide();
            PickImageRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void Initialize(WidgetConfig config)
    {
        _config = config;
        LocalizationService localization = App.Current.LocalizationService;
        TitleText.Text = localization.T("Widget.CustomBackground.Title");
        SubtitleText.Text = localization.T("Widget.CustomBackground.Subtitle");
        ChooseImageButton.Content = localization.T("Widget.CustomBackground.ChooseImage");
        ImageFormatsHint.Text = localization.T("Widget.CustomIcon.ImageHint");
        FitLabel.Text = localization.T("Widget.CustomBackground.FitLabel");
        FitFillItem.Content = localization.T("Widget.CustomBackground.FitFill");
        FitContainItem.Content = localization.T("Widget.CustomBackground.FitContain");
        DimLabel.Text = localization.T("Widget.CustomBackground.DimLabel");
        ClearButton.Content = localization.T("Widget.CustomBackground.Clear");
        RefreshFromConfig();
    }

    private void SetFit(string fit)
    {
        if (_config is null)
        {
            return;
        }

        WidgetBackgroundCustomization.SetFitOverride(_config, fit);
        PersistAndNotify();
        SyncOptionControls();
    }

    private void DimSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isSyncingControls || _config is null)
        {
            return;
        }

        WidgetBackgroundCustomization.SetDimPercent(_config, e.NewValue);
        DimValueText.Text = ((int)Math.Round(e.NewValue)).ToString();
        PersistAndNotify();
    }

    private void ResetToDefault()
    {
        if (_config is null)
        {
            return;
        }

        string? imageFileName = WidgetBackgroundCustomization.GetImageFileNameOverride(_config);
        WidgetBackgroundCustomization.Clear(_config);
        if (imageFileName is not null)
        {
            WidgetTitleIconAssetStore.Current.DeleteImage(_config.Id, imageFileName);
        }

        PersistAndNotify();
        RefreshFromConfig();
    }

    private void PersistAndNotify()
    {
        if (App.Current.SettingsService is { } settingsService)
        {
            // Per-widget chrome: the shell refresh rides the Changed event,
            // so skip the global settings notification — the dim slider fires
            // this per tick and must not fan out to every subscriber.
            settingsService.UpdateWidget(_config!, notifySubscribers: false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshFromConfig()
    {
        if (_config is null)
        {
            return;
        }

        string? imageFileName = WidgetBackgroundCustomization.GetImageFileNameOverride(_config);
        string? imagePath = WidgetTitleIconAssetStore.Current.ResolveBackgroundPath(
            _config.Id,
            imageFileName);
        bool hasImage = imagePath is not null;
        LocalizationService localization = App.Current.LocalizationService;

        PreviewImage.Source = hasImage
            ? WidgetBackgroundImageSourceFactory.TryCreate(imagePath)
            : null;
        PreviewImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        PreviewEmptyIcon.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        SubtitleText.Text = localization.T(
            hasImage
                ? "Widget.CustomBackground.ImageCurrent"
                : "Widget.CustomBackground.ImageNone");
        OptionsPanel.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        SyncOptionControls();
    }

    private void SyncOptionControls()
    {
        if (_config is null)
        {
            return;
        }

        _isSyncingControls = true;
        try
        {
            string fit = WidgetBackgroundCustomization.GetFitOverride(_config) ??
                WidgetBackgroundCustomization.FitFill;
            FitSegmented.SelectedIndex = fit == WidgetBackgroundCustomization.FitContain ? 1 : 0;
            double dim = WidgetBackgroundCustomization.ResolveDimPercent(_config);
            DimSlider.Value = dim;
            DimValueText.Text = ((int)Math.Round(dim)).ToString();
        }
        finally
        {
            _isSyncingControls = false;
        }
    }
}
