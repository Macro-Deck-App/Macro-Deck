namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// A value a flow contributes to the persisted config entry on completion (e.g. OAuth tokens that
/// were never form fields). Secret values are stored encrypted; plain values are stored as-is.
/// </summary>
public sealed record ConfigFlowValue
{
	private ConfigFlowValue(string? value, bool isSecret)
	{
		Value = value;
		IsSecret = isSecret;
	}

	public string? Value { get; }

	public bool IsSecret { get; }

	public static ConfigFlowValue Plain(string? value) => new(value, false);

	public static ConfigFlowValue Secret(string? value) => new(value, true);
}
