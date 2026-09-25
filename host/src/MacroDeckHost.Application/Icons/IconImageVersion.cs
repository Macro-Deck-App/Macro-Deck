using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Icons;

public static class IconImageVersion
{
	public static string? Of(IconEntity icon) => icon.MasterContentHash ?? icon.SourceContentHash;
}
