using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class SetFolderFocusRuleRequest
{
	public string FolderId { get; set; } = string.Empty;

	public string? RuleId { get; set; }

	public bool Enabled { get; set; } = true;
	public string ApplicationIdentity { get; set; } = string.Empty;

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ApplicationIdentityKind IdentityKind { get; set; }

	public string DeviceId { get; set; } = string.Empty;
	public bool ReturnOnFocusLoss { get; set; }
}
