namespace MacroDeckHost.Application.Scripts;

internal static class ScriptCallDepth
{
	private static readonly AsyncLocal<int> _current = new();

	public static int Current => _current.Value;

	public static void Set(int depth) => _current.Value = depth;
}
