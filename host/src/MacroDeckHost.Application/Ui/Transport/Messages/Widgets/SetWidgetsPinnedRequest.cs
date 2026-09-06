using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class SetWidgetsPinnedRequest
{
	public string FolderId { get; set; } = string.Empty;

	public List<string> WidgetIds { get; set; } = [];

	public bool Pinned { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public PinScope? Scope { get; set; }
}
