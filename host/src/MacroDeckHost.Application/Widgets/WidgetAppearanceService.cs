using System.Text.Json.Nodes;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Sdk.Widgets;
using Serilog;

namespace MacroDeckHost.Application.Widgets;

public interface IWidgetAppearanceService
{
	IReadOnlyList<WidgetTargetInfo> GetWidgets();

	bool Exists(string widgetId);

	Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default);

	async Task<WidgetAppearanceOutcome> ApplyWithOutcomeAsync(
		WidgetAppearanceRequest request,
		CancellationToken cancellationToken = default)
		=> await ApplyAsync(request, cancellationToken) ? WidgetAppearanceOutcome.Changed : WidgetAppearanceOutcome.Unchanged;
}

public sealed class WidgetAppearanceService : IWidgetAppearanceService
{
	private readonly IFolderCache _folderCache;
	private readonly IProfileCache _profileCache;
	private readonly IWidgetService _widgetService;
	private readonly IWidgetDataWriteLock _writeLock;
	private readonly WidgetDerivedStateStore _derivedStates;
	private readonly IWidgetTypeRegistry _widgetTypes;
	private readonly IWidgetDataSchemaProvider _schemas;
	private readonly WidgetAppearanceSchemaProbe _schemaProbe;
	private readonly ILogger _logger;

	public WidgetAppearanceService(
		IFolderCache folderCache,
		IProfileCache profileCache,
		IWidgetService widgetService,
		IWidgetDataWriteLock writeLock,
		WidgetDerivedStateStore derivedStates,
		IWidgetTypeRegistry widgetTypes,
		IWidgetDataSchemaProvider schemas,
		WidgetAppearanceSchemaProbe schemaProbe,
		ILogger logger)
	{
		_folderCache = folderCache;
		_profileCache = profileCache;
		_widgetService = widgetService;
		_writeLock = writeLock;
		_derivedStates = derivedStates;
		_widgetTypes = widgetTypes;
		_schemas = schemas;
		_schemaProbe = schemaProbe;
		_logger = logger.ForContext<WidgetAppearanceService>();
	}

	public IReadOnlyList<WidgetTargetInfo> GetWidgets()
	{
		var result = new List<WidgetTargetInfo>();
		foreach (var folder in _folderCache.GetAllFolders())
		{
			var profileName = _profileCache.GetById(folder.ProfileId)?.Name;
			var location = string.IsNullOrWhiteSpace(profileName) ? folder.Name : $"{profileName} / {folder.Name}";

			foreach (var widget in folder.Widgets)
			{
				var data = WidgetAppearanceJson.ParseDataBag(widget.Data);
				var (states, currentStateId) = ResolveTargetStates(widget, data);
				var providerSupported = ProviderSupported(widget);

				result.Add(new WidgetTargetInfo
				{
					Id = widget.Id.ToString(),
					Label = DescribeWidget(widget, data, states.Count > 0 ? states[0].Id : null, providerSupported),
					Location = location,
					Type = widget.Type.ToString(),
#pragma warning disable CS0618 // Kept for a plugin still reading the collapsed on/off flag; States is the replacement.
					HasOnOffStates = states.Count >= 2,
#pragma warning restore CS0618
					States = states,
					CurrentStateId = currentStateId,
					AppearanceProperties = providerSupported ?? WidgetAppearanceJson.SupportedProperties(widget.Type),
					HasActiveIconProvider = HasActiveIconProvider(widget, data)
				});
			}
		}

		return result;
	}

	public bool Exists(string widgetId) => Guid.TryParse(widgetId, out var id) && FindWidget(id) is not null;

	public async Task<bool> ApplyAsync(
		WidgetAppearanceRequest request,
		CancellationToken cancellationToken = default)
		=> await ApplyWithOutcomeAsync(request, cancellationToken) == WidgetAppearanceOutcome.Changed;

	public async Task<WidgetAppearanceOutcome> ApplyWithOutcomeAsync(
		WidgetAppearanceRequest request,
		CancellationToken cancellationToken = default)
	{
		if (request.Patch.IsEmpty && request.ClearProperties.Count == 0)
		{
			return WidgetAppearanceOutcome.Unchanged;
		}

		var widget = Locate(request.WidgetId);
		if (widget is null)
		{
			return WidgetAppearanceOutcome.Unchanged;
		}

		using var _ = await _writeLock.AcquireAsync(widget.Id, cancellationToken);

		var data = WidgetAppearanceJson.ParseDataBag(widget.Data);
		if (widget.Type == WidgetTypeIds.ActionButton)
		{
			ActionButtonStateJson.Normalize(data);
		}

		// A mapping/provider-bound button's displayed state lives in the derived store, not in its
		// data (issue #312).
		var states = WidgetAppearanceJson.ResolveStates(data,
			widget.Type,
			request.ResolveStateIds(),
			_derivedStates.TryGet(widget.Id));

		var (patch, clearProperties) = HasActiveIconProvider(widget, data)
			? WithoutIcon(request.Patch, request.ClearProperties)
			: (request.Patch, request.ClearProperties);

		var providerSupported = ProviderSupported(widget);
		var before = providerSupported is null ? null : data.DeepClone().AsObject();

		var changed = WidgetAppearanceJson.Apply(data, widget.Type, patch, states, providerSupported);
		foreach (var property in clearProperties)
		{
			foreach (var state in states)
			{
				changed |= WidgetAppearanceJson.ClearProperty(data, widget.Type, property, state, providerSupported);
			}
		}

		if (!changed)
		{
			return WidgetAppearanceOutcome.Unchanged;
		}

		if (before is not null && AddsSchemaProblems(widget.Type, before, patch, states, data))
		{
			_logger.Warning("Widget appearance change on widget {WidgetId} skipped: its type's data schema rejects it",
				widget.Id);
			return WidgetAppearanceOutcome.Rejected;
		}

		return await Write(widget, data) ? WidgetAppearanceOutcome.Changed : WidgetAppearanceOutcome.WriteFailed;
	}

	private List<WidgetAppearanceProperty>? ProviderSupported(WidgetEntity widget)
	{
		if (!_widgetTypes.TryResolve(widget.Type, out var entry) || entry.IsBuiltIn)
		{
			return null;
		}

		var supported = new List<WidgetAppearanceProperty>
		{
			WidgetAppearanceProperty.Border, WidgetAppearanceProperty.BorderColor
		};

		var declared = entry.Descriptor.AppearanceProperties ?? [];
		var hasSchema = _schemas.TryGet(widget.Type, out var schema);
		foreach (var property in WidgetAppearanceJson.ProviderOptionalProperties)
		{
			if (declared.Contains(property) &&
				(!hasSchema || _schemaProbe.Accepts(widget.Type, schema!, widget.Data ?? string.Empty, property)))
			{
				supported.Add(property);
			}
		}

		return supported;
	}

	// Border writes keep their behaviour from before provider types could declare more, schema or not, so
	// only what the rest of the patch adds on top of the border change is held against the schema.
	private bool AddsSchemaProblems(
		string type,
		JsonObject before,
		WidgetAppearancePatch patch,
		IReadOnlyCollection<string> states,
		JsonObject after)
	{
		if (!_schemas.TryGet(type, out var schema))
		{
			return false;
		}

		var borderOnly = before.DeepClone().AsObject();
		WidgetAppearanceJson.Apply(borderOnly,
			type,
			new WidgetAppearancePatch { BorderStyle = patch.BorderStyle, BorderColor = patch.BorderColor },
			states,
			[]);
		return WidgetAppearanceSchemaProbe.AddsProblems(schema, borderOnly, after);
	}

	/// <summary>
	/// Whether an icon-provider action is currently assigned - a structural check on the stored data,
	/// exactly as <c>ActionButtonStateService</c> refuses a state write purely on <c>stateProvider</c>'s
	/// presence rather than resolving whether the block still answers. Deliberately does not resolve the
	/// guard chain (block found/enabled, integration registered/enabled, action still an icon provider):
	/// the assignment is what makes the icon not this call's to set, independent of whether the provider
	/// can answer right now (issue #425 decision 2).
	/// </summary>
	private static bool HasActiveIconProvider(WidgetEntity widget, JsonObject data)
		=> widget.Type == WidgetTypeIds.ActionButton && ActionButtonStateModel.Read(data).IconProvider is not null;

	/// <summary>
	/// Drops the icon property from a patch/clear-request exactly as a property the target type has no
	/// notion of is dropped today - every other property still applies, and <c>Apply</c> reports whether
	/// any of them changed something.
	/// </summary>
	private static (WidgetAppearancePatch Patch, IReadOnlyCollection<WidgetAppearanceProperty> ClearProperties)
		WithoutIcon(WidgetAppearancePatch patch, IReadOnlyCollection<WidgetAppearanceProperty> clearProperties)
		=> (patch with { IconId = null },
			clearProperties.Count == 0
				? clearProperties
				: clearProperties.Where(property => property != WidgetAppearanceProperty.Icon).ToList());

	private async Task<bool> Write(WidgetEntity widget, JsonObject data)
	{
		widget.Data = data.ToJsonString();

		var result = await _widgetService.Update(widget);
		if (result.Success)
		{
			return true;
		}

		_logger.Warning("Failed to write appearance change on widget {WidgetId}: {Error}",
			widget.Id,
			result.ErrorMessage ?? result.Error.ToString());
		return false;
	}

	private WidgetEntity? Locate(string widgetId)
	{
		if (!Guid.TryParse(widgetId, out var id))
		{
			_logger.Warning("Widget appearance change skipped: '{WidgetId}' is not a widget id", widgetId);
			return null;
		}

		var widget = FindWidget(id);
		if (widget is null)
		{
			_logger.Warning("Widget appearance change skipped: widget {WidgetId} does not exist", id);
		}

		return widget;
	}

	private WidgetEntity? FindWidget(Guid id)
		=> _folderCache.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(w => w.Id == id);

	/// <summary>
	/// Never a live provider read - <see cref="GetWidgets" /> runs synchronously on every options
	/// request, so a provider-backed button's states come from the cached <c>stateProvider.states</c>
	/// and its current id from the derived store, exactly as stored/last-reconciled.
	/// </summary>
	private (IReadOnlyList<WidgetStateInfo> States, string? CurrentStateId) ResolveTargetStates(
		WidgetEntity widget,
		JsonObject data)
	{
		if (widget.Type != WidgetTypeIds.ActionButton)
		{
			return ([], null);
		}

		var model = ActionButtonStateModel.Read(data);
		if (!model.StateMode)
		{
			return ([], null);
		}

		if (model.StateProvider is { States.Count: > 0 } provider)
		{
			var providerStates = provider.States.Select(s => new WidgetStateInfo(s.Id, s.Label)).ToList();
			var derived = _derivedStates.TryGet(widget.Id);
			var current = derived is not null && providerStates.Any(s => s.Id == derived)
				? derived
				: providerStates[0].Id;
			return (providerStates, current);
		}

		if (model.States.Count == 0)
		{
			return ([], null);
		}

		var infos = model.States.Select(s => new WidgetStateInfo(s.Id, s.Label)).ToList();
		var derivedId = _derivedStates.TryGet(widget.Id);
		var activeId = derivedId is not null && infos.Any(s => s.Id == derivedId)
			? derivedId
			: model.ActiveStateId is { } explicitId && infos.Any(s => s.Id == explicitId)
				? explicitId
				: infos[0].Id;
		return (infos, activeId);
	}

	private static string DescribeWidget(
		WidgetEntity widget,
		JsonObject data,
		string? stateId,
		IReadOnlyCollection<WidgetAppearanceProperty>? providerSupported)
	{
		var label = WidgetAppearanceJson.ReadLabel(data, widget.Type, stateId, providerSupported);
		return string.IsNullOrWhiteSpace(label) ? DescribeType(widget.Type) : label;
	}

	private static string DescribeType(string type) => type switch
	{
		WidgetTypeIds.ActionButton => "Button",
		WidgetTypeIds.MusicPlayer => "Music Player",
		WidgetTypeIds.Slider => "Slider",
		WidgetTypeIds.Weather => "Weather",
		WidgetTypeIds.HistoryGraph => "History Graph",
		WidgetTypeIds.Clock => "Clock",
		_ => "Widget"
	};
}
