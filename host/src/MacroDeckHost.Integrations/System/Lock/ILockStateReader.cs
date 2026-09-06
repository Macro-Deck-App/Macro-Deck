namespace MacroDeckHost.Integrations.System.Lock;

public interface ILockStateReader
{
	bool IsSupported { get; }

	string? UnsupportedReason { get; }

	bool? IsLocked();
}
