using MacroDeckHost.Application.Announcements;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Announcements;

public class GetPendingAnnouncementResponse
{
	public Announcement? Announcement { get; set; }
}
