using MacroDeck.Localization;

namespace MacroDeck.Sdk.Variables;

/// <summary>The outcome of <see cref="IVariableProvider.SetValueAsync"/>.</summary>
public enum VariableWriteStatus
{
	/// <summary>
	/// The provider applied the write. It does <em>not</em> mean the host has seen the resulting value:
	/// the authoritative value still arrives on the read side, and a provider that clamps, quantizes or
	/// otherwise adjusts what it was given reports the adjusted value there.
	/// </summary>
	Applied = 0,

	/// <summary>This variable cannot be written at all. The host answers this itself for a definition
	/// that declares no <see cref="VariableDefinition.Write"/>, without calling the provider.</summary>
	NotWritable = 1,

	/// <summary>No variable of this id belongs to this provider.</summary>
	NotFound = 2,

	/// <summary>Writable, but not right now - the backing resource is disconnected or busy. A retry
	/// later may succeed, which is why this is not <see cref="Failed"/>.</summary>
	Unavailable = 3,

	/// <summary>The value does not fit the variable's declared type, or is otherwise unusable.</summary>
	InvalidValue = 4,

	/// <summary>The write was attempted and did not succeed. Also what an unrecognised status degrades to.</summary>
	Failed = 5
}

/// <summary>The result of a write, carrying an optional explanation a client may show the user.</summary>
public sealed record VariableWriteResult
{
	public required VariableWriteStatus Status { get; init; }

	/// <summary>
	/// Optional detail for a non-<see cref="VariableWriteStatus.Applied"/> outcome. The host has its own
	/// localized message for every status, so a provider only sets this when it can say something the
	/// status alone does not.
	/// </summary>
	public LocalizedText Message { get; init; }

	public static VariableWriteResult Applied() => new() { Status = VariableWriteStatus.Applied };

	public static VariableWriteResult NotWritable(LocalizedText message = default)
		=> new() { Status = VariableWriteStatus.NotWritable, Message = message };

	public static VariableWriteResult NotFound(LocalizedText message = default)
		=> new() { Status = VariableWriteStatus.NotFound, Message = message };

	public static VariableWriteResult Unavailable(LocalizedText message = default)
		=> new() { Status = VariableWriteStatus.Unavailable, Message = message };

	public static VariableWriteResult InvalidValue(LocalizedText message = default)
		=> new() { Status = VariableWriteStatus.InvalidValue, Message = message };

	public static VariableWriteResult Failed(LocalizedText message = default)
		=> new() { Status = VariableWriteStatus.Failed, Message = message };
}
