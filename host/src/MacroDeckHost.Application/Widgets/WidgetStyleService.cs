using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using Serilog;

namespace MacroDeckHost.Application.Widgets;

public enum WidgetStylePasteOutcome
{
	Applied,
	InvalidStyle,
	WidgetNotFound,
	Rejected,
	WriteFailed
}

public interface IWidgetStyleService
{
	string? Copy(Guid widgetId);

	Task<WidgetStylePasteOutcome> PasteAsync(
		string? style,
		IReadOnlyCollection<Guid> widgetIds,
		CancellationToken cancellationToken = default);
}

public sealed class WidgetStyleService : IWidgetStyleService
{
	private static readonly string[] _currentState = [WidgetStates.Current];

	private readonly IFolderCache _folderCache;
	private readonly IWidgetService _widgetService;
	private readonly IWidgetDataWriteLock _writeLock;
	private readonly WidgetDerivedStateStore _derivedStates;
	private readonly IWidgetDataSchemaProvider _schemas;
	private readonly WidgetProviderAppearance _providerAppearance;
	private readonly ILogger _logger;

	public WidgetStyleService(
		IFolderCache folderCache,
		IWidgetService widgetService,
		IWidgetDataWriteLock writeLock,
		WidgetDerivedStateStore derivedStates,
		IWidgetTypeRegistry widgetTypes,
		IWidgetDataSchemaProvider schemas,
		WidgetAppearanceSchemaProbe schemaProbe,
		ILogger logger)
	{
		_folderCache = folderCache;
		_widgetService = widgetService;
		_writeLock = writeLock;
		_derivedStates = derivedStates;
		_schemas = schemas;
		_providerAppearance = new WidgetProviderAppearance(widgetTypes, schemas, schemaProbe);
		_logger = logger.ForContext<WidgetStyleService>();
	}

	public string? Copy(Guid widgetId)
	{
		var widget = FindWidget(widgetId);
		if (widget is null)
		{
			return null;
		}

		var data = WidgetAppearanceJson.ParseDataBag(widget.Data);
		if (widget.Type == WidgetTypeIds.ActionButton)
		{
			ActionButtonStateJson.Normalize(data);
		}

		var shown = WidgetAppearanceJson.ResolveStates(data,
			widget.Type,
			_currentState,
			_derivedStates.TryGet(widget.Id));
		return WidgetStyleJson.Extract(widget.Type, data, shown.Count > 0 ? shown[0] : null, KeysFor(widget)).ToJson();
	}

	public async Task<WidgetStylePasteOutcome> PasteAsync(
		string? style,
		IReadOnlyCollection<Guid> widgetIds,
		CancellationToken cancellationToken = default)
	{
		if (!WidgetStyleJson.TryParse(style, out var parsed))
		{
			return WidgetStylePasteOutcome.InvalidStyle;
		}

		// The per-widget lock is not re-entrant: a repeated id would wait on itself forever.
		var ids = widgetIds.Distinct().Order().ToList();
		var locks = new List<IDisposable>(ids.Count);
		try
		{
			foreach (var id in ids)
			{
				locks.Add(await _writeLock.AcquireAsync(id, cancellationToken));
			}

			var pending = new List<(WidgetEntity Widget, string Data)>();
			foreach (var id in ids)
			{
				var widget = FindWidget(id);
				if (widget is null)
				{
					return WidgetStylePasteOutcome.WidgetNotFound;
				}

				var data = WidgetAppearanceJson.ParseDataBag(widget.Data);
				if (widget.Type == WidgetTypeIds.ActionButton)
				{
					ActionButtonStateJson.Normalize(data);
				}

				var baseline = data.DeepClone().AsObject();
				if (!WidgetStyleJson.Apply(widget.Type, data, parsed, KeysFor(widget)))
				{
					continue;
				}

				// A provider type takes the border whatever its schema says, as Set Border always did.
				if (WidgetStyleJson.BuiltInKeys(widget.Type) is null)
				{
					WidgetStyleJson.Apply(widget.Type, baseline, parsed, WidgetStyleJson.ProviderKeys);
				}

				if (_schemas.TryGet(widget.Type, out var schema)
					&& WidgetAppearanceSchemaProbe.AddsProblems(schema, baseline, data))
				{
					_logger.Warning("Style paste skipped: the data schema of widget {WidgetId} rejects it", widget.Id);
					return WidgetStylePasteOutcome.Rejected;
				}

				pending.Add((widget, data.ToJsonString()));
			}

			var outcome = WidgetStylePasteOutcome.Applied;
			foreach (var (widget, data) in pending)
			{
				widget.Data = data;
				var result = await _widgetService.Update(widget);
				if (!result.Success)
				{
					_logger.Warning("Failed to write the pasted style on widget {WidgetId}: {Error}",
						widget.Id,
						result.ErrorMessage ?? result.Error.ToString());
					outcome = WidgetStylePasteOutcome.WriteFailed;
				}
			}

			return outcome;
		}
		finally
		{
			foreach (var held in locks)
			{
				held.Dispose();
			}
		}
	}

	private IReadOnlyList<string> KeysFor(WidgetEntity widget)
	{
		if (WidgetStyleJson.BuiltInKeys(widget.Type) is { } builtIn)
		{
			return builtIn;
		}

		return _providerAppearance.Supported(widget)?.Contains(WidgetAppearanceProperty.BackgroundColor) == true
			? WidgetStyleJson.ProviderKeysWithBackground
			: WidgetStyleJson.ProviderKeys;
	}

	private WidgetEntity? FindWidget(Guid id)
	{
		return _folderCache.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(w => w.Id == id);
	}
}
