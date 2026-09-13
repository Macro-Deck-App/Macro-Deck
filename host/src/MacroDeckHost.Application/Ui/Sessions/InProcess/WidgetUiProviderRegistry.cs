using MacroDeck.Sdk.Ui;
using System.Collections.Concurrent;
using MacroDeckHost.Application.Caching;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

public sealed class WidgetUiProviderRegistry
{
	private const string LivePrefix = "widget:";
	private const string ConfigPrefix = "widget-config:";
	private const string PreviewPrefix = "widget-preview:";
	private const string SamplePreviewPrefix = "widget-preview-sample:";
	private const string GhostSuffix = ":ghost";
	private const string UnavailablePrefix = "widget-unavailable:";

	private readonly IFolderCache _folderCache;
	private readonly IEnumerable<IBuiltInWidgetUiProvider> _providers;
	private readonly Func<IUiSessionSink> _sink;
	private readonly ILogger _logger;
	private readonly IUiProvider? _unavailable;

	private readonly ConcurrentDictionary<string, InProcessUiSessionProvider> _adapters = new(StringComparer.Ordinal);

	public WidgetUiProviderRegistry(
		IFolderCache folderCache,
		IEnumerable<IBuiltInWidgetUiProvider> providers,
		Func<IUiSessionSink> sink,
		ILogger logger,
		IUiProvider? unavailable = null)
	{
		_folderCache = folderCache;
		_providers = providers;
		_sink = sink;
		_logger = logger;
		_unavailable = unavailable;
	}

	public static string ProviderIdFor(Guid widgetId, string principal) => $"widget:{widgetId:N}:{principal}";

	public static string GhostProviderIdFor(Guid widgetId, string principal)
		=> $"widget:{widgetId:N}:{principal}:ghost";

	/// <summary>A widget's configuration editor's own id. <paramref name="widgetId" /> is part of it for
	/// the same reason a draft preview's id carries <c>variableScopeWidgetId</c>: a session is reused per
	/// provider id, so two editors open on two different widgets of the same type would otherwise share
	/// one session and each would be handed whichever widget's editor opened first.</summary>
	public static string ConfigProviderIdFor(Guid widgetId, string principal) =>
		$"widget-config:{widgetId:N}:{principal}";

	/// <summary>A draft preview's own id. <paramref name="variableScopeWidgetId" /> is part of it for the
	/// same reason <see cref="SamplePreviewProviderIdFor" /> is a separate prefix: a session is reused per
	/// provider id, so two editors previewing different widgets of the same type would otherwise share one
	/// session and resolve their variables against whichever scope opened first.</summary>
	public static string PreviewProviderIdFor(string widgetType, string principal, Guid? variableScopeWidgetId = null)
		=> variableScopeWidgetId is { } scope
			? $"widget-preview:{widgetType}:{principal}:{scope:N}"
			: $"widget-preview:{widgetType}:{principal}";

	/// <summary>A sample preview's own id. Separate from <see cref="PreviewProviderIdFor" /> because a
	/// session is reused per provider id: the picker's sample and an editor's draft preview of the same
	/// type would otherwise be handed each other's tree.</summary>
	public static string SamplePreviewProviderIdFor(string widgetType, string principal)
		=> $"widget-preview-sample:{widgetType}:{principal}";

	// Principal-free: a hardware press opens under a fresh principal every time, and a per-principal id
	// would leave one cached adapter behind per press.
	public static string UnavailableProviderIdFor(Guid widgetId, bool ghost)
		=> ghost ? $"{UnavailablePrefix}ghost:{widgetId:N}" : $"{UnavailablePrefix}live:{widgetId:N}";

	public static string UnavailableConfigProviderIdFor(Guid widgetId) => $"{UnavailablePrefix}config:{widgetId:N}";

	public static string UnavailablePreviewProviderIdFor(string widgetType) => $"{UnavailablePrefix}preview:{widgetType}";

	public IUiSessionProvider? Resolve(string providerId)
	{
		// Checked first and by prefix alone: a qualified type in a preview id contains "::", which the
		// first-colon parsing below would cut apart.
		if (providerId.StartsWith(UnavailablePrefix, StringComparison.Ordinal))
		{
			return _unavailable is null ? null : Adapter(providerId, _unavailable);
		}

		if (TryParseLive(providerId, out var widgetId))
		{
			var type = FindWidgetType(widgetId);
			return type is null ? null : ResolveAdapter(providerId, type);
		}

		// The widget was already looked up before this id was minted (ConfigUiSessionOpener), so this
		// only matters for the race of it being deleted in between - resolved the same way a live
		// widget's own id is, by falling back to "no provider" rather than guessing.
		if (TryParseConfig(providerId, out var configWidgetId))
		{
			var type = FindWidgetType(configWidgetId);
			return type is null ? null : ResolveAdapter(providerId, type);
		}

		// A preview names its type directly, and an id no provider serves simply resolves to nothing -
		// the same answer a live widget of an unregistered type gives.
		if (TryParsePreview(providerId, out var widgetTypeName))
		{
			return ResolveAdapter(providerId, widgetTypeName);
		}

		return null;
	}

	/// <summary>
	/// Disposes and forgets every adapter cached for <paramref name="widgetId" /> - its live tile, its
	/// ghost, and its config editor, across every principal that ever opened one. Nothing else ever removes
	/// an entry from <see cref="_adapters" />, so without this a deleted widget's adapters would sit in the
	/// dictionary for as long as the host runs.
	/// </summary>
	public void EvictWidget(Guid widgetId)
	{
		var liveOrGhostPrefix = LivePrefix + widgetId.ToString("N") + ":";
		var configPrefix = ConfigPrefix + widgetId.ToString("N") + ":";
		string[] placeholders =
		[
			UnavailableProviderIdFor(widgetId, ghost: false),
			UnavailableProviderIdFor(widgetId, ghost: true),
			UnavailableConfigProviderIdFor(widgetId)
		];

		foreach (var key in _adapters.Keys)
		{
			if (!key.StartsWith(liveOrGhostPrefix, StringComparison.Ordinal) &&
				!key.StartsWith(configPrefix, StringComparison.Ordinal) &&
				!placeholders.Contains(key, StringComparer.Ordinal))
			{
				continue;
			}

			if (_adapters.TryRemove(key, out var adapter))
			{
				_ = DisposeEvictedAsync(adapter);
			}
		}
	}

	private async Task DisposeEvictedAsync(InProcessUiSessionProvider adapter)
	{
		try
		{
			await adapter.DisposeAsync();
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Failed to dispose a widget UI adapter evicted after its widget was deleted");
		}
	}

	private InProcessUiSessionProvider? ResolveAdapter(string providerId, string widgetType)
	{
		var provider = _providers.FirstOrDefault(candidate =>
			string.Equals(candidate.WidgetTypeId, widgetType, StringComparison.Ordinal));
		return provider is null ? null : Adapter(providerId, provider);
	}

	private InProcessUiSessionProvider Adapter(string providerId, IUiProvider provider)
		=> _adapters.GetOrAdd(providerId,
			static (id, state) => new InProcessUiSessionProvider(id, state.Provider, state.Sink(), state.Logger),
			(Provider: provider, Sink: _sink, Logger: _logger));

	private string? FindWidgetType(Guid widgetId)
		=> _folderCache.GetAllFolders()
			.SelectMany(folder => folder.Widgets)
			.FirstOrDefault(widget => widget.Id == widgetId)
			?.Type;

	private static bool TryParseLive(string providerId, out Guid widgetId)
	{
		widgetId = Guid.Empty;

		if (!providerId.StartsWith(LivePrefix, StringComparison.Ordinal))
		{
			return false;
		}

		var rest = providerId[LivePrefix.Length..];
		if (rest.EndsWith(GhostSuffix, StringComparison.Ordinal))
		{
			rest = rest[..^GhostSuffix.Length];
		}

		var separator = rest.IndexOf(':');
		var widgetIdPart = separator < 0 ? rest : rest[..separator];

		return Guid.TryParseExact(widgetIdPart, "N", out widgetId);
	}

	private static bool TryParseConfig(string providerId, out Guid widgetId)
	{
		widgetId = Guid.Empty;

		if (!providerId.StartsWith(ConfigPrefix, StringComparison.Ordinal))
		{
			return false;
		}

		var rest = providerId[ConfigPrefix.Length..];
		var separator = rest.IndexOf(':');
		var widgetIdPart = separator < 0 ? rest : rest[..separator];

		return Guid.TryParseExact(widgetIdPart, "N", out widgetId);
	}

	private static bool TryParsePreview(string providerId, out string widgetType)
	{
		widgetType = string.Empty;

		var prefix = providerId.StartsWith(SamplePreviewPrefix, StringComparison.Ordinal)
			? SamplePreviewPrefix
			: PreviewPrefix;

		if (!providerId.StartsWith(prefix, StringComparison.Ordinal))
		{
			return false;
		}

		var rest = providerId[prefix.Length..];
		var separator = rest.IndexOf(':');
		widgetType = separator < 0 ? rest : rest[..separator];

		return widgetType.Length > 0;
	}
}
