using DeskBox.Contracts;
using DeskBox.Controls.WidgetContents;
using DeskBox.Models;

namespace DeskBox.Services;

internal sealed class DingTalkGroupDocsWidgetContentProvider : IWidgetContentProvider
{
    public WidgetKind WidgetKind => WidgetKind.DingTalkGroupDocs;
    public bool CanCreateDetachedContent => true;

    public IWidgetContent CreateDetachedContent(
        WidgetConfig config,
        WidgetContentProviderContext context)
        => new DingTalkGroupDocsWidgetContentAdapter(config, context.LocalizationService);
}
