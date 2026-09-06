using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal static class HomeAssistantOptions
{
	internal const int MaxOptions = 200;

	private const int CacheSeconds = 30;

	public static DynamicOptionsResult Entities(
		HomeAssistantConnection? connection,
		string? filter,
		string? domain = null)
	{
		var catalog = connection?.Catalog ?? HomeAssistantCatalog.Empty;
		var options = new List<ActionParameterOption>();

		foreach (var state in catalog.Entities.Values)
		{
			if (domain is { Length: > 0 } && !string.Equals(state.Domain, domain, StringComparison.Ordinal))
			{
				continue;
			}

			var option = EntityOption(state, catalog.AreaOf(state.EntityId));
			if (Matches(option.Value, option.Label.Literal, filter))
			{
				options.Add(option);
			}
		}

		options.Sort(static (left, right)
			=> string.Compare(left.Label.Literal, right.Label.Literal, StringComparison.OrdinalIgnoreCase));

		return Result(options.Count > MaxOptions ? options.GetRange(0, MaxOptions) : options);
	}

	public static ActionParameterOption EntityOption(HomeAssistantEntityState state, string? area)
	{
		var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["domain"] = state.Domain };
		if (state.FriendlyName is { Length: > 0 } friendlyName)
		{
			metadata["friendlyName"] = friendlyName;
		}

		if (area is { Length: > 0 })
		{
			metadata["area"] = area;
		}

		return new ActionParameterOption
		{
			Value = state.EntityId,
			Label = state.FriendlyName is { Length: > 0 } name ? $"{name} ({state.EntityId})" : state.EntityId,
			Metadata = metadata
		};
	}

	public static DynamicOptionsResult Domains(HomeAssistantConnection? connection, string? filter)
		=> Values((connection?.Catalog ?? HomeAssistantCatalog.Empty).Domains, filter);

	public static DynamicOptionsResult Services(HomeAssistantConnection? connection, string? filter, string? domain)
	{
		var catalog = connection?.Catalog ?? HomeAssistantCatalog.Empty;

		if (domain is not { Length: > 0 })
		{
			var all = new SortedSet<string>(StringComparer.Ordinal);
			foreach (var services in catalog.Services.Values)
			{
				foreach (var service in services)
				{
					all.Add(service);
				}
			}

			return Values(all, filter);
		}

		return Values(catalog.Services.GetValueOrDefault(domain) ?? [], filter);
	}

	public static DynamicOptionsResult Areas(HomeAssistantConnection? connection, string? filter)
	{
		var areas = (connection?.Catalog ?? HomeAssistantCatalog.Empty).Areas;
		var options = new List<ActionParameterOption>();

		foreach (var area in areas)
		{
			if (Matches(area.AreaId, area.Name, filter))
			{
				options.Add(new ActionParameterOption { Value = area.AreaId, Label = area.Name });
			}
		}

		options.Sort(static (left, right)
			=> string.Compare(left.Label.Literal, right.Label.Literal, StringComparison.OrdinalIgnoreCase));

		return Result(options);
	}

	public static DynamicOptionsResult Devices(HomeAssistantConnection? connection, string? filter)
	{
		var devices = (connection?.Catalog ?? HomeAssistantCatalog.Empty).Devices;
		var options = new List<ActionParameterOption>();

		foreach (var device in devices)
		{
			if (Matches(device.DeviceId, device.Name, filter))
			{
				options.Add(new ActionParameterOption { Value = device.DeviceId, Label = device.Name });
			}
		}

		options.Sort(static (left, right)
			=> string.Compare(left.Label.Literal, right.Label.Literal, StringComparison.OrdinalIgnoreCase));

		return Result(options.Count > MaxOptions ? options.GetRange(0, MaxOptions) : options);
	}

	public static DynamicOptionsResult Attributes(
		HomeAssistantConnection? connection,
		string? filter,
		string? entityId)
		=> Values(connection?.Entity(entityId)?.AttributeNames() ?? [], filter);

	public static DynamicOptionsResult AttributeValues(
		HomeAssistantConnection? connection,
		string? filter,
		string? entityId,
		string attribute)
		=> Values(connection?.Entity(entityId)?.ReadStringList(attribute) ?? [], filter);

	public static DynamicOptionsResult EventTypes(HomeAssistantConnection? connection, string? filter)
	{
		var types = connection?.ObservedEventTypes ?? [];
		return Values(new SortedSet<string>(types, StringComparer.Ordinal), filter);
	}

	public static DynamicOptionsResult Values(IEnumerable<string> values, string? filter)
	{
		var options = new List<ActionParameterOption>();
		foreach (var value in values)
		{
			if (Matches(value, null, filter))
			{
				options.Add(new ActionParameterOption { Value = value });
			}

			if (options.Count == MaxOptions)
			{
				break;
			}
		}

		return Result(options);
	}

	private static DynamicOptionsResult Result(IReadOnlyList<ActionParameterOption> options)
		=> new()
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = CacheSeconds
		};

	private static bool Matches(string value, string? label, string? filter)
		=> filter is not { Length: > 0 } ||
			value.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
			label?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
}
