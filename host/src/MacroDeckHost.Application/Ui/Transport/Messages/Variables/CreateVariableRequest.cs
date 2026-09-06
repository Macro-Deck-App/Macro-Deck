namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class CreateVariableRequest
{
	public string Name { get; set; } = string.Empty;
	public string Scope { get; set; } = "global";
	public string? ScopeRefId { get; set; }
	public string Type { get; set; } = "text";
	public string? InitialValue { get; set; }
	public int? DecimalPlaces { get; set; }
}
