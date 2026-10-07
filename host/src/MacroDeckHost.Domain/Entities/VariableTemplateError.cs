namespace MacroDeckHost.Domain.Entities;

public sealed record VariableTemplateError(string Code, string? Detail = null)
{
	public const string RenderFailed = "RenderFailed";
	public const string NotNumeric = "NotNumeric";
	public const string NotBoolean = "NotBoolean";
	public const string CircularReference = "CircularReference";
}
