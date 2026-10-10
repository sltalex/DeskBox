using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;

namespace DeskBox.Controls.WidgetContents;

public sealed class TreeWidgetContentAdapter : WidgetContentAdapterBase
{
    public TreeWidgetContentAdapter(WidgetConfig config, LocalizationService localization)
        : base(config, () => new TreeWidgetContent(localization)) { }

    public override void OnWindowVisibilityChanged(bool visible)
    {
        if (MaterializedView is TreeWidgetContent view) view.SetVisible(visible);
    }

    protected override void Dispose(bool disposing)
    {
        if (MaterializedView is TreeWidgetContent view) view.Dispose();
    }
}
