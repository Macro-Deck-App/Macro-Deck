namespace MacroDeckHost.Application.Triggers;

internal static class EventCascade
{
	public const int MaxDepth = 32;

	private static readonly AsyncLocal<int> _current = new();

	public static int Current => _current.Value;

	public static void Set(int depth)
	{
		_current.Value = depth;
	}
}
