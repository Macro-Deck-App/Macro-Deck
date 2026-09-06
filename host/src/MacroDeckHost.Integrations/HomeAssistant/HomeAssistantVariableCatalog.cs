using System.Globalization;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.HomeAssistant;

/// <summary>
/// The catalog half of <see cref="HomeAssistantIntegration"/>'s variable provider: every Home Assistant
/// entity and every one of its attributes, so the user can browse and bind whatever they need instead of
/// being limited to the entities picked in the config flow.
/// </summary>
internal sealed class HomeAssistantVariableCatalog
{
	private const string EntityPrefix = "entity/";
	private const string StateChild = "state";
	private const string AttributesChild = "attributes";

	private readonly Func<HomeAssistantCatalog> _catalog;
	private readonly Action<IReadOnlyCollection<string>>? _onSubscriptionChanged;
	private readonly Lock _lock = new();

	private HashSet<string> _subscribed = new(StringComparer.Ordinal);
	private IVariableSink? _sink;

	public HomeAssistantVariableCatalog(
		Func<HomeAssistantCatalog> catalog,
		Action<IReadOnlyCollection<string>>? onSubscriptionChanged = null)
	{
		_catalog = catalog;
		_onSubscriptionChanged = onSubscriptionChanged;
	}

	public static string CatalogName => "Home Assistant";

	public static bool SupportsPush => true;

	public static bool SupportsSearch => true;

	public ValueTask<VariableCatalogPage> DiscoverAsync(
		VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
	{
		var catalog = _catalog();

		if (query.Search is { Length: > 0 } search)
		{
			var matches = catalog.Entities.Values
				.Where(state => Matches(state, search))
				.Select(state => state.EntityId)
				.OrderBy(id => id, StringComparer.Ordinal)
				.ToList();

			return ValueTask.FromResult(PageEntities(matches, catalog, query));
		}

		if (query.ParentId is null)
		{
			var entityIds = catalog.Entities.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
			return ValueTask.FromResult(PageEntities(entityIds, catalog, query));
		}

		return ValueTask.FromResult(DiscoverChildren(catalog, query.ParentId));
	}

	public ValueTask<VariableDefinition?> ResolveAsync(
		string id,
		CancellationToken cancellationToken = default)
	{
		if (ParseId(id) is not { } parsed)
		{
			return ValueTask.FromResult<VariableDefinition?>(null);
		}

		var state = _catalog().Entity(parsed.EntityId);
		if (state is null)
		{
			return ValueTask.FromResult<VariableDefinition?>(null);
		}

		if (parsed.Child is null)
		{
			return ValueTask.FromResult<VariableDefinition?>(EntityContainer(state));
		}

		if (parsed.Child == StateChild)
		{
			return ValueTask.FromResult<VariableDefinition?>(StateLeaf(state));
		}

		if (parsed.Child == AttributesChild)
		{
			return ValueTask.FromResult<VariableDefinition?>(AttributesCompatLeaf(state));
		}

		if (state.Attributes.ValueKind == JsonValueKind.Object && state.Attributes.TryGetProperty(parsed.Child, out _))
		{
			return ValueTask.FromResult<VariableDefinition?>(AttributeLeaf(state, parsed.Child));
		}

		return ValueTask.FromResult<VariableDefinition?>(null);
	}

	public ValueTask<VariableReading> ReadAsync(string id, CancellationToken cancellationToken = default)
	{
		if (ParseId(id) is not { Child: { } child } parsed)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var state = _catalog().Entity(parsed.EntityId);
		return ValueTask.FromResult(state is null
			? VariableReading.Unavailable
			: VariableReading.Of(ReadValue(state, child)));
	}

	public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
		IReadOnlyCollection<string> ids,
		CancellationToken cancellationToken = default)
	{
		var subscribed = new HashSet<string>(ids, StringComparer.Ordinal);
		lock (_lock)
		{
			_subscribed = subscribed;
		}

		var catalog = _catalog();
		var values = new List<VariableValue>(ids.Count);
		var entityIds = new HashSet<string>(StringComparer.Ordinal);

		foreach (var id in ids)
		{
			if (ParseId(id) is not { Child: { } child } parsed)
			{
				values.Add(VariableValue.Unavailable(id));
				continue;
			}

			entityIds.Add(parsed.EntityId);
			var state = catalog.Entity(parsed.EntityId);
			values.Add(state is null
				? VariableValue.Unavailable(id)
				: VariableValue.Of(id, ReadValue(state, child)));
		}

		_onSubscriptionChanged?.Invoke(entityIds);

		return ValueTask.FromResult<IReadOnlyList<VariableValue>>(values);
	}

	public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken = default)
	{
		_sink = sink;
		return Task.CompletedTask;
	}

	/// <summary>
	/// Called by <see cref="HomeAssistantConnection"/> whenever an entity's state arrives, whether from a
	/// live <c>state_changed</c> event or from the fresh snapshot after a reconnect. Publishes only the
	/// resources of that entity the host is currently subscribed to.
	/// </summary>
	public void Push(HomeAssistantEntityState state)
	{
		var sink = _sink;
		if (sink is null)
		{
			return;
		}

		HashSet<string> subscribed;
		lock (_lock)
		{
			subscribed = _subscribed;
		}

		if (subscribed.Count == 0)
		{
			return;
		}

		var containerId = EntityPrefix + state.EntityId;
		var values = new List<VariableValue>();

		void Consider(string resourceId, string child)
		{
			if (subscribed.Contains(resourceId))
			{
				values.Add(VariableValue.Of(resourceId, ReadValue(state, child)));
			}
		}

		Consider(containerId + "/" + StateChild, StateChild);
		Consider(containerId + "/" + AttributesChild, AttributesChild);
		foreach (var attributeName in state.AttributeNames())
		{
			Consider(containerId + "/" + attributeName, attributeName);
		}

		if (values.Count > 0)
		{
			_ = sink.PublishAsync(values);
		}
	}

	/// <summary>
	/// Publishes the current value of every currently subscribed resource. Used after a reconnect instead
	/// of replaying <see cref="Push"/> for every entity in the catalogue: that would mean scanning
	/// 1500-3000 entities' worth of attributes to refresh the handful actually bound, and doing so without
	/// awaiting the publish.
	/// </summary>
	public async Task PublishSubscribedAsync(CancellationToken cancellationToken = default)
	{
		var sink = _sink;
		if (sink is null)
		{
			return;
		}

		HashSet<string> subscribed;
		lock (_lock)
		{
			subscribed = _subscribed;
		}

		if (subscribed.Count == 0)
		{
			return;
		}

		var catalog = _catalog();
		var values = new List<VariableValue>(subscribed.Count);

		foreach (var id in subscribed)
		{
			if (ParseId(id) is not { Child: { } child } parsed)
			{
				continue;
			}

			var state = catalog.Entity(parsed.EntityId);
			values.Add(state is null
				? VariableValue.Unavailable(id)
				: VariableValue.Of(id, ReadValue(state, child)));
		}

		if (values.Count > 0)
		{
			await sink.PublishAsync(values, cancellationToken).ConfigureAwait(false);
		}
	}

	private static VariableCatalogPage PageEntities(
		List<string> orderedEntityIds,
		HomeAssistantCatalog catalog,
		VariableCatalogQuery query)
	{
		var startIndex = 0;
		if (query.ContinuationToken is { Length: > 0 } token)
		{
			while (startIndex < orderedEntityIds.Count &&
				string.CompareOrdinal(orderedEntityIds[startIndex], token) <= 0)
			{
				startIndex++;
			}
		}

		var pageSize = Math.Max(query.PageSize, 0);
		var pageIds = orderedEntityIds.Skip(startIndex).Take(pageSize).ToList();
		var items = pageIds
			.Select(id => catalog.Entity(id))
			.Where(state => state is not null)
			.Select(state => EntityContainer(state!))
			.ToList();

		var hasMore = startIndex + pageIds.Count < orderedEntityIds.Count;

		return new VariableCatalogPage
		{
			Items = items,
			ContinuationToken = hasMore ? pageIds[^1] : null
		};
	}

	private static VariableCatalogPage DiscoverChildren(HomeAssistantCatalog catalog, string parentId)
	{
		if (ParseId(parentId) is not { Child: null } parsed)
		{
			return VariableCatalogPage.Empty;
		}

		var state = catalog.Entity(parsed.EntityId);
		if (state is null)
		{
			return VariableCatalogPage.Empty;
		}

		var items = new List<VariableDefinition> { StateLeaf(state) };
		foreach (var attributeName in state.AttributeNames())
		{
			items.Add(AttributeLeaf(state, attributeName));
		}

		// Compatibility leaf carrying the whole attribute set as one JSON-text value, matching what the
		// old ha_<entity>_attributes variable held. Not asked for by the issue itself, but required so
		// HomeAssistantWatchedEntityMigration's bindings - which point at this exact resource id - keep
		// resolving once the config-flow watched-entity picker that created them is gone.
		items.Add(AttributesCompatLeaf(state));

		return new VariableCatalogPage { Items = items };
	}

	private static bool Matches(HomeAssistantEntityState state, string search)
		=> state.EntityId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			(state.FriendlyName is { Length: > 0 } name && name.Contains(search, StringComparison.OrdinalIgnoreCase));

	private static VariableDefinition EntityContainer(HomeAssistantEntityState state)
		=> VariableDefinition.OnDemand(EntityPrefix + state.EntityId, VariableType.Text) with
		{
			Name = SuggestedEntityName(state.EntityId),
			DisplayName = state.EntityId,
			Description = state.FriendlyName,
			IsContainer = true,
			IsBindable = false
		};

	private static VariableDefinition StateLeaf(HomeAssistantEntityState state)
		=> VariableDefinition.OnDemand(EntityPrefix + state.EntityId + "/" + StateChild, InferStateType(state)) with
		{
			Name = SuggestedEntityName(state.EntityId),
			DisplayName = StateChild,
			ParentId = EntityPrefix + state.EntityId
		};

	private static VariableDefinition AttributesCompatLeaf(HomeAssistantEntityState state)
		=> VariableDefinition.OnDemand(EntityPrefix + state.EntityId + "/" + AttributesChild, VariableType.Text) with
		{
			Name = SuggestedEntityName(state.EntityId) + "_" + AttributesChild,
			DisplayName = AttributesChild,
			ParentId = EntityPrefix + state.EntityId
		};

	private static VariableDefinition AttributeLeaf(HomeAssistantEntityState state, string name)
		=> VariableDefinition.OnDemand(EntityPrefix + state.EntityId + "/" + name, InferAttributeType(state, name))
			with
			{
				Name = SuggestedEntityName(state.EntityId) + "_" + Sanitize(name),
				DisplayName = name,
				ParentId = EntityPrefix + state.EntityId
			};

	private static object? ReadValue(HomeAssistantEntityState state, string child)
	{
		if (child == StateChild)
		{
			// Always the raw string Home Assistant reported, regardless of InferStateType's Numeric/Boolean
			// guess for the definition's default type: that guess only chooses which VariableType a fresh
			// binding defaults to, and VariableValueSerializer.Serialize(type, raw) already converts a raw
			// string correctly for Text and Numeric. Returning anything else here would make that
			// conversion lossy - most visibly for HomeAssistantWatchedEntityMigration, whose bindings stay
			// Text forever and must serialize back out exactly as SetValueAsync(entry.StateId, state.State)
			// used to store them (e.g. "on", not "True").
			return state.State;
		}

		if (child == AttributesChild)
		{
			return state.AttributesJson;
		}

		if (state.Attributes.ValueKind != JsonValueKind.Object ||
			!state.Attributes.TryGetProperty(child, out var value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.Number => value.TryGetDouble(out var number) ? number : null,
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			_ => value.GetRawText()
		};
	}

	// No Boolean inference here on purpose. binary_sensor/switch/light states are "on"/"off" strings, and
	// VariableValueSerializer (out of this project's reach) only treats "true"/"1" as truthy for a Boolean
	// variable - it would silently turn "on" into "false". Defaulting those states to Text instead is the
	// only choice that cannot mis-render a bound value; a user who wants a real boolean can still override
	// the type at bind time.
	private static VariableType InferStateType(HomeAssistantEntityState state)
	{
		if (string.Equals(state.Domain, "sensor", StringComparison.Ordinal) &&
			state.UnitOfMeasurement is { Length: > 0 } &&
			double.TryParse(state.State, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
		{
			return VariableType.Numeric;
		}

		return VariableType.Text;
	}

	private static VariableType InferAttributeType(HomeAssistantEntityState state, string name)
	{
		if (state.Attributes.ValueKind != JsonValueKind.Object || !state.Attributes.TryGetProperty(name, out var value))
		{
			return VariableType.Text;
		}

		return value.ValueKind switch
		{
			JsonValueKind.Number => VariableType.Numeric,
			JsonValueKind.True or JsonValueKind.False => VariableType.Boolean,
			_ => VariableType.Text
		};
	}

	private static string SuggestedEntityName(string entityId) => "ha_" + Sanitize(entityId);

	private static string Sanitize(string value)
	{
		var builder = new StringBuilder(value.Length);
		foreach (var character in value.ToLowerInvariant())
		{
			builder.Append(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) ? character : '_');
		}

		return builder.ToString();
	}

	private static ParsedId? ParseId(string id)
	{
		if (!id.StartsWith(EntityPrefix, StringComparison.Ordinal))
		{
			return null;
		}

		var rest = id[EntityPrefix.Length..];
		if (rest.Length == 0)
		{
			return null;
		}

		var slash = rest.IndexOf('/', StringComparison.Ordinal);
		if (slash < 0)
		{
			return new ParsedId(rest, null);
		}

		var entityId = rest[..slash];
		var child = rest[(slash + 1)..];
		return entityId.Length > 0 && child.Length > 0 ? new ParsedId(entityId, child) : null;
	}

	private readonly record struct ParsedId(string EntityId, string? Child);
}
