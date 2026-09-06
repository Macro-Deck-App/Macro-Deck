namespace MacroDeckHost.Application.Connect;

public sealed record ConnectAccount(
	string Subject,
	string DisplayName,
	string? PictureUrl,
	string? CreatorUsername,
	IReadOnlyList<string> Roles);
