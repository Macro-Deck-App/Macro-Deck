using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Domain.Common;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Triggers;

public static class EventTriggerParser
{
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static bool MightContainEventTriggers(string? flowsSource)
		=> flowsSource is not null && flowsSource.Contains(WidgetTriggerTypes.Event, StringComparison.Ordinal);

	public static IReadOnlyList<EventSubscription> Parse(EventTriggerOwner owner, string? widgetData)
	{
		if (!MightContainEventTriggers(widgetData) || !WidgetFlowsJson.TryExtract(widgetData, out var flowsJson))
		{
			return [];
		}

		return ParseFlows(owner, flowsJson);
	}

	public static EventSubscription? ParseAutomation(EventTriggerOwner owner, string? flowsJson)
	{
		var subscriptions = ParseFlows(owner, flowsJson);
		return subscriptions.Count > 0 ? subscriptions[0] : null;
	}

	public static IReadOnlyList<EventSubscription> ParseFlows(EventTriggerOwner owner, string? flowsJson)
	{
		if (flowsJson is null || !MightContainEventTriggers(flowsJson))
		{
			return [];
		}

		List<FlowDto>? flows;
		try
		{
			flows = JsonSerializer.Deserialize<List<FlowDto>>(flowsJson, _jsonOptions);
		}
		catch (JsonException)
		{
			return [];
		}

		if (flows is null)
		{
			return [];
		}

		var subscriptions = new List<EventSubscription>();
		foreach (var flow in flows)
		{
			if (!string.Equals(flow.TriggerType, WidgetTriggerTypes.Event, StringComparison.OrdinalIgnoreCase) ||
				string.IsNullOrWhiteSpace(flow.TriggerId) ||
				flow.Event is not { } binding ||
				string.IsNullOrWhiteSpace(binding.ProviderId) ||
				string.IsNullOrWhiteSpace(binding.EventId) ||
				!QualifiedId.TryCreate(binding.ProviderId, binding.EventId, out var eventId))
			{
				continue;
			}

			subscriptions.Add(new EventSubscription(owner,
				flow.TriggerId,
				eventId.ToString(),
				ReadConfiguration(binding.Parameters),
				binding.Filter));
		}

		return subscriptions;
	}

	private static IReadOnlyDictionary<string, EventConfigurationValue> ReadConfiguration(
		List<ParameterDto>? parameters)
	{
		if (parameters is null || parameters.Count == 0)
		{
			return EventSubscription.NoConfiguration;
		}

		var values = new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal);
		foreach (var parameter in parameters)
		{
			if (!string.IsNullOrWhiteSpace(parameter.Name))
			{
				values[parameter.Name] = new EventConfigurationValue(parameter.Value, parameter.Operator);
			}
		}

		return values;
	}

	private sealed class FlowDto
	{
		public string TriggerId { get; set; } = string.Empty;
		public string TriggerType { get; set; } = string.Empty;
		public EventBindingDto? Event { get; set; }
	}

	private sealed class EventBindingDto
	{
		public string ProviderId { get; set; } = string.Empty;
		public string EventId { get; set; } = string.Empty;
		public List<ParameterDto>? Parameters { get; set; }

		public JsonElement? Filter { get; set; }
	}

	private sealed class ParameterDto
	{
		public string Name { get; set; } = string.Empty;
		public JsonElement Value { get; set; }

		public string? Operator { get; set; }
	}
}
