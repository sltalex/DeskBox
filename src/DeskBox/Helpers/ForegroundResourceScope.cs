using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace DeskBox.Helpers;

/// <summary>Applies the shared text palette to application and window resources.</summary>
public static class ForegroundResourceScope
{
    private static readonly ConditionalWeakTable<ResourceDictionary, Dictionary<string, SolidColorBrush>> OwnedBrushes = new();
    private static readonly string[] AliasKeys =
    [
        "DefaultTextForegroundThemeBrush", "TextControlForeground",
        "TextControlForegroundPointerOver", "TextControlForegroundFocused",
        "TextControlForegroundDisabled", "TextControlPlaceholderForeground",
        "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused",
        "TextControlPlaceholderForegroundDisabled", "GridViewItemForeground",
        "GridViewItemForegroundPointerOver", "GridViewItemForegroundSelected",
        "ListViewItemForeground", "ListViewItemForegroundPointerOver",
        "ListViewItemForegroundPressed", "ListViewItemForegroundSelected",
        "ListViewItemForegroundSelectedPointerOver", "ListViewItemForegroundSelectedPressed"
    ];

    public static Color ResolvePrimary(string mode, Color customColor, ElementTheme theme) => mode switch
    {
        WidgetForegroundSettings.ModeLight => Color.FromArgb(255, 245, 241, 232),
        WidgetForegroundSettings.ModeDark => Color.FromArgb(255, 48, 48, 46),
        WidgetForegroundSettings.ModeCustom => Color.FromArgb(255, customColor.R, customColor.G, customColor.B),
        _ when theme == ElementTheme.Light => Color.FromArgb(255, 48, 48, 46),
        _ => Color.FromArgb(255, 245, 241, 232)
    };

    public static bool Apply(FrameworkElement target, Color primary)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Apply(target.Resources, primary);
    }

    public static bool Apply(ResourceDictionary resources, Color primary)
    {
        ArgumentNullException.ThrowIfNull(resources);
        bool changed = SetBrush(resources, "TextFillColorPrimaryBrush", primary);
        changed |= SetBrush(resources, "TextFillColorSecondaryBrush", WithAlpha(primary, 0xE8));
        changed |= SetBrush(resources, "TextFillColorTertiaryBrush", WithAlpha(primary, 0xCC));
        changed |= SetBrush(resources, "TextFillColorDisabledBrush", WithAlpha(primary, 0x8F));
        changed |= SetBrush(resources, "ControlStrongFillColorDefaultBrush", primary);
        changed |= SetBrush(resources, "ButtonForeground", primary);
        changed |= SetBrush(resources, "SubtleButtonForeground", primary);
        changed |= SetAlias(resources, "DefaultTextForegroundThemeBrush", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "TextControlForeground", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "TextControlForegroundPointerOver", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "TextControlForegroundFocused", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "TextControlForegroundDisabled", "TextFillColorDisabledBrush");
        changed |= SetAlias(resources, "TextControlPlaceholderForeground", "TextFillColorSecondaryBrush");
        changed |= SetAlias(resources, "TextControlPlaceholderForegroundPointerOver", "TextFillColorSecondaryBrush");
        changed |= SetAlias(resources, "TextControlPlaceholderForegroundFocused", "TextFillColorSecondaryBrush");
        changed |= SetAlias(resources, "TextControlPlaceholderForegroundDisabled", "TextFillColorDisabledBrush");
        changed |= SetAlias(resources, "GridViewItemForeground", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "GridViewItemForegroundPointerOver", "TextFillColorSecondaryBrush");
        changed |= SetAlias(resources, "GridViewItemForegroundSelected", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "ListViewItemForeground", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "ListViewItemForegroundPointerOver", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "ListViewItemForegroundPressed", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "ListViewItemForegroundSelected", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "ListViewItemForegroundSelectedPointerOver", "TextFillColorPrimaryBrush");
        changed |= SetAlias(resources, "ListViewItemForegroundSelectedPressed", "TextFillColorPrimaryBrush");
        return changed;
    }

    /// <summary>Removes a palette previously installed on this local surface.</summary>
    public static void Clear(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ResourceDictionary resources = target.Resources;
        if (OwnedBrushes.TryGetValue(resources, out Dictionary<string, SolidColorBrush>? owned))
        {
            foreach (string key in owned.Keys)
                resources.Remove(key);
            OwnedBrushes.Remove(resources);
        }
        foreach (string key in AliasKeys)
            resources.Remove(key);
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static bool SetBrush(ResourceDictionary resources, string key, Color color)
    {
        Dictionary<string, SolidColorBrush> owned = OwnedBrushes.GetValue(resources, _ => new());
        if (owned.TryGetValue(key, out SolidColorBrush? brush))
        {
            if (brush.Color == color) return false;
            brush.Color = color;
            return true;
        }

        brush = new SolidColorBrush(color);
        owned[key] = brush;
        resources[key] = brush;
        return true;
    }

    private static bool SetAlias(ResourceDictionary resources, string alias, string source)
    {
        object brush = resources[source];
        if (resources.TryGetValue(alias, out object? current) && ReferenceEquals(current, brush))
            return false;
        resources[alias] = brush;
        return true;
    }
}
