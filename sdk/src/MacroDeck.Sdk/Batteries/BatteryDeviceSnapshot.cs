namespace MacroDeck.Sdk.Batteries;

/// <summary>A published battery device as every reader sees it, qualified with the provider that published it.</summary>
public sealed record BatteryDeviceSnapshot
{
	/// <summary>The device's id across all providers: <c>&lt;provider id&gt;:&lt;local id&gt;</c>. Stable while the provider keeps the local id.</summary>
	public required string Id { get; init; }

	/// <summary>The id the provider published the device under.</summary>
	public required string LocalId { get; init; }

	/// <summary>The plugin or integration that published the device.</summary>
	public required string ProviderId { get; init; }

	/// <summary>The provider's display name.</summary>
	public string ProviderName { get; init; } = string.Empty;

	public required string Name { get; init; }

	/// <summary>One of <see cref="BatteryDeviceKinds" />; a kind this Macro Deck does not know reads as <see cref="BatteryDeviceKinds.Other" />.</summary>
	public string Kind { get; init; } = BatteryDeviceKinds.Other;

	/// <summary>Charge level from 0 to 100, or <c>null</c> when unknown.</summary>
	public int? LevelPercent { get; init; }

	public BatteryChargeState ChargeState { get; init; }

	public bool IsConnected { get; init; }

	public TimeSpan? TimeRemaining { get; init; }

	public TimeSpan? TimeToFull { get; init; }

	public BatteryHealth Health { get; init; }

	/// <summary>When Macro Deck last received a report for the device.</summary>
	public DateTimeOffset UpdatedAt { get; init; }
}
