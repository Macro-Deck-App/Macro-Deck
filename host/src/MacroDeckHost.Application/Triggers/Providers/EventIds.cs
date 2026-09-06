using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Triggers.Providers;

public static class EventIds
{
	public const string VariableChanged = "variable-changed";

	public const string VariableCreated = "variable-created";

	public const string VariableDeleted = "variable-deleted";

	public const string ProfileChanged = "profile-changed";

	public const string FolderChanged = "folder-changed";

	public const string ClientConnected = "client-connected";

	public const string ClientDisconnected = "client-disconnected";

	public const string IntegrationConnected = "integration-connected";

	public const string IntegrationDisconnected = "integration-disconnected";

	public const string ServerStarted = "server-started";

	public const string ServerStopped = "server-stopped";

	public static string Qualify(string eventId)
		=> QualifiedId.Create(CoreEventProvider.ProviderIdValue,
			eventId,
			OwnerIdKind.HostProvider,
			LocalIdKind.Declared,
			"Event").ToString();
}
