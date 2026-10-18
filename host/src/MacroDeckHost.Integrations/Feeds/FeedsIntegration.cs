using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations.Feeds.Actions;
using MacroDeckHost.Integrations.Feeds.Configuration;
using MacroDeckHost.Integrations.Feeds.Connections;
using MacroDeckHost.Integrations.Feeds.Events;
using MacroDeckHost.Integrations.Feeds.Protocol;
using MacroDeckHost.Integrations.Feeds.State;
using MacroDeckHost.Integrations.Feeds.Variables;
using MacroDeckHost.Integrations.Feeds.Widgets;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Feeds;
using WidgetStrings = MacroDeckHost.Localization.AppStrings.Widgets.Feeds;

namespace MacroDeckHost.Integrations.Feeds;

[MacroDeckIntegration]
public sealed partial class FeedsIntegration
	: IIntegration,
		IConfigFlowProvider,
		IConfigEntryLifecycle,
		IVariableProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IWidgetTypeProvider,
		IUiProvider,
		IIntegrationIconProvider,
		IDisposable
{
	public const string IntegrationId = FeedWidgetTypes.OwnerId;

	private static readonly ILogger _logger = IntegrationLog.For<FeedsIntegration>(IntegrationId);

	private static readonly byte[] _icon = LoadIcon();

	private readonly IFeedFetcher _fetcher;
	private readonly FeedClient? _ownedClient;
	private readonly TimeProvider _time;
	private readonly bool _startPolling;
	private readonly FeedHub _hub = new();
	private readonly FeedImageCache _images;
	private readonly FeedsUiProvider _ui;
	private readonly SemaphoreSlim _reloadGate = new(1, 1);

	private IIntegrationContext? _context;
	private FeedRuntime[] _runtimes = [];

	public FeedsIntegration()
		: this(null, TimeProvider.System, true)
	{
	}

	internal FeedsIntegration(IFeedFetcher? fetcher, TimeProvider time, bool startPolling)
	{
		_time = time;
		_startPolling = startPolling;
		if (fetcher is null)
		{
			_ownedClient = new FeedClient(time);
			_fetcher = _ownedClient;
		}
		else
		{
			_fetcher = fetcher;
		}

		_images = new FeedImageCache(_fetcher, () => _context?.UiResources);
		_ui = new FeedsUiProvider(_hub, _images, () => _context?.UiResources, time);
		Actions = [new FeedRefreshAction(this, time)];
	}

	public string Id => IntegrationId;

	public LocalizedText Name => Strings.Name();

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public bool AllowsMultipleConfigurations => true;

	public bool VariablesDependOnConfiguration => true;

	public IReadOnlyList<EventDefinition> EventDefinitions => FeedEventDefinitions.All;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _ui.Surfaces;

	public IReadOnlyList<VariableDefinition> Variables
	{
		get
		{
			var runtimes = Runtimes;
			if (runtimes.Count == 0)
			{
				return [];
			}

			return
			[
				.. FeedVariables.Aggregates(),
				.. runtimes.SelectMany(runtime => FeedVariables.Declare(runtime.Settings.VariableKey,
					runtime.EntryId,
					runtime.Settings.Title))
			];
		}
	}

	public IReadOnlyList<VariableDefinition> DeclaredVariables
		=> Variables is { Count: > 0 } provided ? provided : FeedVariables.Templates;

	internal IReadOnlyList<FeedRuntime> Runtimes => Volatile.Read(ref _runtimes);

	internal IFeedSource Feeds => _hub;

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return new FeedConfigFlow(_fetcher);
	}

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		return _ui.CreateSessionAsync(request, cancellationToken);
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		IntegrationHttp.Use(context.Http);
		_context = context;
		_hub.UseOpener((entryId, key, widgetId) => _ = OpenAsync(entryId, key, widgetId));
		await ReloadConfigurationsAsync(CancellationToken.None).ConfigureAwait(false);
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		_hub.UseOpener(null);
		foreach (var runtime in Interlocked.Exchange(ref _runtimes, []))
		{
			await runtime.DisposeAsync().ConfigureAwait(false);
		}

		_hub.Replace([]);
	}

	public void Dispose()
	{
		foreach (var runtime in Interlocked.Exchange(ref _runtimes, []))
		{
			runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}

		_images.Dispose();
		_ownedClient?.Dispose();
		_reloadGate.Dispose();
	}

	public Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		return RegisterWidgetTypesAsync(context, cancellationToken);
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()
	{
		return [WidgetType(FeedWidgetKind.List), WidgetType(FeedWidgetKind.Featured)];
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(FeedVariables.Read(localId, _hub.Feeds));
	}

	public Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		return Task.FromResult(context.ParameterName == FeedEventPayload.Feed
			? FeedOptions()
			: new DynamicOptionsResult { Options = [] });
	}

	internal DynamicOptionsResult FeedOptions()
	{
		return new DynamicOptionsResult
		{
			Options =
			[
				.. _hub.Feeds.Select(feed => new ActionParameterOption
					{ Value = feed.EntryId.ToString("D"), Label = feed.Title })
			],
			CacheSeconds = 0
		};
	}

	internal async Task ReloadConfigurationsAsync(CancellationToken cancellationToken)
	{
		if (_context is not { } context)
		{
			return;
		}

		await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var entries = await context.Config.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
			var previous = Runtimes;
			var desired = new List<FeedSettings>(entries.Count);
			var usedKeys = new HashSet<string>(StringComparer.Ordinal) { FeedVariables.ReservedKey };

			foreach (var entry in entries)
			{
				var settings = await ReadSettingsAsync(context.Config, entry, usedKeys, cancellationToken)
					.ConfigureAwait(false);
				if (settings is null)
				{
					_logger.Warning("Feed config entry {EntryId} has no valid address; skipping", entry.Id);
					continue;
				}

				desired.Add(settings);
			}

			// A replaced runtime stops before its successor reads the stored state, so no late write is lost.
			var remaining = entries.Select(entry => entry.Id).ToHashSet();
			foreach (var runtime in previous.Where(runtime => !desired.Contains(runtime.Settings)))
			{
				await runtime.DisposeAsync().ConfigureAwait(false);
				if (!remaining.Contains(runtime.EntryId))
				{
					await runtime.DeleteStateAsync(cancellationToken).ConfigureAwait(false);
				}
			}

			var next = new List<FeedRuntime>(desired.Count);
			var created = new List<FeedRuntime>();
			foreach (var settings in desired)
			{
				if (previous.FirstOrDefault(runtime => runtime.Settings == settings) is { } unchanged)
				{
					next.Add(unchanged);
					continue;
				}

				var runtime = new FeedRuntime(settings, _fetcher, context.Storage, _time, OnRefreshed);
				await runtime.LoadAsync(cancellationToken).ConfigureAwait(false);
				next.Add(runtime);
				created.Add(runtime);
			}

			Volatile.Write(ref _runtimes, [.. next]);
			_hub.Replace([.. next.Select(runtime => runtime.Snapshot)]);

			if (_startPolling)
			{
				foreach (var runtime in created)
				{
					runtime.Start();
				}
			}

			NotifyDeclarationsChanged();
			RequestVariableRefresh();
		}
		finally
		{
			_reloadGate.Release();
		}
	}

	internal Task RefreshAsync(Guid entryId, CancellationToken cancellationToken = default)
	{
		return Runtimes.FirstOrDefault(runtime => runtime.EntryId == entryId) is { } runtime
			? runtime.RefreshAsync(cancellationToken)
			: Task.CompletedTask;
	}

	internal async Task OpenAsync(Guid entryId, string itemKey, Guid? widgetId)
	{
		if (Runtimes.FirstOrDefault(candidate => candidate.EntryId == entryId) is not { } runtime
			|| runtime.Snapshot.Find(itemKey) is not { } item)
		{
			return;
		}

		_context?.Events.Publish(FeedEventIds.ItemOpened,
			FeedEventDefinitions.ItemOpened(runtime.Snapshot, item, widgetId).Payload);

		try
		{
			if (!await runtime.MarkReadAsync(itemKey, CancellationToken.None).ConfigureAwait(false))
			{
				return;
			}
		}
		catch (Exception exception) when (exception is ObjectDisposedException or OperationCanceledException)
		{
			return;
		}

		if (Runtimes.Contains(runtime))
		{
			_hub.Update(runtime.Snapshot);
			RequestVariableRefresh();
		}
	}

	private void OnRefreshed(FeedRuntime runtime, FeedSnapshot previous, FeedRefreshOutcome outcome)
	{
		if (!Runtimes.Contains(runtime))
		{
			return;
		}

		var snapshot = runtime.Snapshot;
		_hub.Update(snapshot);

		if (_context is { } context)
		{
			foreach (var item in outcome.NewItems)
			{
				Publish(context, FeedEventDefinitions.NewItem(snapshot, item));
			}

			if (outcome.ItemsChanged)
			{
				Publish(context, FeedEventDefinitions.Updated(snapshot));
			}

			if (outcome.FailureChanged && outcome.Document.Error is { } error)
			{
				Publish(context, FeedEventDefinitions.UpdateFailed(snapshot, error));
			}
		}

		RequestVariableRefresh();
	}

	private static void Publish(IIntegrationContext context, FeedEvent feedEvent)
	{
		context.Events.Publish(feedEvent.Id, feedEvent.Payload);
	}

	private async Task RegisterWidgetTypesAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken)
	{
		foreach (var widgetType in GetWidgetTypes())
		{
			await context.RegisterWidgetTypeAsync(widgetType, cancellationToken).ConfigureAwait(false);
		}
	}

	private static WidgetTypeDescriptor WidgetType(FeedWidgetKind kind)
	{
		var list = kind == FeedWidgetKind.List;
		return new WidgetTypeDescriptor(list ? FeedWidgetTypes.ListLocalId : FeedWidgetTypes.FeaturedLocalId,
			list ? WidgetStrings.FeedList.Name() : WidgetStrings.Featured.Name(),
			list ? WidgetStrings.FeedList.Description() : WidgetStrings.Featured.Description(),
			FeedWidgetTypes.DefaultData(kind),
			FeedWidgetTypes.DataSchema(),
			true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor],
			SupportsFlows = true
		};
	}

	private void RequestVariableRefresh()
	{
		if (_context is { } context)
		{
			_ = context.Variables.RequestRefreshAsync();
		}
	}

	private void NotifyDeclarationsChanged()
	{
		if (_context is { } context)
		{
			_ = context.Variables.NotifyDeclarationsChangedAsync();
		}
	}

	private static async Task<FeedSettings?> ReadSettingsAsync(
		IIntegrationConfig config,
		ConfigEntrySnapshot entry,
		HashSet<string> usedKeys,
		CancellationToken cancellationToken)
	{
		Task<string?> Read(string name)
		{
			return config.GetStringAsync(entry.Id, name, cancellationToken);
		}

		var url = FeedSettings.TryParseUrl(await Read(FeedConfigKeys.Url).ConfigureAwait(false));
		if (url is null)
		{
			return null;
		}

		var storedKey = await Read(FeedConfigKeys.VariableKey).ConfigureAwait(false);
		var key = storedKey is { Length: > 0 } && usedKeys.Add(storedKey)
			? storedKey
			: FeedVariables.UniqueKey(entry.Title, usedKeys);
		var group = await Read(FeedConfigKeys.Group).ConfigureAwait(false);
		var refresh = await Read(FeedConfigKeys.RefreshMinutes).ConfigureAwait(false);
		var include = await Read(FeedConfigKeys.IncludeKeywords).ConfigureAwait(false);
		var exclude = await Read(FeedConfigKeys.ExcludeKeywords).ConfigureAwait(false);
		var categories = await Read(FeedConfigKeys.Categories).ConfigureAwait(false);
		var allowLocal = await Read(FeedConfigKeys.AllowLocalNetwork).ConfigureAwait(false);

		return new FeedSettings(entry.Id, entry.Title, url, key)
		{
			Group = FeedSettings.ReadText(group),
			RefreshMinutes = FeedSettings.ClampRefresh(FeedSettings.ReadNumber(refresh)),
			Filter = FeedFilter.Create(include, exclude, categories),
			AllowLocalNetwork = FeedSettings.ReadBool(allowLocal)
		};
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(FeedsIntegration).Assembly;
		using var stream = assembly.GetManifestResourceStream(
			$"{typeof(FeedsIntegration).Namespace}.Icons.{FeedsUiProvider.IconFolder}.{FeedsUiProvider.Icon}.svg")!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
