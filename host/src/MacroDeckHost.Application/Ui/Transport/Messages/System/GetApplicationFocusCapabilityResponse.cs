using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.System;

public class GetApplicationFocusCapabilityResponse
{
	public bool Supported { get; set; }

	public string? UnsupportedReason { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ApplicationIdentityKind PreferredIdentityKind { get; set; }
}
