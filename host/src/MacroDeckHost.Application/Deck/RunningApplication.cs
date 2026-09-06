using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Deck;

public sealed record RunningApplication(string Identity, ApplicationIdentityKind IdentityKind, string Label);
