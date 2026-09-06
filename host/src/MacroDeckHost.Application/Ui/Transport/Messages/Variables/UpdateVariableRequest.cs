namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class UpdateVariableRequest
{
	public string Id { get; set; } = string.Empty;
	public string? Name { get; set; }
	public string? Value { get; set; }
	public int? DecimalPlaces { get; set; }
}
