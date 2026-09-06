namespace MacroDeckHost.Application.Portable;

public static class PortableFileName
{
	private static readonly char[] _invalidChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

	public static string ForArchive(string name, string fallback, string extension)
	{
		var safeName = string.Concat(name.Where(c => !_invalidChars.Contains(c) && !char.IsControl(c))).Trim();
		return (safeName.Length == 0 ? fallback : safeName) + extension;
	}
}
