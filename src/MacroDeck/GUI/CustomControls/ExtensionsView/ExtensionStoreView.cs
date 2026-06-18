using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using Serilog;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;

public partial class ExtensionStoreView : UserControl
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ExtensionStoreView));

    private int _pages;
    private int _currentPage = 1;
    private string _searchString = "";

    private readonly System.Windows.Forms.Timer _searchDebounce;

    private CancellationTokenSource _cancellationTokenSource = new();

    public ExtensionStoreView()
    {
        InitializeComponent();

        Dock = DockStyle.Fill;
        DoubleBuffered = true;

        _searchDebounce = new System.Windows.Forms.Timer { Interval = 350 };
        _searchDebounce.Tick += SearchDebounce_Tick;

        pagination.PageUpdated += Pagination_PageUpdated;
    }

    private async void Pagination_PageUpdated(int page)
    {
        _currentPage = page;
        await LoadExtensions();
    }

    private void TxtSearch_TextChanged(object sender, EventArgs e)
    {
        // Debounce so we don't fire a request on every keystroke.
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void SearchDebounce_Tick(object sender, EventArgs e)
    {
        _searchDebounce.Stop();
        var search = txtSearch.Text.Trim();
        if (search == _searchString)
        {
            return;
        }

        _searchString = search;
        _currentPage = 1;
        await LoadExtensions();
    }

    private async Task LoadExtensions()
    {
        try
        {
            extensionsGrid.Loading = true;
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;
            var url = $"{Constants.ExtensionStoreApiBaseUrl}/rest/v2/extensions" +
                $"?page={_currentPage}" +
                $"&pageSize=20" +
                $"&ShowPlugins={checkPlugins.Checked}" +
                $"&ShowIconPacks={checkIconPacks.Checked}";
            if (!string.IsNullOrWhiteSpace(_searchString))
            {
                url += $"&searchString={Uri.EscapeDataString(_searchString)}";
            }

            var pagedList =
                await FileDownloader.GetFromJsonAsync<PagedList<ApiV2ExtensionSummary>>(url, cancellationToken);
            if (pagedList == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _pages = (int)Math.Ceiling((double)pagedList.TotalItems / pagedList.PageSize);
            _currentPage = pagedList.Page;
            pagination.Pages = Math.Max(1, _pages);
            pagination.CurrentPage = _currentPage;

            var cards = (pagedList.Items ?? new List<ApiV2ExtensionSummary>())
                .Select(BuildCard)
                .ToList();
            extensionsGrid.SetCards(cards);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while loading extensions from the extension store");
        }
        finally
        {
            extensionsGrid.Loading = false;
        }
    }

    private ExtensionCard BuildCard(ApiV2ExtensionSummary extension)
    {
        var card = new ExtensionCard
        {
            Title = extension.Name,
            Subtitle = extension.Author,
            Description = !string.IsNullOrWhiteSpace(extension.Description)
                ? extension.Description
                : "No description provided. Check the repository for more information.",
            SearchText = $"{extension.Name} {extension.Author}".ToLowerInvariant(),
            DisposableIcon = true,
            LoadIconAsync = ct => LoadStoreIconAsync(extension.PackageId, ct)
        };

        switch (extension.ExtensionType)
        {
            case ExtensionType.Plugin:
                card.BadgeText = LanguageManager.Strings.Plugin;
                card.BadgeColor = Color.FromArgb(0, 95, 173);
                break;
            case ExtensionType.IconPack:
                card.BadgeText = LanguageManager.Strings.IconPack;
                card.BadgeColor = Color.FromArgb(0, 173, 14);
                break;
        }

        var installed = IsInstalled(extension);
        card.Actions.Add(new CardAction
        {
            Label = installed ? LanguageManager.Strings.Installed : LanguageManager.Strings.Install,
            Enabled = !installed,
            Style = CardActionStyle.Primary,
            OnClick = () => Install(extension)
        });

        if (!string.IsNullOrWhiteSpace(extension.GitHubRepository))
        {
            card.Actions.Add(new CardAction
            {
                Label = "Repository",
                Style = CardActionStyle.Link,
                OnClick = () => OpenRepository(extension.GitHubRepository)
            });
        }

        return card;
    }

    private static bool IsInstalled(ApiV2ExtensionSummary extension)
    {
        return extension.ExtensionType switch
        {
            ExtensionType.Plugin => extension.PackageId != null && PluginManager.IsInstalled(extension.Name),
            ExtensionType.IconPack => IconManager.IconPacks.Exists(x =>
                x.PackageId == extension.PackageId && x.ExtensionStoreManaged),
            _ => false
        };
    }

    private async void Install(ApiV2ExtensionSummary extension)
    {
        if (extension.PackageId is null)
        {
            return;
        }

        // InstallById shows a modal download dialog and returns once it closes.
        ExtensionStoreHelper.InstallById(extension.PackageId);
        await LoadExtensions();
    }

    private static void OpenRepository(string repository)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(repository) { UseShellExecute = true }
        };
        process.Start();
    }

    private static async Task<Image> LoadStoreIconAsync(string packageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(packageId))
        {
            return null;
        }

        byte[] bytes;
        if (!ExtensionIconCache.TryGet(packageId, out bytes))
        {
            var iconUrl = $"{Constants.ExtensionStoreApiBaseUrl}/rest/v2/extensions/icon/{packageId}";
            using var stream = await FileDownloader.DownloadImageAsync(iconUrl, cancellationToken);
            bytes = stream.ToArray();
            ExtensionIconCache.Set(packageId, bytes);
        }

        using var ms = new MemoryStream(bytes);
        using var source = Image.FromStream(ms);
        var thumbnail = new Bitmap(50, 50);
        using var g = Graphics.FromImage(thumbnail);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, 50, 50);
        return thumbnail;
    }

    private async void ExtensionStoreView_Load(object sender, EventArgs e)
    {
        await LoadExtensions();
    }

    private async void CheckPlugins_CheckedChanged(object sender, EventArgs e)
    {
        _currentPage = 1;
        await LoadExtensions();
    }

    private async void CheckIconPacks_CheckedChanged(object sender, EventArgs e)
    {
        _currentPage = 1;
        await LoadExtensions();
    }
}
