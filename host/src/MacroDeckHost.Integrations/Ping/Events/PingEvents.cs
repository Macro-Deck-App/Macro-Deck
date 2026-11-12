using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.Ping.State;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Ping.Events;

namespace MacroDeckHost.Integrations.Ping.Events;

internal static class PingEventIds
{
	public const string TargetOnline = "target-online";
	public const string TargetOffline = "target-offline";
}

internal static class PingEventPayload
{
	public const string Configuration = "configuration";
	public const string Host = "host";
	public const string Latency = "latency";
}

internal static class PingEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = PingEventIds.TargetOnline,
			Name = Strings.TargetOnline.Name(),
			Description = Strings.TargetOnline.Description(),
			Category = Strings.Category(),
			IconName = "wifi",
			ConfigurationParameters = [ConfigurationParameter()],
			PayloadParameters =
			[
				ConfigurationParameter(),
				ActionParameter.Text(PingEventPayload.Host, Strings.Host()),
				ActionParameter.Number(PingEventPayload.Latency, Strings.Latency())
			]
		},
		new()
		{
			Id = PingEventIds.TargetOffline,
			Name = Strings.TargetOffline.Name(),
			Description = Strings.TargetOffline.Description(),
			Category = Strings.Category(),
			IconName = "alert-triangle",
			ConfigurationParameters = [ConfigurationParameter()],
			PayloadParameters = [ConfigurationParameter(), ActionParameter.Text(PingEventPayload.Host, Strings.Host())]
		}
	];

	public static (string Id, IReadOnlyDictionary<string, object?> Payload) For(
		PingSnapshot snapshot,
		PingReachability transition)
	{
		var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[PingEventPayload.Configuration] = snapshot.EntryId.ToString("D"),
			[PingEventPayload.Host] = snapshot.Host
		};

		if (transition == PingReachability.Online)
		{
			payload[PingEventPayload.Latency] = snapshot.LatencyMilliseconds;
			return (PingEventIds.TargetOnline, payload);
		}

		return (PingEventIds.TargetOffline, payload);
	}

	private static ActionParameter ConfigurationParameter()
	{
		return ActionParameter.DynamicChoice(PingEventPayload.Configuration, Strings.Target());
	}
}
