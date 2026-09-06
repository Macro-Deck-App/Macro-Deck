namespace MacroDeckHost.Application.Scripts;

/// <summary>
/// The input names declared by the script currently running on this execution flow, so a write path can
/// refuse to assign one. Ambient rather than passed along, for the same reason
/// <see cref="ScriptCallDepth" /> is: the write goes through the SDK's variable API, which has no seam to
/// carry it. It keys on the declarations, not on which values were actually supplied, and a nested script
/// replaces the set with its own rather than inheriting the caller's.
/// </summary>
internal static class ScriptInputScope
{
	private static readonly AsyncLocal<IReadOnlySet<string>?> _current = new();

	public static bool IsDeclared(string name) => _current.Value?.Contains(name) == true;

	public static IReadOnlySet<string>? Current => _current.Value;

	public static void Set(IReadOnlySet<string>? names) => _current.Value = names;
}
