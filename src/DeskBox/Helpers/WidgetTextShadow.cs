using System.Collections.Generic;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Helpers;
/// <summary>
/// Global state for the dual-layer text shadow (the Windows-native
/// DrawShadowText idea transplanted to XAML: the same text drawn twice, a
/// black copy offset by one pixel behind the real one — no blur, no
/// composition objects, no alpha-mask snapshots).
///
/// Shadow layers are ordinary TextBlocks marked with the
/// <see cref="GetLayer"/>/<see cref="SetLayer"/> attached property. Their
/// text, font metrics and visibility mirror the real TextBlock through
/// ElementName bindings, so XAML data binding keeps the layers in sync; the
/// only thing this class owns is the layers' opacity, flipped once when the
/// setting (or the high-contrast mode or the theme) changes. Nothing here
/// runs per frame.
///
/// The strength is theme-aware: the 55% black that matches the native look on
/// light surfaces nearly vanishes on the mid-gray dark widget plate — 55%
/// black over the 43-gray surface leaves a ~19-gray crescent, only ~24 levels
/// below the surface, which measured as invisible — so dark themes use a
/// stronger 80% copy instead (~8.6-gray crescent, ~34-level edge). Each layer
/// follows its own ActualTheme, so windows on mixed themes stay correct.
/// </summary>
public static class WidgetTextShadow
{
    /// <summary>Shadow strength on light surfaces; the 1px offset lives in each layer's Margin.</summary>
    public const double LayerOpacity = 0.55;

    /// <summary>Shadow strength on dark surfaces, where 55% black is imperceptible.</summary>
    public const double DarkLayerOpacity = 0.8;

    private static readonly List<WeakReference<TextBlock>> Layers = [];
    private static bool _enabled;

    /// <summary>Whether the user enabled the shadow (the raw setting value).</summary>
    public static bool IsEnabled => _enabled;

    /// <summary>
    /// Applies the setting. High contrast always forces the layers off: on
    /// the solid high-contrast backgrounds a shadow is noise, matching the
    /// native icon-label behavior.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        RefreshLayers();
    }

    private static double ResolveOpacityFor(TextBlock layer) =>
        !_enabled || WindowsCompatibilityService.IsHighContrast
            ? 0d
            : layer.ActualTheme == ElementTheme.Dark ? DarkLayerOpacity : LayerOpacity;

    private static void RefreshLayers()
    {
        for (int index = Layers.Count - 1; index >= 0; index--)
        {
            if (!Layers[index].TryGetTarget(out TextBlock? layer))
            {
                Layers.RemoveAt(index);
                continue;
            }

            layer.Opacity = ResolveOpacityFor(layer);
        }
    }

    public static readonly DependencyProperty LayerProperty =
        DependencyProperty.RegisterAttached(
            "Layer",
            typeof(bool),
            typeof(WidgetTextShadow),
            new PropertyMetadata(false, OnLayerChanged));

    public static bool GetLayer(DependencyObject obj) => (bool)obj.GetValue(LayerProperty);

    public static void SetLayer(DependencyObject obj, bool value) =>
        obj.SetValue(LayerProperty, value);

    private static void OnLayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock layer || !(bool)e.NewValue)
        {
            return;
        }

        layer.Opacity = ResolveOpacityFor(layer);
        // A theme flip (system or per-window override) changes which strength
        // is right; re-resolve for this layer only. The handler closes over
        // nothing but the sender, so the subscription is collectible along
        // with the layer itself.
        layer.ActualThemeChanged += (sender, _) =>
        {
            if (sender is TextBlock self)
            {
                self.Opacity = ResolveOpacityFor(self);
            }
        };
        // Weak registration: recycled item templates drop out on the next
        // refresh sweep, so no unload bookkeeping is needed.
        if (Layers.Count == 0 || !Layers.Exists(reference =>
                reference.TryGetTarget(out TextBlock? existing) &&
                ReferenceEquals(existing, layer)))
        {
            Layers.Add(new WeakReference<TextBlock>(layer));
        }
    }
}
