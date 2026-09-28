namespace MacroDeckHost.Application.Announcements;

public sealed record Announcement(
	int Number,
	string Title,
	string Content,
	DateTimeOffset PublishedAt,
	DateTimeOffset UpdatedAt);

public abstract record AnnouncementFetch
{
	public sealed record Published(Announcement Announcement) : AnnouncementFetch;

	public sealed record NonePublished : AnnouncementFetch;

	public sealed record Failed : AnnouncementFetch;
}
