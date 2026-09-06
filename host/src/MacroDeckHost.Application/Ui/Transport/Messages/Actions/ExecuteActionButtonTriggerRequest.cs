using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class ExecuteActionButtonTriggerRequest
{
	public string WidgetId { get; set; } = string.Empty;
	public string FolderId { get; set; } = string.Empty;
	public string TriggerType { get; set; } = string.Empty;

	public string? ClientId { get; set; }

	/// <summary>
	/// The device this press came from, set by the host's own device session router and never carried on
	/// the wire - a client cannot ask for a press to be attributed to a device it does not own.
	/// </summary>
	[JsonIgnore]
	public Guid? OriginDeviceId { get; set; }
}
