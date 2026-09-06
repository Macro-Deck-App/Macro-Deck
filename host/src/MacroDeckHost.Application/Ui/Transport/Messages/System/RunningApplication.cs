using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.System;

public class RunningApplication
{
	public string Identity { get; set; } = string.Empty;

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ApplicationIdentityKind IdentityKind { get; set; }

	public string Label { get; set; } = string.Empty;
}
