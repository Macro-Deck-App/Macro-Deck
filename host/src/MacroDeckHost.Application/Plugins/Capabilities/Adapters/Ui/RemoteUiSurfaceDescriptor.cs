namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;

// One surface a remote provider declared it can serve. Kind and mode stay strings because
// both vocabularies are open in the UI model.
public sealed record RemoteUiSurfaceDescriptor
{
	public required string Kind { get; init; }

	public required string SessionMode { get; init; }
}
