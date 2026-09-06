namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class BindCatalogVariableRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string ResourceId { get; set; } = string.Empty;

	/// <summary>Requested variable name. The host sanitizes and de-duplicates it; a caller may leave it
	/// null to have a name derived from the resource.</summary>
	public string? Name { get; set; }

	/// <summary>Overrides the type the provider declares for this resource. Optional.</summary>
	public string? Type { get; set; }
}
