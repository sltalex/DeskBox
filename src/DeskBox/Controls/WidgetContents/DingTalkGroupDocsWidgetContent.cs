using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace DeskBox.Controls.WidgetContents;

public sealed class DingTalkGroupDocsWidgetContent : UserControl, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly DingTalkGroupDocumentService _service;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _search;
    private readonly StackPanel _groups = new() { Spacing = 10 };
    private readonly Button _refresh = new();
    private readonly ProgressRing _progress = new() { Width = 18, Height = 18 };
    private bool _loaded;
    private bool _disposed;

    public DingTalkGroupDocsWidgetContent(
        LocalizationService localization,
        DingTalkGroupDocumentService? service = null)
    {
        _localization = localization;
        _service = service ?? new DingTalkGroupDocumentService(new DingTalkDwsClient());
        _search = new TextBox { PlaceholderText = _localization.T("DingTalkGroupDocs.Search") };

        _refresh.Content = _localization.T("DingTalkGroupDocs.Refresh");
        _refresh.Click += Refresh_Click;
        _search.TextChanged += Search_TextChanged;
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_status);
        Grid.SetColumn(_refresh, 1);
        header.Children.Add(_refresh);

        var root = new StackPanel { Spacing = 8, Padding = new Thickness(8, 4, 8, 8) };
        root.Children.Add(header);
        root.Children.Add(_search);
        root.Children.Add(_progress);
        root.Children.Add(new ScrollViewer
        {
            Content = _groups,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 620
        });
        Content = root;
        Loaded += LoadedHandler;
        Unloaded += UnloadedHandler;
    }

    private async void LoadedHandler(object sender, RoutedEventArgs e)
    {
        if (_loaded || _disposed) return;
        _loaded = true;
        await RefreshAsync(loadOnly: true);
    }

    private void UnloadedHandler(object sender, RoutedEventArgs e) => _loaded = false;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(loadOnly: false);

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Render(_service.Documents);

    private async Task RefreshAsync(bool loadOnly)
    {
        if (_disposed) return;
        _refresh.IsEnabled = false;
        _progress.IsActive = true;
        _status.Text = _localization.T("DingTalkGroupDocs.Loading");
        try
        {
            IReadOnlyList<DingTalkGroupDocument> documents = await _service.LoadAsync();
            if (!loadOnly || documents.Count == 0)
                documents = await _service.RefreshAsync();
            Render(documents);
            _status.Text = _service.Documents.Count == 0
                ? _localization.T("DingTalkGroupDocs.Empty")
                : _localization.Format("DingTalkGroupDocs.Count", _service.Documents.Count);
        }
        catch (DingTalkDwsAuthorizationRequiredException)
        {
            _status.Text = _localization.T("DingTalkGroupDocs.SignIn");
            Render([]);
        }
        catch (DingTalkDwsMessageReadException)
        {
            _status.Text = _localization.T("DingTalkGroupDocs.Error");
            Render(_service.Documents);
        }
        catch (InvalidOperationException ex)
        {
            App.Log($"[DingTalkGroupDocs] Refresh failed: {ex.GetType().Name}");
            _status.Text = _localization.T("DingTalkGroupDocs.Error");
            Render(_service.Documents);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or HttpRequestException)
        {
            App.Log($"[DingTalkGroupDocs] Refresh failed: {ex.GetType().Name}");
            _status.Text = _localization.T("DingTalkGroupDocs.Error");
            Render(_service.Documents);
        }
        finally
        {
            _progress.IsActive = false;
            _refresh.IsEnabled = true;
        }
    }

    private void Render(IReadOnlyList<DingTalkGroupDocument> documents)
    {
        if (_disposed) return;
        string filter = _search.Text?.Trim() ?? string.Empty;
        var groups = documents
            .Where(document => string.IsNullOrWhiteSpace(filter) ||
                document.GroupName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                document.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .GroupBy(document => document.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _groups.Children.Clear();
        foreach (IGrouping<string, DingTalkGroupDocument> group in groups)
        {
            var card = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            var panel = new StackPanel { Spacing = 5 };
            panel.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            foreach (DingTalkGroupDocument document in group.OrderByDescending(item => item.LastSeenAt))
            {
                var link = new HyperlinkButton
                {
                    Content = document.Title,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                ToolTipService.SetToolTip(link, document.Url);
                link.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(document.Url));
                panel.Children.Add(link);
            }
            card.Child = panel;
            _groups.Children.Add(card);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refresh.Click -= Refresh_Click;
        _search.TextChanged -= Search_TextChanged;
        Loaded -= LoadedHandler;
        Unloaded -= UnloadedHandler;
    }
}

public sealed class DingTalkGroupDocsWidgetContentAdapter : WidgetContentAdapterBase
{
    public DingTalkGroupDocsWidgetContentAdapter(
        WidgetConfig config,
        LocalizationService localization)
        : base(config, () => new DingTalkGroupDocsWidgetContent(localization))
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (MaterializedView is DingTalkGroupDocsWidgetContent view) view.Dispose();
    }
}
