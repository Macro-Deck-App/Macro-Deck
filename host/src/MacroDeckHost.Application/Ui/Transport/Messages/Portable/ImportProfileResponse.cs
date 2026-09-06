using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Portable;

public sealed class ImportProfileResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public Profile? Profile { get; set; }
}
