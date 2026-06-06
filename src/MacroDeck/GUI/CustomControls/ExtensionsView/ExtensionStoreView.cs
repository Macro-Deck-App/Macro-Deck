using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Windows.Forms;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;

public partial class ExtensionStoreView : UserControl
{
    private int _pages = 0;
    private int _currentPage = 1;

    private CancellationTokenSource _cancellationTokenSource = new();

    public ExtensionStoreView()
    {
        InitializeComponent();

        Dock = DockStyle.Fill;
        DoubleBuffered = true;

        pagination.PageUpdated += Pagination_PageUpdated;
    }

    private async void Pagination_PageUpdated(int page)
    {
        _currentPage = page;
        await LoadExtensions();
    }

    private async Task LoadExtensions()
    {
        try
        {
            SetLoading(true);
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new CancellationTokenSource(); ClearExtensionsList();
            var cancellationToken = _cancellationTokenSource.Token;
            var url = $"{Constants.ExtensionStoreApiBaseUrl}/rest/v2/extensions" +
                $"?page={_currentPage}" +
                $"&pageSize=20" +
                $"&ShowPlugins={checkPlugins.Checked}" +
                $"&ShowIconPacks={checkIconPacks.Checked}";
            using var httpClient = new HttpClient();
            var pagedList = await httpClient.GetFromJsonAsync<PagedList<ApiV2ExtensionSummary>>(url, cancellationToken);
            if (pagedList == null)
            {
                return;
            }
            _pages = (int)Math.Ceiling((double)pagedList.TotalItems / pagedList.PageSize);
            _currentPage = pagedList.Page;
            UpdatePagination(cancellationToken);
            await AddExtensions(pagedList.Items ?? new List<ApiV2ExtensionSummary>(), cancellationToken);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error($"Error while loading extensions from the extension store:\n{ex.Message}");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async Task AddExtensions(List<ApiV2ExtensionSummary> extensions, CancellationToken cancellationToken)
    {
        foreach (var extension in extensions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var extensionView = new ExtensionStoreItemView(extension);
            Invoke(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    extensionsList.Controls.Add(extensionView);
                }
            });
        }

        foreach (var control in extensionsList.Controls)
        {
            if (control is not ExtensionStoreItemView extensionStoreItemView)
            {
                continue;
            }

            await extensionStoreItemView.LoadIcon(cancellationToken);
        }
    }

    private void UpdatePagination(CancellationToken cancellationToken)
    {
        Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            pagination.Pages = _pages;
            pagination.CurrentPage = _currentPage;
        });
    }

    private void ClearExtensionsList()
    {
        Invoke(() =>
        {
            extensionsList.Controls.Clear();
        });
    }

    private void SetLoading(bool state)
    {
        Invoke(() =>
        {
            extensionStoreIcon.Image = state ? Resources.Spinner : Resources.Macro_Deck_2021;
        });
    }

    private async void ExtensionStoreView_Load(object sender, EventArgs e)
    {
        await LoadExtensions();
    }

    private async void CheckPlugins_CheckedChanged(object sender, EventArgs e)
    {
        await LoadExtensions();
    }

    private async void CheckIconPacks_CheckedChanged(object sender, EventArgs e)
    {
        await LoadExtensions();
    }
}