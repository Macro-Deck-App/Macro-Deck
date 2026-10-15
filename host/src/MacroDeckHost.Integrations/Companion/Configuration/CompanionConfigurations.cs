namespace MacroDeckHost.Integrations.Companion.Configuration;

internal static class CompanionConfigurationMetadata
{
	public const string SchemaKey = "companionConfigurationSchema";
	public const string SchemaVersion = "v1";
	public const string VariableIdentityKey = "companionVariableIdentity";
}

internal sealed record CompanionRuntime(Guid Id, string Title, ConfigurationIdentity Identity);
