namespace MacroDeckHost.Application.Announcements;

public interface IAnnouncementService
{
	event Action? Changed;

	Announcement? Pending { get; }

	Task Refresh(CancellationToken cancellationToken);

	Task MarkSeen(int number, CancellationToken cancellationToken);
}
