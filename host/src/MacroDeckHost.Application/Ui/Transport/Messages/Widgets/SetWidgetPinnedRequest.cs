using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class SetWidgetPinnedRequest
{
	public string WidgetId { get; set; } = string.Empty;

	public string FolderId { get; set; } = string.Empty;

	public bool Pinned { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public PinScope? Scope { get; set; }
}
