using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class VariableCatalogProviderDto
{
	public string IntegrationId { get; set; } = string.Empty;

	public LocalizedText Name { get; set; }

	public bool SupportsSearch { get; set; }

	/// <summary>
	/// How many of this provider's catalog entries are not bound yet, or null when the provider
	/// cannot say how large its catalog is. Null is not zero - the client then counts what it has
	/// loaded rather than showing a total it does not have.
	/// </summary>
	public int? UnboundCount { get; set; }

	/// <summary>
	/// Whether the client may bind a resource id the browse tree never surfaced, by typing it
	/// directly. Every provider's <c>ResolveAsync</c> contract must handle an arbitrary id (see
	/// <c>IVariableProvider</c>), so this is always <c>true</c> today - the flag exists so the client has
	/// an honest, provider-declared signal rather than assuming the capability.
	/// </summary>
	public bool SupportsManualIds { get; set; }
}

public class GetVariableCatalogProvidersResponse
{
	public List<VariableCatalogProviderDto> Providers { get; set; } = new();
}
