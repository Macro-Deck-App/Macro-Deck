namespace MacroDeckHost.Application.Portable;

public sealed class PortableArchiveContents
{
	public string Name { get; set; } = string.Empty;

	public int FolderCount { get; set; }

	public int WidgetCount { get; set; }

	public int IconCount { get; set; }

	public int ScriptCount { get; set; }

	public int SecretCount { get; set; }

	public int VariableCount { get; set; }

	public List<PortableIntegrationRequirement> Integrations { get; set; } = [];
}
