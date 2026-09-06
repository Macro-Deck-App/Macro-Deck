using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class FolderFocusRule
{
	public string FolderId { get; set; } = string.Empty;
	public string FolderName { get; set; } = string.Empty;
	public string ProfileId { get; set; } = string.Empty;

	public string RuleId { get; set; } = string.Empty;
	public bool Enabled { get; set; } = true;
	public string ApplicationIdentity { get; set; } = string.Empty;

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ApplicationIdentityKind IdentityKind { get; set; }

	public string DeviceId { get; set; } = string.Empty;

	public string? DeviceName { get; set; }

	public bool DeviceExists { get; set; }

	public bool ReturnOnFocusLoss { get; set; }
}
