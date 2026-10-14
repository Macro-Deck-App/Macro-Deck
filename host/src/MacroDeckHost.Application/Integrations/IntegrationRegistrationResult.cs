using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Integrations;

public sealed record IntegrationRegistrationResult(
	bool Registered,
	IntegrationRegistrationFailure Failure = IntegrationRegistrationFailure.None,
	IReadOnlyList<CapabilityIdConflict>? Conflicts = null)
{
	public static IntegrationRegistrationResult Success { get; } = new(true);

	public static IntegrationRegistrationResult DuplicateOwner { get; }
		= new(false, IntegrationRegistrationFailure.DuplicateIntegrationId);

	public static IntegrationRegistrationResult Invalid(IReadOnlyList<CapabilityIdConflict> conflicts)
	{
		return new IntegrationRegistrationResult(false, IntegrationRegistrationFailure.InvalidCapabilityIds, conflicts);
	}

	public string Describe()
	{
		return Conflicts is null or { Count: 0 }
			? string.Empty
			: string.Join(Environment.NewLine, Conflicts.Select(conflict => conflict.ToString()));
	}
}

public enum IntegrationRegistrationFailure
{
	None,
	DuplicateIntegrationId,
	InvalidCapabilityIds
}
