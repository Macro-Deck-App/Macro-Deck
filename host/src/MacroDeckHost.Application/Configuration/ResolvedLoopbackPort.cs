namespace MacroDeckHost.Application.Configuration;

public static class ResolvedLoopbackPort
{
	private static int _value;

	public static int? Value => Volatile.Read(ref _value) is var port and > 0 ? port : null;

	public static void Set(int port)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);

		var previous = Interlocked.CompareExchange(ref _value, port, 0);
		if (previous is not 0 && previous != port)
		{
			throw new InvalidOperationException(
				$"The loopback port was already resolved to {previous} and cannot be changed to {port}.");
		}
	}

	internal static void ResetForTests()
	{
		Volatile.Write(ref _value, 0);
	}
}
