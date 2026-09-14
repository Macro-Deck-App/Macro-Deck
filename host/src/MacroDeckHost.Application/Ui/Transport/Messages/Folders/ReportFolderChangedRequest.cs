using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class ReportFolderChangedRequest
{
	public string FolderId { get; set; } = string.Empty;

	public string? ClientId { get; set; }

	public Guid? DeviceId { get; set; }

	public string? NavigationToken { get; set; }

	public bool IsResync { get; set; }

	// Set by the dispatcher from the connection's registration, never from the payload.
	[JsonIgnore]
	public string? RegisteredClientId { get; set; }
}
