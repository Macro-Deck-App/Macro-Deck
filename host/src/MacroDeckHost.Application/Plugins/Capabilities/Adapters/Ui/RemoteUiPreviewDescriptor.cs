namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;

// One preview scenario a remote plugin declared. Developer-tooling data only, never persisted - see
// RemotePluginSnapshotStore.
public sealed record RemoteUiPreviewDescriptor
{
	public required string Id { get; init; }

	public required string View { get; init; }

	public required string Scenario { get; init; }

	public required string Profile { get; init; }
}
