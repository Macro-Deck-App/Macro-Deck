using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class CreateIconPackRequest
{
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public string? Author { get; set; }
	public string? Version { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public IconPackAiAssets? AiAssets { get; set; }
}
