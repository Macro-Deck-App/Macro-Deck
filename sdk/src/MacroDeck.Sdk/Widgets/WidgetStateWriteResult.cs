namespace MacroDeck.Sdk.Widgets;

/// <summary>Why a <see cref="IWidgetApi.SetStateAsync" />/<see cref="IWidgetApi.AdvanceStateAsync" /> write was refused.</summary>
public enum WidgetStateWriteError
{
	/// <summary>The target does not exist, is not an action button, or has State Mode disabled.</summary>
	NotFound,

	/// <summary>A state provider is authoritative for the target; its state cannot be set explicitly.</summary>
	ProviderActive,

	/// <summary>A state mapping is authoritative for the target; its state cannot be set explicitly.</summary>
	MappingActive,

	/// <summary>The requested state id does not name one of the target's states.</summary>
	UnknownState
}

/// <summary>Outcome of an explicit state write. Nothing is written in any failure case.</summary>
public sealed record WidgetStateWriteResult(bool Success, WidgetStateWriteError? Error, string? StateId)
{
	public static WidgetStateWriteResult Succeeded(string stateId) => new(true, null, stateId);

	public static WidgetStateWriteResult Failed(WidgetStateWriteError error) => new(false, error, null);
}
