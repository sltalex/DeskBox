using DeskBox.Contracts;
using DeskBox.Controls.WidgetContents;
using DeskBox.Models;

namespace DeskBox.Services;

internal sealed class TreeWidgetContentProvider : IWidgetContentProvider
{
    public WidgetKind WidgetKind => WidgetKind.Tree;
    public bool CanCreateDetachedContent => true;
    public IWidgetContent CreateDetachedContent(WidgetConfig config, WidgetContentProviderContext context)
        => new TreeWidgetContentAdapter(config, context.LocalizationService);
}
