namespace MacroDeckHost.Integrations.StreamlabsDesktop.Models;

internal enum StreamlabsCommandStatus
{
	Ok,

	NotConnected,

	NotFound,

	Rejected
}

internal readonly record struct StreamlabsCommandResult(StreamlabsCommandStatus Status, string? Message)
{
	public static StreamlabsCommandResult Ok { get; } = new(StreamlabsCommandStatus.Ok, null);

	public static StreamlabsCommandResult NotConnected { get; } =
		new(StreamlabsCommandStatus.NotConnected, "Streamlabs Desktop is not connected.");

	public bool Succeeded => Status == StreamlabsCommandStatus.Ok;

	public static StreamlabsCommandResult NotFound(string message)
	{
		return new StreamlabsCommandResult(StreamlabsCommandStatus.NotFound, message);
	}

	public static StreamlabsCommandResult Rejected(string message)
	{
		return new StreamlabsCommandResult(StreamlabsCommandStatus.Rejected, message);
	}
}
