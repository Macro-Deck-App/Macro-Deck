using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class UpdateIconPackRequest
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public string? Author { get; set; }
	public string? Version { get; set; }

	// Null leaves the declaration unchanged, so a client that predates the field never clears it.
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public IconPackAiAssets? AiAssets { get; set; }
}
