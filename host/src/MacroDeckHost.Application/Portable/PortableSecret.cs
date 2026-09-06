using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Portable;

public sealed class PortableSecret
{
	public Guid Id { get; set; }

	public SecretKind Kind { get; set; }

	public string Value { get; set; } = string.Empty;
}
