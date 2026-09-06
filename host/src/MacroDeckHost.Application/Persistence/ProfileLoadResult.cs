using MacroDeckHost.Application.Persistence.Profiles;

namespace MacroDeckHost.Application.Persistence;

public sealed record ProfileLoadResult(IReadOnlyList<ProfileFile> Profiles, int UnreadableCount);
