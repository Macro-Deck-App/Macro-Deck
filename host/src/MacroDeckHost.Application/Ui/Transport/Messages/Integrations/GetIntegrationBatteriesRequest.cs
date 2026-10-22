using System.Text.Json.Serialization;
using MacroDeck.Sdk.Batteries;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class GetIntegrationBatteriesRequest
{
	public string IntegrationId { get; set; } = string.Empty;
}

public class GetIntegrationBatteriesResponse
{
	public bool Found { get; set; }

	public bool Available { get; set; }

	public List<IntegrationBatteryDto> Batteries { get; set; } = [];
}

public class IntegrationBatteryDto
{
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string Kind { get; set; } = BatteryDeviceKinds.Other;

	public int? LevelPercent { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public BatteryChargeState ChargeState { get; set; }

	public bool IsConnected { get; set; }

	public double? TimeRemainingSeconds { get; set; }

	public double? TimeToFullSeconds { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public BatteryHealth Health { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }

	public static IntegrationBatteryDto From(BatteryDeviceSnapshot snapshot)
	{
		return new IntegrationBatteryDto
		{
			Id = snapshot.Id,
			Name = snapshot.Name,
			Kind = BatteryDeviceKinds.Normalize(snapshot.Kind),
			LevelPercent = snapshot.LevelPercent,
			ChargeState = snapshot.ChargeState,
			IsConnected = snapshot.IsConnected,
			TimeRemainingSeconds = snapshot.TimeRemaining?.TotalSeconds,
			TimeToFullSeconds = snapshot.TimeToFull?.TotalSeconds,
			Health = snapshot.Health,
			UpdatedAt = snapshot.UpdatedAt
		};
	}
}
