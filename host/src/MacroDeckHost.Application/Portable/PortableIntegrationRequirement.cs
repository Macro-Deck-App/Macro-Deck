namespace MacroDeckHost.Application.Portable;

public sealed class PortableIntegrationRequirement
{
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string Version { get; set; } = string.Empty;

	public bool RequiresConfiguration { get; set; }
}
