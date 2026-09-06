using MacroDeckHost.Application.Integrations;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Variables;

// Resolves an integration id to a live IVariableProvider. The single place that answers "does this
// integration truthfully offer variables right now" - registered, enabled, initialized, and actually
// declaring the capability rather than merely implementing the interface.
//
// A RemotePluginIntegration implements every provider interface unconditionally (ADR 0004), so a bare
// "is IVariableProvider" cast is not a truthful signal for an out-of-process plugin. When the integration
// implements IDeclaredCapabilityKinds, CapabilityKinds.Variables must be in its declared set; otherwise
// the cast alone is enough (see ProvidedCapabilityCatalog's remarks on the same distinction). Every other
// file that needs this answer goes through here rather than repeating the predicate.
//
// Resolve and GetAvailable answer for the *catalog* half only. Since IVariableProvider absorbed
// IDynamicVariableProvider every provider satisfies the cast, so without the SupportsCatalog gate every
// integration would sprout an empty browse tree. ResolveOwner deliberately skips that gate: a write
// targets the declaration the entity already carries, and an eager-only provider - the System volume, a
// Voicemeeter gain - is writable without offering a catalog at all.
public sealed class VariableCatalogProviders
{
	private readonly IIntegrationRegistry _integrations;

	public VariableCatalogProviders(IIntegrationRegistry integrations)
	{
		_integrations = integrations;
	}

	// The catalog provider for integrationId, or null when it is not currently available - unregistered,
	// disabled, not yet initialized, not declaring the capability, or declaring no catalog.
	public IVariableProvider? Resolve(string integrationId)
		=> ResolveOwner(integrationId) is { SupportsCatalog: true } provider ? provider : null;

	/// <summary>The variable provider that owns <paramref name="integrationId"/>'s variables, catalog or
	/// not, or null when it is not currently available.</summary>
	public IVariableProvider? ResolveOwner(string integrationId)
	{
		var integration = Find(integrationId);
		return integration is not null && IsAvailable(integration) ? integration as IVariableProvider : null;
	}

	/// <summary>Every integration currently offering a variable catalog, alongside its id.</summary>
	public IReadOnlyList<(string IntegrationId, IVariableProvider Provider)> GetAvailable()
	{
		var results = new List<(string, IVariableProvider)>();
		foreach (var integration in _integrations.Integrations)
		{
			if (IsAvailable(integration) && integration is IVariableProvider { SupportsCatalog: true } provider)
			{
				results.Add((integration.Id, provider));
			}
		}

		return results;
	}

	private IIntegration? Find(string integrationId)
		=> _integrations.Integrations.FirstOrDefault(i =>
			string.Equals(i.Id, integrationId, StringComparison.Ordinal));

	private bool IsAvailable(IIntegration integration)
		=> _integrations.IsEnabled(integration.Id) && integration.IsInitialized && IsDeclaredProvider(integration);

	private static bool IsDeclaredProvider(IIntegration integration)
		=> integration is IDeclaredCapabilityKinds declared
			? declared.DeclaredCapabilityKinds.Contains(CapabilityKinds.Variables, StringComparer.Ordinal)
			: integration is IVariableProvider;
}
