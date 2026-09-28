using MacroDeckHost.Application.Announcements;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Announcements;

public class AnnouncementChangedEvent
{
	public Announcement? Announcement { get; set; }
}
