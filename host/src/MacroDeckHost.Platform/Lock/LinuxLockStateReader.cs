using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Platform.Lock;

[SupportedOSPlatform("linux")]
public sealed class LinuxLockStateReader : ILockStateReader
{
	private readonly Func<bool?> _readLockedHint;

	public LinuxLockStateReader()
		: this(LogindSession.ReadLockedHint)
	{
	}

	internal LinuxLockStateReader(Func<bool?> readLockedHint)
	{
		_readLockedHint = readLockedHint;
		IsSupported = readLockedHint() is not null;
	}

	public bool IsSupported { get; }

	public string? UnsupportedReason => IsSupported ? null : "No logind session answered the LockedHint read.";

	public bool? IsLocked()
	{
		if (!IsSupported)
		{
			return null;
		}

		// A failed read has to mean unlocked: null would leave a locked state standing and block every
		// action until a restart, and a machine without an answer must keep working.
		return _readLockedHint() ?? false;
	}
}
