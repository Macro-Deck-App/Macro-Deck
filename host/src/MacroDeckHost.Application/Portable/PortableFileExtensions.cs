namespace MacroDeckHost.Application.Portable;

public static class PortableFileExtensions
{
	public const string Profile = ".macroDeckProfile";

	public const string Folder = ".macroDeckFolder";

	public const string Widgets = ".macroDeckWidget";

	public static readonly IReadOnlyList<string> All = [Profile, Folder, Widgets];
}
