using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Integrations;

public sealed class IntegrationAvailabilityChangedEventArgs : EventArgs
{
	public required string IntegrationId { get; init; }

	public required bool IsAvailable { get; init; }
}

public interface IIntegrationRegistry
{
	IReadOnlyList<IIntegration> Integrations { get; }

	/// <summary>Raised when an integration is disabled, re-enabled or unregistered, so a consumer holding
	/// live state for it can tear that state down instead of waiting for a call that will never come.</summary>
	event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged;

	IActionDefinition? FindAction(string integrationId, string actionId);

	IActionDefinition? FindAction(QualifiedId id);

	IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true);

	bool IsEnabled(string integrationId);

	void SetEnabled(string integrationId, bool enabled);

	IntegrationOrigin GetOrigin(string integrationId);

	Task<IntegrationRegistrationResult> RegisterAsync(
		IIntegration integration,
		IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
		IntegrationMetadata? metadata = null);

	Task<bool> UnregisterAsync(string integrationId);

	bool IsExplicitlyDisabled(string integrationId) => false;

	long DisabledVersion(string integrationId) => 0;

	bool ClearDisabledChoice(string integrationId) => false;

	void ClearEnabledChoice(string integrationId)
	{
	}
}
