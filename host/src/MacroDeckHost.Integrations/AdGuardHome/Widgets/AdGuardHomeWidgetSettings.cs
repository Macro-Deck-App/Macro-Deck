using System.Text.Json;
using MacroDeckHost.Integrations.Ui;

namespace MacroDeckHost.Integrations.AdGuardHome.Widgets;

internal sealed record AdGuardHomeWidgetOptions
{
	public string? InstanceId { get; init; }

	public string? DisplayName { get; init; }

	public string View { get; init; } = AdGuardHomeWidgetType.ControlView;

	public IReadOnlyList<string> Statistics { get; init; } = AdGuardHomeWidgetType.DefaultStatistics;

	public IReadOnlyList<string> Durations { get; init; } = AdGuardHomeWidgetType.DefaultDurations;

	public bool ShowVersion { get; init; } = true;

	public bool ShowStatus { get; init; } = true;
}

internal static class AdGuardHomeWidgetSettings
{
	public static AdGuardHomeWidgetOptions Parse(JsonElement data)
	{
		var view = ReadString(data, AdGuardHomeWidgetType.ViewKey);

		return new AdGuardHomeWidgetOptions
		{
			InstanceId = ReadString(data, AdGuardHomeWidgetType.InstanceKey) is { Length: > 0 } id
				? id
				: null,
			DisplayName = ReadString(data, AdGuardHomeWidgetType.DisplayNameKey)?.Trim() is
				{ Length: > 0 } name
				? name
				: null,
			View = view is not null && AdGuardHomeWidgetType.Views.Contains(view, StringComparer.Ordinal)
				? view
				: AdGuardHomeWidgetType.ControlView,
			Statistics = ReadList(data, AdGuardHomeWidgetType.StatisticsKey, AdGuardHomeWidgetType.Statistics)
				?? AdGuardHomeWidgetType.DefaultStatistics,
			Durations = ReadList(data, AdGuardHomeWidgetType.DurationsKey, AdGuardHomeWidgetType.DurationIds)
				?? AdGuardHomeWidgetType.DefaultDurations,
			ShowVersion = ReadBool(data, AdGuardHomeWidgetType.ShowVersionKey) ?? true,
			ShowStatus = ReadBool(data, AdGuardHomeWidgetType.ShowStatusKey) ?? true
		};
	}

	public static string? BackgroundColor(JsonElement data)
	{
		return WidgetBackgroundColor.Normalize(ReadString(data, AdGuardHomeWidgetType.BackgroundColorKey));
	}

	private static string? ReadString(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}

	private static bool? ReadBool(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: null;
	}

	private static IReadOnlyList<string>? ReadList(JsonElement data, string name, IReadOnlyList<string> known)
	{
		if (data.ValueKind != JsonValueKind.Object
			|| !data.TryGetProperty(name, out var list)
			|| list.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		return
		[
			.. list.EnumerateArray()
				.Where(item => item.ValueKind == JsonValueKind.String)
				.Select(item => item.GetString()!)
				.Where(id => known.Contains(id, StringComparer.Ordinal))
				.Distinct(StringComparer.Ordinal)
		];
	}
}
