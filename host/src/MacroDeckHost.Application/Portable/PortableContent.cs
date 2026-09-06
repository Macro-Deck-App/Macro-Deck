using MacroDeckHost.Application.Persistence.Profiles;

namespace MacroDeckHost.Application.Portable;

public sealed class PortableContent
{
	public PortableArchiveKind Kind { get; set; }

	public ProfileFile? Profile { get; set; }

	public List<ProfileFolder>? Folders { get; set; }

	public List<PortableWidget>? Widgets { get; set; }

	public List<PortableIcon> Icons { get; set; } = [];

	public List<PortableScript> Scripts { get; set; } = [];

	public List<PortableSecret> Secrets { get; set; } = [];

	public List<PortableVariable> Variables { get; set; } = [];
}
