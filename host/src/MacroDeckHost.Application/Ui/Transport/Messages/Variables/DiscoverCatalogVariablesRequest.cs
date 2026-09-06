namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class DiscoverCatalogVariablesRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	/// <summary>The node whose children are wanted, or <c>null</c> for the roots.</summary>
	public string? ParentId { get; set; }

	/// <summary>A user-typed substring. Only honoured when the provider reports
	/// <c>SupportsSearch</c>; ignored otherwise rather than filtering a fetched page locally.</summary>
	public string? Search { get; set; }

	/// <summary>Opaque continuation token from a previous response's <c>NextCursor</c>. Round-tripped to
	/// the provider verbatim - never parsed or synthesized by the host.</summary>
	public string? Cursor { get; set; }

	public int? Limit { get; set; }
}
