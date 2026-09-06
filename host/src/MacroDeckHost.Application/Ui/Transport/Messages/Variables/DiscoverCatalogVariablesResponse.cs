namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class DiscoverCatalogVariablesResponse
{
	public List<VariableCatalogNodeDto> Nodes { get; set; } = new();

	/// <summary>Pass back as <c>Cursor</c> to read the next page. <c>null</c> once the provider reports
	/// no further page.</summary>
	public string? NextCursor { get; set; }

	public bool HasMore { get; set; }

	/// <summary>
	/// <c>false</c> means the provider could not be reached at all - not merely that it answered with
	/// zero resources. The client must render an error state, not an empty-tree state, when this is
	/// <c>false</c>; <see cref="Nodes"/> is then always empty.
	/// </summary>
	public bool Available { get; set; } = true;
}
