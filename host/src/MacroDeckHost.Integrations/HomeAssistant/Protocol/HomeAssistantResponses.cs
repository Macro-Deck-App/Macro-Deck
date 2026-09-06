using System.Text.Json;

namespace MacroDeckHost.Integrations.HomeAssistant.Protocol;

internal static class HomeAssistantResponses
{
	public static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyServices { get; }
		= new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

	public static IReadOnlyDictionary<string, string> EmptyEntityAreas { get; }
		= new Dictionary<string, string>(StringComparer.Ordinal);

	public static IReadOnlyList<HomeAssistantEntityState> ReadStates(JsonElement result)
	{
		if (result.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var states = new List<HomeAssistantEntityState>(result.GetArrayLength());
		foreach (var entry in result.EnumerateArray())
		{
			if (ReadState(entry) is { } state)
			{
				states.Add(state);
			}
		}

		return states;
	}

	public static HomeAssistantEntityState? ReadState(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object ||
			ReadString(element, "entity_id") is not { Length: > 0 } entityId)
		{
			return null;
		}

		var attributes = element.TryGetProperty("attributes", out var attributesElement) &&
			attributesElement.ValueKind == JsonValueKind.Object
				? attributesElement.Clone()
				: default;

		return new HomeAssistantEntityState(entityId, ReadString(element, "state") ?? string.Empty, attributes);
	}

	public static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadServices(JsonElement result)
	{
		if (result.ValueKind != JsonValueKind.Object)
		{
			return EmptyServices;
		}

		var services = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
		foreach (var domain in result.EnumerateObject())
		{
			if (domain.Value.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var names = new List<string>();
			foreach (var service in domain.Value.EnumerateObject())
			{
				names.Add(service.Name);
			}

			if (names.Count > 0)
			{
				names.Sort(StringComparer.Ordinal);
				services[domain.Name] = names;
			}
		}

		return services;
	}

	public static (string? LocationName, string? Version) ReadConfig(JsonElement result)
		=> result.ValueKind == JsonValueKind.Object
			? (ReadString(result, "location_name"), ReadString(result, "version"))
			: (null, null);

	public static IReadOnlyList<HomeAssistantAreaInfo> ReadAreas(JsonElement result)
	{
		if (result.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var areas = new List<HomeAssistantAreaInfo>(result.GetArrayLength());
		foreach (var entry in result.EnumerateArray())
		{
			if (ReadString(entry, "area_id") is { Length: > 0 } areaId)
			{
				areas.Add(new HomeAssistantAreaInfo(areaId, ReadString(entry, "name") ?? areaId));
			}
		}

		return areas;
	}

	public static IReadOnlyList<HomeAssistantDeviceInfo> ReadDevices(JsonElement result)
	{
		if (result.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var devices = new List<HomeAssistantDeviceInfo>(result.GetArrayLength());
		foreach (var entry in result.EnumerateArray())
		{
			if (ReadString(entry, "id") is not { Length: > 0 } deviceId)
			{
				continue;
			}

			var name = ReadString(entry, "name_by_user") ?? ReadString(entry, "name") ?? deviceId;
			devices.Add(new HomeAssistantDeviceInfo(deviceId, name, ReadString(entry, "area_id")));
		}

		return devices;
	}

	public static IReadOnlyDictionary<string, string> ReadEntityAreas(
		JsonElement entityRegistry,
		IReadOnlyList<HomeAssistantAreaInfo> areas,
		IReadOnlyList<HomeAssistantDeviceInfo> devices)
	{
		if (entityRegistry.ValueKind != JsonValueKind.Array || areas.Count == 0)
		{
			return EmptyEntityAreas;
		}

		var areaNames = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var area in areas)
		{
			areaNames[area.AreaId] = area.Name;
		}

		var deviceAreas = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var device in devices)
		{
			if (device.AreaId is { Length: > 0 } deviceArea)
			{
				deviceAreas[device.DeviceId] = deviceArea;
			}
		}

		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var entry in entityRegistry.EnumerateArray())
		{
			if (ReadString(entry, "entity_id") is not { Length: > 0 } entityId)
			{
				continue;
			}

			var areaId = ReadString(entry, "area_id");
			if (areaId is null && ReadString(entry, "device_id") is { } deviceId)
			{
				deviceAreas.TryGetValue(deviceId, out areaId);
			}

			if (areaId is not null && areaNames.TryGetValue(areaId, out var name))
			{
				result[entityId] = name;
			}
		}

		return result;
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
}
