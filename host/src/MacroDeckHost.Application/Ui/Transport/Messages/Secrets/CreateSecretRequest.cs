using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Secrets;

public class CreateSecretRequest
{
	public string Value { get; set; } = string.Empty;

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public SecretKind Kind { get; set; }
}
