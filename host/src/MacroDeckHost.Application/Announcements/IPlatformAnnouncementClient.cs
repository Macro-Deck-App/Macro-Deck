namespace MacroDeckHost.Application.Announcements;

public interface IPlatformAnnouncementClient
{
	Task<AnnouncementFetch> GetLatest(CancellationToken cancellationToken);
}
