namespace MacroDeckHost.Platform.Lock;

public interface ILockStateReader
{
	bool IsSupported { get; }

	string? UnsupportedReason { get; }

	bool? IsLocked();
}
