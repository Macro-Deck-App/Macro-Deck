using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Variables;

public interface IWidgetVariableIndex
{
	IReadOnlyList<Guid> FindLabelReferences(string variableName);

	IReadOnlyList<Guid> FindStateMappingReferences(string variableName);

	/// <summary>Every widget whose stateProvider follows the given integration - any action, when actionId is null.</summary>
	IReadOnlyList<Guid> FindProviderReferences(string integrationId, string? actionId = null);

	/// <summary>Every widget whose iconProvider follows the given integration - any action, when actionId is null.</summary>
	IReadOnlyList<Guid> FindIconProviderReferences(string integrationId, string? actionId = null);

	bool LabelReferences(Guid widgetId, string variableName);

	bool StateMappingReferences(Guid widgetId, string variableName);

	IReadOnlyList<Guid> FindColorReferences(string variableName) => [];

	bool ColorReferences(Guid widgetId, string variableName) => false;

	void Rebuild();

	void ReindexWidget(Guid widgetId, string type, string? data);

	void Remove(Guid widgetId);
}

public sealed class WidgetVariableIndex : IWidgetVariableIndex
{
	private readonly IFolderCache _folderCache;
	private readonly Lock _writeLock = new();

	private volatile Snapshot _snapshot = Snapshot.Empty;

	public WidgetVariableIndex(IFolderCache folderCache)
	{
		_folderCache = folderCache;
	}

	public IReadOnlyList<Guid> FindLabelReferences(string variableName)
		=> _snapshot.ByLabelName.TryGetValue(variableName, out var widgets) ? widgets : [];

	public IReadOnlyList<Guid> FindStateMappingReferences(string variableName)
		=> _snapshot.ByStateMappingName.TryGetValue(variableName, out var widgets) ? widgets : [];

	public IReadOnlyList<Guid> FindProviderReferences(string integrationId, string? actionId = null)
	{
		if (actionId is not null)
		{
			return _snapshot.ByProvider.TryGetValue((integrationId, actionId), out var widgets) ? widgets : [];
		}

		return _snapshot.ByProvider
			.Where(pair => string.Equals(pair.Key.IntegrationId, integrationId, StringComparison.Ordinal))
			.SelectMany(pair => pair.Value)
			.Distinct()
			.ToList();
	}

	public IReadOnlyList<Guid> FindIconProviderReferences(string integrationId, string? actionId = null)
	{
		if (actionId is not null)
		{
			return _snapshot.ByIconProvider.TryGetValue((integrationId, actionId), out var widgets) ? widgets : [];
		}

		return _snapshot.ByIconProvider
			.Where(pair => string.Equals(pair.Key.IntegrationId, integrationId, StringComparison.Ordinal))
			.SelectMany(pair => pair.Value)
			.Distinct()
			.ToList();
	}

	public bool LabelReferences(Guid widgetId, string variableName)
		=> _snapshot.ByWidget.TryGetValue(widgetId, out var references) &&
			references.LabelNames.Contains(variableName);

	public bool StateMappingReferences(Guid widgetId, string variableName)
		=> _snapshot.ByWidget.TryGetValue(widgetId, out var references) &&
			references.StateMappingNames.Contains(variableName);

	public IReadOnlyList<Guid> FindColorReferences(string variableName)
		=> _snapshot.ByColorName.TryGetValue(variableName, out var widgets) ? widgets : [];

	public bool ColorReferences(Guid widgetId, string variableName)
		=> _snapshot.ColorsByWidget.TryGetValue(widgetId, out var names) && names.Contains(variableName);

	public void Rebuild()
	{
		lock (_writeLock)
		{
			var byWidget = new Dictionary<Guid, WidgetVariableReferences>();
			var colorsByWidget = new Dictionary<Guid, IReadOnlySet<string>>();
			foreach (var widget in _folderCache.GetAllFolders().SelectMany(folder => folder.Widgets))
			{
				if (ColorReferenceScanner.Names(widget.Data) is { Count: > 0 } colors)
				{
					colorsByWidget[widget.Id] = colors;
				}

				if (widget.Type != WidgetTypeIds.ActionButton)
				{
					continue;
				}

				var references = WidgetVariableReferenceParser.Parse(widget.Data);
				if (!references.IsEmpty)
				{
					byWidget[widget.Id] = references;
				}
			}

			Swap(byWidget, colorsByWidget);
		}
	}

	public void ReindexWidget(Guid widgetId, string type, string? data)
	{
		var references = type == WidgetTypeIds.ActionButton
			? WidgetVariableReferenceParser.Parse(data)
			: WidgetVariableReferences.Empty;
		var colors = ColorReferenceScanner.Names(data);

		lock (_writeLock)
		{
			var current = _snapshot.ByWidget;
			var currentColors = _snapshot.ColorsByWidget;
			if (references.IsEmpty &&
				!current.ContainsKey(widgetId) &&
				colors.Count == 0 &&
				!currentColors.ContainsKey(widgetId))
			{
				return;
			}

			var byWidget = new Dictionary<Guid, WidgetVariableReferences>(current);
			if (references.IsEmpty)
			{
				byWidget.Remove(widgetId);
			}
			else
			{
				byWidget[widgetId] = references;
			}

			var colorsByWidget = new Dictionary<Guid, IReadOnlySet<string>>(currentColors);
			if (colors.Count == 0)
			{
				colorsByWidget.Remove(widgetId);
			}
			else
			{
				colorsByWidget[widgetId] = colors;
			}

			Swap(byWidget, colorsByWidget);
		}
	}

	public void Remove(Guid widgetId)
	{
		lock (_writeLock)
		{
			if (!_snapshot.ByWidget.ContainsKey(widgetId) && !_snapshot.ColorsByWidget.ContainsKey(widgetId))
			{
				return;
			}

			var byWidget = new Dictionary<Guid, WidgetVariableReferences>(_snapshot.ByWidget);
			byWidget.Remove(widgetId);
			var colorsByWidget = new Dictionary<Guid, IReadOnlySet<string>>(_snapshot.ColorsByWidget);
			colorsByWidget.Remove(widgetId);
			Swap(byWidget, colorsByWidget);
		}
	}

	private void Swap(
		Dictionary<Guid, WidgetVariableReferences> byWidget,
		Dictionary<Guid, IReadOnlySet<string>> colorsByWidget)
	{
		var byLabelName = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
		var byStateMappingName = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
		var byProvider = new Dictionary<(string IntegrationId, string ActionId), List<Guid>>();
		var byIconProvider = new Dictionary<(string IntegrationId, string ActionId), List<Guid>>();

		foreach (var (widgetId, references) in byWidget)
		{
			Add(byLabelName, references.LabelNames, widgetId);
			Add(byStateMappingName, references.StateMappingNames, widgetId);
			AddProvider(byProvider, references.Provider, widgetId);
			AddProvider(byIconProvider, references.IconProvider, widgetId);
		}

		var byColorName = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
		foreach (var (widgetId, names) in colorsByWidget)
		{
			Add(byColorName, names, widgetId);
		}

		_snapshot = new Snapshot(byWidget,
			Freeze(byLabelName),
			Freeze(byStateMappingName),
			byProvider,
			byIconProvider,
			colorsByWidget,
			Freeze(byColorName));
	}

	private static void AddProvider(
		Dictionary<(string IntegrationId, string ActionId), List<Guid>> target,
		(string IntegrationId, string ActionId)? provider,
		Guid widgetId)
	{
		if (provider is not { } value)
		{
			return;
		}

		if (target.TryGetValue(value, out var widgets))
		{
			widgets.Add(widgetId);
		}
		else
		{
			target[value] = [widgetId];
		}
	}

	private static void Add(Dictionary<string, List<Guid>> target, IReadOnlySet<string> names, Guid widgetId)
	{
		foreach (var name in names)
		{
			if (target.TryGetValue(name, out var widgets))
			{
				widgets.Add(widgetId);
			}
			else
			{
				target[name] = [widgetId];
			}
		}
	}

	private static Dictionary<string, IReadOnlyList<Guid>> Freeze(Dictionary<string, List<Guid>> source)
	{
		var frozen = new Dictionary<string, IReadOnlyList<Guid>>(source.Count, StringComparer.Ordinal);
		foreach (var (name, widgets) in source)
		{
			frozen[name] = widgets;
		}

		return frozen;
	}

	private sealed record Snapshot(
		IReadOnlyDictionary<Guid, WidgetVariableReferences> ByWidget,
		IReadOnlyDictionary<string, IReadOnlyList<Guid>> ByLabelName,
		IReadOnlyDictionary<string, IReadOnlyList<Guid>> ByStateMappingName,
		IReadOnlyDictionary<(string IntegrationId, string ActionId), List<Guid>> ByProvider,
		IReadOnlyDictionary<(string IntegrationId, string ActionId), List<Guid>> ByIconProvider,
		IReadOnlyDictionary<Guid, IReadOnlySet<string>> ColorsByWidget,
		IReadOnlyDictionary<string, IReadOnlyList<Guid>> ByColorName)
	{
		public static readonly Snapshot Empty = new(new Dictionary<Guid, WidgetVariableReferences>(),
			new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal),
			new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal),
			new Dictionary<(string IntegrationId, string ActionId), List<Guid>>(),
			new Dictionary<(string IntegrationId, string ActionId), List<Guid>>(),
			new Dictionary<Guid, IReadOnlySet<string>>(),
			new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal));
	}
}
