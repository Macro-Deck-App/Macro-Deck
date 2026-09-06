using System.Globalization;
using System.Text;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs;

/// <summary>
/// The catalog half of <see cref="ObsIntegration"/>'s variable provider: every configured OBS
/// connection's inputs, scenes, scene items and filters, so the user can pick exactly the sources they
/// want as variables instead of Macro Deck 2's approach of turning every input and source into one, which
/// is what caused the lag and freezes reported in
/// <see href="https://github.com/Macro-Deck-Org/Macro-Deck/issues/470">issue #470</see>.
/// </summary>
internal sealed class ObsVariableCatalog
{
	private const string InputSegment = "input";
	private const string SceneSegment = "scene";
	private const string ItemSegment = "item";
	private const string FilterSegment = "filter";
	private const string SettingSegment = "setting";
	private const string EnabledLeaf = "enabled";
	private const string VisibleLeaf = "visible";
	private const string TrackPrefix = "track-";

	private static readonly ILogger _logger = IntegrationLog.For<ObsVariableCatalog>(ObsIntegration.IntegrationId);

	// Decibels, not the raw multiplier OBS's protocol carries: OBS's own audio mixer is a -60..0 dB
	// scale, and a linear multiplier squeezes every useful mixing position into the bottom tenth of a
	// control, which is unusable to drag. The multiplier stays the wire representation on both sides.
	private const double MinimumVolumeDb = -60;

	private static readonly LeafSpec _volumeSpec = new("volume",
		VariableType.Numeric,
		1,
		TimeSpan.FromSeconds(1),
		AppStrings.Integrations.Obs.VariableCatalog.Volume(),
		"volume")
	{
		Unit = "dB",
		Write = new VariableWriteCapability()
	};

	private static readonly LeafSpec _mutedSpec = new("muted",
		VariableType.Boolean,
		null,
		TimeSpan.FromSeconds(1),
		AppStrings.Integrations.Obs.VariableCatalog.Muted(),
		"muted");

	private static readonly LeafSpec _activeSpec = new("active",
		VariableType.Boolean,
		null,
		TimeSpan.FromSeconds(1),
		AppStrings.Integrations.Obs.VariableCatalog.Active(),
		"active");

	private static readonly LeafSpec _showingSpec = new("showing",
		VariableType.Boolean,
		null,
		TimeSpan.FromSeconds(1),
		AppStrings.Integrations.Obs.VariableCatalog.Showing(),
		"showing");

	private static readonly LeafSpec _monitorTypeSpec = new("monitor-type",
		VariableType.Text,
		null,
		TimeSpan.FromSeconds(5),
		AppStrings.Integrations.Obs.VariableCatalog.MonitorType(),
		"monitor_type");

	private static readonly LeafSpec _syncOffsetSpec = new("sync-offset",
		VariableType.Numeric,
		0,
		TimeSpan.FromSeconds(5),
		AppStrings.Integrations.Obs.VariableCatalog.SyncOffset(),
		"sync_offset");

	private static readonly LeafSpec[] _inputFixedLeaves =
		[_volumeSpec, _mutedSpec, _activeSpec, _showingSpec, _monitorTypeSpec, _syncOffsetSpec];

	private static readonly LeafSpec[] _audioTrackSpecs = Enumerable.Range(1, 6)
		.Select(track =>
		{
			var digits = track.ToString(CultureInfo.InvariantCulture);
			return new LeafSpec(TrackPrefix + digits,
				VariableType.Boolean,
				null,
				TimeSpan.FromSeconds(5),
				AppStrings.Integrations.Obs.VariableCatalog.AudioTrack(track: digits),
				"track_" + digits);
		})
		.ToArray();

	private readonly Func<IReadOnlyList<ObsRuntime>> _runtimes;

	public ObsVariableCatalog(Func<IReadOnlyList<ObsRuntime>> runtimes)
	{
		_runtimes = runtimes;
	}

	public static string CatalogName => "OBS Studio";

	// OBS has no event stream for volume, input settings, audio tracks or monitor type, and SupportsPush is
	// all-or-nothing per provider: reporting true here would turn off polling for the whole provider, not
	// just the resources OBS happens to push, freezing every event-less one at its first value forever.
	public static bool SupportsPush => false;

	// A single OBS install has dozens of resources, not thousands; the host then correctly omits the
	// search box rather than showing one that only filters whatever single page is already loaded.
	public static bool SupportsSearch => false;

	public async ValueTask<VariableCatalogPage> DiscoverAsync(
		VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
	{
		if (query.ParentId is null)
		{
			return DiscoverRoots(query);
		}

		if (ParseId(query.ParentId) is not { } parent)
		{
			return VariableCatalogPage.Empty;
		}

		return parent.Kind switch
		{
			ResourceKind.Connection => await DiscoverConnectionChildrenAsync(parent.RuntimeId, query)
				.ConfigureAwait(false),
			ResourceKind.InputContainer =>
				await DiscoverInputChildrenAsync(parent.RuntimeId, parent.SourceName!, query).ConfigureAwait(false),
			ResourceKind.SceneContainer =>
				await DiscoverSceneChildrenAsync(parent.RuntimeId, parent.SourceName!, query).ConfigureAwait(false),
			ResourceKind.InputFilterContainer => DiscoverInputFilterChildren(parent, query),
			ResourceKind.SceneFilterContainer => DiscoverSceneFilterChildren(parent, query),
			ResourceKind.SceneItemContainer => DiscoverSceneItemChildren(parent, query),
			_ => VariableCatalogPage.Empty
		};
	}

	public async ValueTask<VariableDefinition?> ResolveAsync(
		string id,
		CancellationToken cancellationToken = default)
	{
		if (ParseId(id) is not { } parsed)
		{
			return null;
		}

		switch (parsed.Kind)
		{
			case ResourceKind.Connection:
				return ConnectionDefinition(parsed.RuntimeId, TitleOf(parsed.RuntimeId));
			case ResourceKind.InputContainer:
				return InputContainerDefinition(parsed.RuntimeId, parsed.SourceName!);
			case ResourceKind.InputVolume:
				return LeafDefinition(id,
					InputContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.SourceName!,
					_volumeSpec);
			case ResourceKind.InputMuted:
				return LeafDefinition(id,
					InputContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.SourceName!,
					_mutedSpec);
			case ResourceKind.InputActive:
				return LeafDefinition(id,
					InputContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.SourceName!,
					_activeSpec);
			case ResourceKind.InputShowing:
				return LeafDefinition(id,
					InputContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.SourceName!,
					_showingSpec);
			case ResourceKind.InputMonitorType:
				return LeafDefinition(id,
					InputContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.SourceName!,
					_monitorTypeSpec);
			case ResourceKind.InputSyncOffset:
				return LeafDefinition(id,
					InputContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.SourceName!,
					_syncOffsetSpec);
			case ResourceKind.InputAudioTrack:
				return LeafDefinition(id,
					InputContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.SourceName!,
					_audioTrackSpecs[parsed.TrackIndex - 1]);
			case ResourceKind.InputSetting:
				return await ResolveSettingAsync(parsed).ConfigureAwait(false);
			case ResourceKind.InputFilterContainer:
				return FilterContainerDefinition(id,
					InputContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.ChildName!);
			case ResourceKind.InputFilterEnabled:
				return FilterEnabledDefinition(id,
					InputFilterContainerId(parsed.RuntimeId, parsed.SourceName!, parsed.ChildName!),
					parsed.SourceName!,
					parsed.ChildName!);
			case ResourceKind.SceneContainer:
				return SceneContainerDefinition(parsed.RuntimeId, parsed.SourceName!);
			case ResourceKind.SceneActive:
				return LeafDefinition(id,
					SceneContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.SourceName!,
					_activeSpec);
			case ResourceKind.SceneShowing:
				return LeafDefinition(id,
					SceneContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.SourceName!,
					_showingSpec);
			case ResourceKind.SceneItemContainer:
				return ItemContainerDefinition(id,
					SceneContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.ChildName!);
			case ResourceKind.SceneItemVisible:
				return SceneItemVisibleDefinition(id,
					SceneItemContainerId(parsed.RuntimeId, parsed.SourceName!, parsed.ChildName!),
					parsed.SourceName!,
					parsed.ChildName!);
			case ResourceKind.SceneFilterContainer:
				return FilterContainerDefinition(id,
					SceneContainerId(parsed.RuntimeId, parsed.SourceName!),
					parsed.ChildName!);
			case ResourceKind.SceneFilterEnabled:
				return FilterEnabledDefinition(id,
					SceneFilterContainerId(parsed.RuntimeId, parsed.SourceName!, parsed.ChildName!),
					parsed.SourceName!,
					parsed.ChildName!);
			default:
				return null;
		}
	}

	public async ValueTask<VariableReading> ReadAsync(string id, CancellationToken cancellationToken = default)
	{
		if (ParseId(id) is not { } parsed)
		{
			return VariableReading.Unavailable;
		}

		var runtime = FindRuntime(parsed.RuntimeId);
		if (runtime is null)
		{
			return VariableReading.Unavailable;
		}

		var connection = runtime.Connection;
		switch (parsed.Kind)
		{
			case ResourceKind.InputVolume:
				var percent = await connection.GetInputVolumePercentCachedAsync(parsed.SourceName!)
					.ConfigureAwait(false);
				if (!percent.HasValue)
				{
					return VariableReading.Unavailable;
				}

				// Rounded to the declared DecimalPlaces: OBS's multiplier is a float, so the converted
				// decibels carry noise well past the digit anyone reads.
				var decibels = Math.Round(ToDecibels(percent.Value), 1);

				// An input boosted in Advanced Audio Properties sits above 0 dB, and reporting a ceiling
				// of 0 would make a control that shows it also unable to hold it: the first drag would
				// silently cut the boost. The reported ceiling therefore follows the value up.
				return VariableReading.Of(decibels, MinimumVolumeDb, Math.Max(0, decibels), 0.1);
			case ResourceKind.InputMuted:
				return VariableReading.Of(await connection.GetInputMutedCachedAsync(parsed.SourceName!)
					.ConfigureAwait(false));
			case ResourceKind.InputActive:
				return VariableReading.Of(
					(await connection.GetSourceActiveAsync(parsed.SourceName!).ConfigureAwait(false))?.Active);
			case ResourceKind.InputShowing:
				return VariableReading.Of(
					(await connection.GetSourceActiveAsync(parsed.SourceName!).ConfigureAwait(false))?.Showing);
			case ResourceKind.InputMonitorType:
				return VariableReading.Of(await connection.GetInputAudioMonitorTypeAsync(parsed.SourceName!)
					.ConfigureAwait(false));
			case ResourceKind.InputSyncOffset:
				return VariableReading.Of(await connection
					.GetInputAudioSyncOffsetMillisecondsAsync(parsed.SourceName!)
					.ConfigureAwait(false));
			case ResourceKind.InputAudioTrack:
				var tracks = await connection.GetInputAudioTracksAsync(parsed.SourceName!).ConfigureAwait(false);
				return VariableReading.Of(tracks?.Tracks[parsed.TrackIndex - 1]);
			case ResourceKind.InputSetting:
				var json = await connection.GetInputSettingsJsonAsync(parsed.SourceName!).ConfigureAwait(false);
				return VariableReading.Of(
					json is not null && TryGetSetting(json, parsed.ChildName!, out _, out var settingValue)
						? settingValue
						: null);
			case ResourceKind.InputFilterEnabled:
				return VariableReading.Of(await connection
					.GetSourceFilterEnabledCachedAsync(parsed.SourceName!, parsed.ChildName!)
					.ConfigureAwait(false));
			case ResourceKind.SceneActive:
				return VariableReading.Of(
					(await connection.GetSourceActiveAsync(parsed.SourceName!).ConfigureAwait(false))?.Active);
			case ResourceKind.SceneShowing:
				return VariableReading.Of(
					(await connection.GetSourceActiveAsync(parsed.SourceName!).ConfigureAwait(false))?.Showing);
			case ResourceKind.SceneItemVisible:
				return VariableReading.Of(await connection
					.GetSourceVisibleCachedAsync(parsed.SourceName!, parsed.ChildName!)
					.ConfigureAwait(false));
			case ResourceKind.SceneFilterEnabled:
				return VariableReading.Of(await connection
					.GetSourceFilterEnabledCachedAsync(parsed.SourceName!, parsed.ChildName!)
					.ConfigureAwait(false));
			default:
				return VariableReading.Unavailable;
		}
	}

	public async ValueTask<VariableWriteResult> SetValueAsync(
		string id,
		object? value,
		CancellationToken cancellationToken = default)
	{
		if (ParseId(id) is not { Kind: ResourceKind.InputVolume } parsed)
		{
			return VariableWriteResult.NotWritable();
		}

		if (FindRuntime(parsed.RuntimeId) is not { } runtime)
		{
			return VariableWriteResult.Unavailable();
		}

		if (!TryReadNumber(value, out var decibels))
		{
			return VariableWriteResult.InvalidValue();
		}

		// Clamped at the bottom only, where OBS's own scale ends and the conversion would otherwise run
		// off toward negative infinity. Deliberately unclamped upward: OBS accepts a multiplier above
		// 1.0, the reading reports a ceiling that follows a boosted input up, and clamping here would
		// quietly cut the boost the first time a control wrote the value it had just been shown.
		var percent = ToPercent(Math.Max(MinimumVolumeDb, decibels));
		var applied = await runtime.Connection.SetInputVolumePercentAsync(parsed.SourceName!, percent)
			.ConfigureAwait(false);

		return applied ? VariableWriteResult.Applied() : VariableWriteResult.Unavailable();
	}

	/// <summary>OBS's linear multiplier as a percentage, expressed on OBS's own decibel scale.</summary>
	private static double ToDecibels(double percent)
	{
		var multiplier = percent / 100.0;
		return multiplier <= 0
			? MinimumVolumeDb
			: Math.Max(MinimumVolumeDb, 20 * Math.Log10(multiplier));
	}

	private static double ToPercent(double decibels) => Math.Pow(10, decibels / 20.0) * 100.0;

	private static bool TryReadNumber(object? value, out double percent)
	{
		percent = value switch
		{
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			decimal m => (double)m,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
				parsed,
			_ => double.NaN
		};

		return double.IsFinite(percent);
	}

	private ObsRuntime? FindRuntime(Guid runtimeId) => _runtimes().FirstOrDefault(runtime => runtime.Id == runtimeId);

	private string TitleOf(Guid runtimeId) => FindRuntime(runtimeId)?.Title ?? runtimeId.ToString("D");

	private VariableCatalogPage DiscoverRoots(VariableCatalogQuery query)
	{
		var candidates = _runtimes()
			.Select(runtime =>
			{
				var title = runtime.Title;
				return (Id: RootId(runtime.Id),
					Factory: (Func<VariableDefinition>)(() => ConnectionDefinition(runtime.Id, title)));
			})
			.ToList();

		return BuildPage(candidates, query);
	}

	private async Task<VariableCatalogPage> DiscoverConnectionChildrenAsync(Guid runtimeId, VariableCatalogQuery query)
	{
		var runtime = FindRuntime(runtimeId);
		if (runtime is null)
		{
			return VariableCatalogPage.Empty;
		}

		var inputs = await runtime.Connection.GetInputNamesAsync().ConfigureAwait(false);
		var scenes = await runtime.Connection.GetSceneNamesAsync().ConfigureAwait(false);

		var candidates = new List<(string Id, Func<VariableDefinition> Factory)>(inputs.Count + scenes.Count);
		foreach (var name in inputs)
		{
			var inputName = name;
			candidates.Add((InputContainerId(runtimeId, inputName),
				() => InputContainerDefinition(runtimeId, inputName)));
		}

		foreach (var name in scenes)
		{
			var sceneName = name;
			candidates.Add((SceneContainerId(runtimeId, sceneName),
				() => SceneContainerDefinition(runtimeId, sceneName)));
		}

		return BuildPage(candidates, query);
	}

	private async Task<VariableCatalogPage> DiscoverInputChildrenAsync(
		Guid runtimeId,
		string inputName,
		VariableCatalogQuery query)
	{
		var runtime = FindRuntime(runtimeId);
		if (runtime is null)
		{
			return VariableCatalogPage.Empty;
		}

		var connection = runtime.Connection;
		var containerId = InputContainerId(runtimeId, inputName);
		var candidates = new List<(string Id, Func<VariableDefinition> Factory)>();

		foreach (var spec in _inputFixedLeaves.Concat(_audioTrackSpecs))
		{
			var leafSpec = spec;
			var leafId = InputLeafId(runtimeId, inputName, leafSpec.Segment);
			candidates.Add((leafId, () => LeafDefinition(leafId, containerId, inputName, leafSpec)));
		}

		var filters = await connection.GetSourceFilterNamesAsync(inputName).ConfigureAwait(false);
		foreach (var name in filters)
		{
			var filterName = name;
			var filterContainerId = InputFilterContainerId(runtimeId, inputName, filterName);
			candidates.Add((filterContainerId,
				() => FilterContainerDefinition(filterContainerId, containerId, filterName)));
		}

		var json = await connection.GetInputSettingsJsonAsync(inputName).ConfigureAwait(false);
		if (json is not null && TryGetSettingKeys(json, out var keys))
		{
			foreach (var name in keys)
			{
				var settingKey = name;
				candidates.Add((InputSettingId(runtimeId, inputName, settingKey), () =>
				{
					TryGetSetting(json, settingKey, out var type, out _);
					return SettingDefinition(runtimeId, inputName, settingKey, type);
				}));
			}
		}

		return BuildPage(candidates, query);
	}

	private async Task<VariableCatalogPage> DiscoverSceneChildrenAsync(
		Guid runtimeId,
		string sceneName,
		VariableCatalogQuery query)
	{
		var runtime = FindRuntime(runtimeId);
		if (runtime is null)
		{
			return VariableCatalogPage.Empty;
		}

		var connection = runtime.Connection;
		var containerId = SceneContainerId(runtimeId, sceneName);
		var candidates = new List<(string Id, Func<VariableDefinition> Factory)>();

		foreach (var spec in new[] { _activeSpec, _showingSpec })
		{
			var leafSpec = spec;
			var leafId = SceneLeafId(runtimeId, sceneName, leafSpec.Segment);
			candidates.Add((leafId, () => LeafDefinition(leafId, containerId, sceneName, leafSpec)));
		}

		var items = await connection.GetSceneItemNamesAsync(sceneName).ConfigureAwait(false);
		foreach (var name in items)
		{
			var itemName = name;
			var itemContainerId = SceneItemContainerId(runtimeId, sceneName, itemName);
			candidates.Add((itemContainerId, () => ItemContainerDefinition(itemContainerId, containerId, itemName)));
		}

		var filters = await connection.GetSourceFilterNamesAsync(sceneName).ConfigureAwait(false);
		foreach (var name in filters)
		{
			var filterName = name;
			var filterContainerId = SceneFilterContainerId(runtimeId, sceneName, filterName);
			candidates.Add((filterContainerId,
				() => FilterContainerDefinition(filterContainerId, containerId, filterName)));
		}

		return BuildPage(candidates, query);
	}

	private static VariableCatalogPage DiscoverInputFilterChildren(ParsedId parent, VariableCatalogQuery query)
	{
		var containerId = InputFilterContainerId(parent.RuntimeId, parent.SourceName!, parent.ChildName!);
		var leafId = InputFilterEnabledId(parent.RuntimeId, parent.SourceName!, parent.ChildName!);
		var candidates = new List<(string Id, Func<VariableDefinition> Factory)>
		{
			(leafId, () => FilterEnabledDefinition(leafId, containerId, parent.SourceName!, parent.ChildName!))
		};

		return BuildPage(candidates, query);
	}

	private static VariableCatalogPage DiscoverSceneFilterChildren(ParsedId parent, VariableCatalogQuery query)
	{
		var containerId = SceneFilterContainerId(parent.RuntimeId, parent.SourceName!, parent.ChildName!);
		var leafId = SceneFilterEnabledId(parent.RuntimeId, parent.SourceName!, parent.ChildName!);
		var candidates = new List<(string Id, Func<VariableDefinition> Factory)>
		{
			(leafId, () => FilterEnabledDefinition(leafId, containerId, parent.SourceName!, parent.ChildName!))
		};

		return BuildPage(candidates, query);
	}

	private static VariableCatalogPage DiscoverSceneItemChildren(ParsedId parent, VariableCatalogQuery query)
	{
		var containerId = SceneItemContainerId(parent.RuntimeId, parent.SourceName!, parent.ChildName!);
		var leafId = SceneItemVisibleId(parent.RuntimeId, parent.SourceName!, parent.ChildName!);
		var candidates = new List<(string Id, Func<VariableDefinition> Factory)>
		{
			(leafId, () => SceneItemVisibleDefinition(leafId, containerId, parent.SourceName!, parent.ChildName!))
		};

		return BuildPage(candidates, query);
	}

	private async ValueTask<VariableDefinition> ResolveSettingAsync(ParsedId parsed)
	{
		var type = VariableType.Text;
		var runtime = FindRuntime(parsed.RuntimeId);
		if (runtime is not null)
		{
			var json = await runtime.Connection.GetInputSettingsJsonAsync(parsed.SourceName!).ConfigureAwait(false);
			if (json is not null && TryGetSetting(json, parsed.ChildName!, out var resolvedType, out _))
			{
				type = resolvedType;
			}
		}

		return SettingDefinition(parsed.RuntimeId, parsed.SourceName!, parsed.ChildName!, type);
	}

	// The token is the last candidate considered on this page, whether or not it made it into Items - not
	// the last one actually returned. That is what keeps an over-length skip (see the loop below) from
	// stalling or rewinding the cursor: the next page still starts strictly after it.
	private static VariableCatalogPage BuildPage(
		List<(string Id, Func<VariableDefinition> Factory)> candidates,
		VariableCatalogQuery query)
	{
		candidates.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

		var startIndex = 0;
		if (query.ContinuationToken is { Length: > 0 } token)
		{
			while (startIndex < candidates.Count && string.CompareOrdinal(candidates[startIndex].Id, token) <= 0)
			{
				startIndex++;
			}
		}

		var pageSize = Math.Max(query.PageSize, 0);
		var slice = candidates.Skip(startIndex).Take(pageSize).ToList();

		var items = new List<VariableDefinition>(slice.Count);
		foreach (var candidate in slice)
		{
			if (!MacroDeckId.IsValidLocalId(candidate.Id, LocalIdKind.Resource))
			{
				_logger.Debug("Skipping an OBS resource whose id is longer than {Limit} characters",
					MacroDeckId.MaxResourceLocalIdLength);

				continue;
			}

			items.Add(candidate.Factory());
		}

		var hasMore = startIndex + slice.Count < candidates.Count;
		return new VariableCatalogPage
		{
			Items = items,
			ContinuationToken = hasMore ? slice[^1].Id : null
		};
	}

	private static VariableDefinition ConnectionDefinition(Guid runtimeId, string title)
		=> VariableDefinition.OnDemand(RootId(runtimeId), VariableType.Text) with
		{
			DisplayName = title,
			IsContainer = true,
			IsBindable = false
		};

	private static VariableDefinition InputContainerDefinition(Guid runtimeId, string inputName)
		=> VariableDefinition.OnDemand(InputContainerId(runtimeId, inputName), VariableType.Text) with
		{
			DisplayName = inputName,
			ParentId = RootId(runtimeId),
			IsContainer = true,
			IsBindable = false
		};

	private static VariableDefinition SceneContainerDefinition(Guid runtimeId, string sceneName)
		=> VariableDefinition.OnDemand(SceneContainerId(runtimeId, sceneName), VariableType.Text) with
		{
			DisplayName = sceneName,
			ParentId = RootId(runtimeId),
			IsContainer = true,
			IsBindable = false
		};

	private static VariableDefinition FilterContainerDefinition(string id, string parentId, string filterName)
		=> VariableDefinition.OnDemand(id, VariableType.Text) with
		{
			DisplayName = filterName,
			ParentId = parentId,
			IsContainer = true,
			IsBindable = false
		};

	private static VariableDefinition ItemContainerDefinition(string id, string parentId, string itemName)
		=> VariableDefinition.OnDemand(id, VariableType.Text) with
		{
			DisplayName = itemName,
			ParentId = parentId,
			IsContainer = true,
			IsBindable = false
		};

	private static VariableDefinition LeafDefinition(string id,
		string parentId,
		string sourceName,
		LeafSpec spec)
		=> VariableDefinition.OnDemand(id, spec.Type) with
		{
			Name = SuggestedName(sourceName, spec.SuggestedSuffix),
			DisplayName = spec.Segment,
			ParentId = parentId,
			DecimalPlaces = spec.DecimalPlaces,
			RefreshInterval = spec.RefreshInterval,
			Description = spec.Description,
			Unit = spec.Unit,
			SemanticKind = spec.SemanticKind,
			Write = spec.Write
		};

	private static VariableDefinition SettingDefinition(Guid runtimeId,
		string inputName,
		string key,
		VariableType type)
		=> VariableDefinition.OnDemand(InputSettingId(runtimeId, inputName, key), type) with
		{
			Name = SuggestedName(inputName, Sanitize(key)),
			DisplayName = key,
			ParentId = InputContainerId(runtimeId, inputName),
			RefreshInterval = TimeSpan.FromSeconds(5),
			Description = AppStrings.Integrations.Obs.VariableCatalog.InputSetting()
		};

	private static VariableDefinition FilterEnabledDefinition(
		string id,
		string parentId,
		string sourceName,
		string filterName)
		=> VariableDefinition.OnDemand(id, VariableType.Boolean) with
		{
			Name = $"{SuggestedName(sourceName, Sanitize(filterName))}_{EnabledLeaf}",
			DisplayName = EnabledLeaf,
			ParentId = parentId,
			RefreshInterval = TimeSpan.FromSeconds(2),
			Description = AppStrings.Integrations.Obs.VariableCatalog.FilterEnabled()
		};

	private static VariableDefinition SceneItemVisibleDefinition(
		string id,
		string parentId,
		string sceneName,
		string itemName)
		=> VariableDefinition.OnDemand(id, VariableType.Boolean) with
		{
			Name = $"{SuggestedName(sceneName, Sanitize(itemName))}_{VisibleLeaf}",
			DisplayName = VisibleLeaf,
			ParentId = parentId,
			RefreshInterval = TimeSpan.FromSeconds(1),
			Description = AppStrings.Integrations.Obs.VariableCatalog.Visible()
		};

	private static string SuggestedName(string sourceName, string suffix) => $"obs_{Sanitize(sourceName)}_{suffix}";

	private static string Sanitize(string value)
	{
		var builder = new StringBuilder(value.Length);
		foreach (var character in value.ToLowerInvariant())
		{
			builder.Append(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) ? character : '_');
		}

		return builder.ToString();
	}

	private static bool TryGetSettingKeys(string json, out List<string> keys)
	{
		keys = [];

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json);
		}
		catch (JsonException)
		{
			return false;
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return false;
			}

			foreach (var property in document.RootElement.EnumerateObject())
			{
				keys.Add(property.Name);
			}

			return true;
		}
	}

	private static bool TryGetSetting(string json, string key, out VariableType type, out object value)
	{
		type = VariableType.Text;
		value = string.Empty;

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json);
		}
		catch (JsonException)
		{
			return false;
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object ||
				!document.RootElement.TryGetProperty(key, out var element))
			{
				return false;
			}

			(type, value) = MapSettingValue(element);
			return true;
		}
	}

	// Never coerces a string to a boolean or a number: a setting whose JSON value is the string "true" (or
	// "75") stays Text with that literal CLR string, matching what OBS actually reports rather than what
	// the text happens to look like. Object, array and null values fall through to their raw JSON text -
	// none of GetValueAsync's three legal shapes (string, number, bool) can carry them losslessly.
	private static (VariableType Type, object Value) MapSettingValue(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.True => (VariableType.Boolean, true),
		JsonValueKind.False => (VariableType.Boolean, false),
		// A JSON number is arbitrary-precision, and .NET's own double parsing saturates rather than failing
		// on overflow - TryGetDouble reports success with a PositiveInfinity/NegativeInfinity result for a
		// magnitude like 1e999, not false - so IsFinite is what actually catches an unrepresentable value;
		// TryGetDouble's own bool is kept as a defensive first check. Either failure falls back to the raw
		// JSON text rather than a substituted 0, exactly like the neighbouring default case below.
		JsonValueKind.Number => element.TryGetDouble(out var number) && double.IsFinite(number)
			? (VariableType.Numeric, number)
			: (VariableType.Text, element.GetRawText()),
		JsonValueKind.String => (VariableType.Text, (object)(element.GetString() ?? string.Empty)),
		_ => (VariableType.Text, element.GetRawText())
	};

	private static string RootId(Guid runtimeId) => runtimeId.ToString("D");

	private static string InputContainerId(Guid runtimeId, string inputName)
		=> $"{RootId(runtimeId)}/{InputSegment}/{CatalogResourceIds.Encode(inputName)}";

	private static string InputLeafId(Guid runtimeId, string inputName, string leaf)
		=> $"{InputContainerId(runtimeId, inputName)}/{leaf}";

	private static string InputSettingId(Guid runtimeId, string inputName, string key)
		=> $"{InputContainerId(runtimeId, inputName)}/{SettingSegment}/{CatalogResourceIds.Encode(key)}";

	private static string InputFilterContainerId(Guid runtimeId, string inputName, string filterName)
		=> $"{InputContainerId(runtimeId, inputName)}/{FilterSegment}/{CatalogResourceIds.Encode(filterName)}";

	private static string InputFilterEnabledId(Guid runtimeId, string inputName, string filterName)
		=> $"{InputFilterContainerId(runtimeId, inputName, filterName)}/{EnabledLeaf}";

	private static string SceneContainerId(Guid runtimeId, string sceneName)
		=> $"{RootId(runtimeId)}/{SceneSegment}/{CatalogResourceIds.Encode(sceneName)}";

	private static string SceneLeafId(Guid runtimeId, string sceneName, string leaf)
		=> $"{SceneContainerId(runtimeId, sceneName)}/{leaf}";

	private static string SceneItemContainerId(Guid runtimeId, string sceneName, string itemName)
		=> $"{SceneContainerId(runtimeId, sceneName)}/{ItemSegment}/{CatalogResourceIds.Encode(itemName)}";

	private static string SceneItemVisibleId(Guid runtimeId, string sceneName, string itemName)
		=> $"{SceneItemContainerId(runtimeId, sceneName, itemName)}/{VisibleLeaf}";

	private static string SceneFilterContainerId(Guid runtimeId, string sceneName, string filterName)
		=> $"{SceneContainerId(runtimeId, sceneName)}/{FilterSegment}/{CatalogResourceIds.Encode(filterName)}";

	private static string SceneFilterEnabledId(Guid runtimeId, string sceneName, string filterName)
		=> $"{SceneFilterContainerId(runtimeId, sceneName, filterName)}/{EnabledLeaf}";

	private static ParsedId? ParseId(string id)
	{
		var segments = id.Split('/');
		if (!Guid.TryParse(segments[0], out var runtimeId))
		{
			return null;
		}

		if (segments.Length == 1)
		{
			return new ParsedId(runtimeId, ResourceKind.Connection);
		}

		if (segments.Length < 3 || !CatalogResourceIds.TryDecode(segments[2], out var sourceName))
		{
			return null;
		}

		return segments[1] switch
		{
			InputSegment => ParseInputId(runtimeId, sourceName, segments),
			SceneSegment => ParseSceneId(runtimeId, sourceName, segments),
			_ => null
		};
	}

	private static ParsedId? ParseInputId(Guid runtimeId, string inputName, string[] segments)
	{
		if (segments.Length == 3)
		{
			return new ParsedId(runtimeId, ResourceKind.InputContainer, inputName);
		}

		var leaf = segments[3];
		if (segments.Length == 4)
		{
			return leaf switch
			{
				"volume" => new ParsedId(runtimeId, ResourceKind.InputVolume, inputName),
				"muted" => new ParsedId(runtimeId, ResourceKind.InputMuted, inputName),
				"active" => new ParsedId(runtimeId, ResourceKind.InputActive, inputName),
				"showing" => new ParsedId(runtimeId, ResourceKind.InputShowing, inputName),
				"monitor-type" => new ParsedId(runtimeId, ResourceKind.InputMonitorType, inputName),
				"sync-offset" => new ParsedId(runtimeId, ResourceKind.InputSyncOffset, inputName),
				_ => TryParseTrack(leaf, out var track)
					? new ParsedId(runtimeId, ResourceKind.InputAudioTrack, inputName, TrackIndex: track)
					: null
			};
		}

		if (leaf == SettingSegment &&
			segments.Length == 5 &&
			CatalogResourceIds.TryDecode(segments[4], out var settingKey))
		{
			return new ParsedId(runtimeId, ResourceKind.InputSetting, inputName, settingKey);
		}

		if (leaf == FilterSegment &&
			segments.Length is 5 or 6 &&
			CatalogResourceIds.TryDecode(segments[4], out var filterName))
		{
			if (segments.Length == 5)
			{
				return new ParsedId(runtimeId, ResourceKind.InputFilterContainer, inputName, filterName);
			}

			return segments[5] == EnabledLeaf
				? new ParsedId(runtimeId, ResourceKind.InputFilterEnabled, inputName, filterName)
				: null;
		}

		return null;
	}

	private static ParsedId? ParseSceneId(Guid runtimeId, string sceneName, string[] segments)
	{
		if (segments.Length == 3)
		{
			return new ParsedId(runtimeId, ResourceKind.SceneContainer, sceneName);
		}

		var leaf = segments[3];
		if (segments.Length == 4)
		{
			return leaf switch
			{
				"active" => new ParsedId(runtimeId, ResourceKind.SceneActive, sceneName),
				"showing" => new ParsedId(runtimeId, ResourceKind.SceneShowing, sceneName),
				_ => null
			};
		}

		if (leaf == ItemSegment &&
			segments.Length is 5 or 6 &&
			CatalogResourceIds.TryDecode(segments[4], out var itemName))
		{
			if (segments.Length == 5)
			{
				return new ParsedId(runtimeId, ResourceKind.SceneItemContainer, sceneName, itemName);
			}

			return segments[5] == VisibleLeaf
				? new ParsedId(runtimeId, ResourceKind.SceneItemVisible, sceneName, itemName)
				: null;
		}

		if (leaf == FilterSegment &&
			segments.Length is 5 or 6 &&
			CatalogResourceIds.TryDecode(segments[4], out var filterName))
		{
			if (segments.Length == 5)
			{
				return new ParsedId(runtimeId, ResourceKind.SceneFilterContainer, sceneName, filterName);
			}

			return segments[5] == EnabledLeaf
				? new ParsedId(runtimeId, ResourceKind.SceneFilterEnabled, sceneName, filterName)
				: null;
		}

		return null;
	}

	private static bool TryParseTrack(string leaf, out int track)
	{
		track = 0;
		return leaf.StartsWith(TrackPrefix, StringComparison.Ordinal) &&
			int.TryParse(leaf.AsSpan(TrackPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out track) &&
			track is >= 1 and <= 6;
	}

	private enum ResourceKind
	{
		Connection,
		InputContainer,
		InputVolume,
		InputMuted,
		InputActive,
		InputShowing,
		InputMonitorType,
		InputSyncOffset,
		InputAudioTrack,
		InputSetting,
		InputFilterContainer,
		InputFilterEnabled,
		SceneContainer,
		SceneActive,
		SceneShowing,
		SceneItemContainer,
		SceneItemVisible,
		SceneFilterContainer,
		SceneFilterEnabled
	}

	private readonly record struct ParsedId(
		Guid RuntimeId,
		ResourceKind Kind,
		string? SourceName = null,
		string? ChildName = null,
		int TrackIndex = 0);

	private sealed record LeafSpec(
		string Segment,
		VariableType Type,
		int? DecimalPlaces,
		TimeSpan RefreshInterval,
		LocalizedText Description,
		string SuggestedSuffix)
	{
		public string? Unit { get; init; }

		public string? SemanticKind { get; init; }

		public VariableWriteCapability? Write { get; init; }
	}
}
