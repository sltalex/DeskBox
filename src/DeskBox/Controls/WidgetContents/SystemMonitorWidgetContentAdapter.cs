using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Controls.WidgetContents;

public sealed class SystemMonitorWidgetContentAdapter : WidgetContentAdapterBase
{
    private bool _visible = true;

    public SystemMonitorWidgetContentAdapter(WidgetConfig config, LocalizationService localization)
        : base(config, () => new SystemMonitorWidgetContent(localization)) { }

    public override void OnWindowVisibilityChanged(bool visible)
    {
        _visible = visible;
        if (MaterializedView is SystemMonitorWidgetContent view) view.SetVisible(visible);
    }

    protected override void OnViewMaterialized(Microsoft.UI.Xaml.FrameworkElement view)
    {
        if (view is SystemMonitorWidgetContent monitor) monitor.SetVisible(_visible);
    }

    protected override void Dispose(bool disposing)
    {
        if (MaterializedView is SystemMonitorWidgetContent view) view.Dispose();
    }
}
