using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Secrets;

public sealed record SecretMaterial(SecretKind Kind, string Value);
