namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class SharedVariableDto
{
	public string Name { get; set; } = string.Empty;
	public string Type { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;
	public bool Present { get; set; }
	public bool Available { get; set; }
	public bool CanWrite { get; set; }
	public bool CommitOnRelease { get; set; }
	public int? DecimalPlaces { get; set; }
	public string? Unit { get; set; }
	public double? Min { get; set; }
	public double? Max { get; set; }
	public double? Step { get; set; }
}
