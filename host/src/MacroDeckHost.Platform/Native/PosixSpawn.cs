using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Platform.Native;

[UnsupportedOSPlatform("windows")]
internal static class PosixSpawn
{
	public const int NoSuchFile = 2;
	public const int NotExecutable = 8;
	public const int IsADirectory = 21;

	private const string LibC = "libc";

	// posix_spawnattr_t, posix_spawn_file_actions_t and sigset_t are opaque: a pointer on macOS,
	// a few hundred bytes on glibc. A zeroed 1 KiB buffer holds any of them.
	private const int OpaqueSize = 1024;

	private const short SetSignalMask = 0x08;
	private const short LinuxSetSession = 0x80;
	private const short MacOsSetSession = 0x400;
	private const int ReadOnly = 0;
	private const int WriteOnly = 1;
	private const int ExecuteAccess = 1;
	private const int Interrupted = 4;

	private static readonly byte[] _zeroes = new byte[OpaqueSize];

	public static int SpawnDetached(
		string path,
		IReadOnlyList<string> arguments,
		IReadOnlyList<string> environment,
		string? workingDirectory,
		out int pid)
	{
		pid = 0;
		var attributes = AllocateZeroed();
		var actions = AllocateZeroed();
		var signalMask = AllocateZeroed();
		var argv = ToNative(arguments);
		var envp = ToNative(environment);
		var attributesReady = false;
		var actionsReady = false;
		try
		{
			var error = posix_spawnattr_init(attributes);
			if (error != 0)
			{
				return error;
			}

			attributesReady = true;
			error = posix_spawn_file_actions_init(actions);
			if (error != 0)
			{
				return error;
			}

			actionsReady = true;
			_ = sigemptyset(signalMask);
			var setSession = OperatingSystem.IsMacOS() ? MacOsSetSession : LinuxSetSession;
			error = posix_spawnattr_setsigmask(attributes, signalMask);
			error = error != 0 ? error : posix_spawnattr_setflags(attributes, (short)(setSession | SetSignalMask));
			error = error != 0 ? error : posix_spawn_file_actions_addopen(actions, 0, "/dev/null", ReadOnly, 0);
			error = error != 0 ? error : posix_spawn_file_actions_addopen(actions, 1, "/dev/null", WriteOnly, 0);
			error = error != 0 ? error : posix_spawn_file_actions_adddup2(actions, 1, 2);
			if (error == 0 && workingDirectory is not null)
			{
				error = posix_spawn_file_actions_addchdir_np(actions, workingDirectory);
			}

			return error != 0 ? error : posix_spawn(out pid, path, actions, attributes, argv, envp);
		}
		finally
		{
			if (actionsReady)
			{
				_ = posix_spawn_file_actions_destroy(actions);
			}

			if (attributesReady)
			{
				_ = posix_spawnattr_destroy(attributes);
			}

			Marshal.FreeHGlobal(attributes);
			Marshal.FreeHGlobal(actions);
			Marshal.FreeHGlobal(signalMask);
			Free(argv);
			Free(envp);
		}
	}

	// Returns the exit code, 128 plus the signal number for a signalled child (as Process reports it),
	// or -1 when the runtime already reaped the child (a host started with SIGCHLD ignored).
	public static int WaitForExit(int pid)
	{
		while (true)
		{
			if (waitpid(pid, out var status, 0) == pid)
			{
				var signal = status & 0x7f;
				return signal == 0 ? status >> 8 & 0xff : 128 + signal;
			}

			if (Marshal.GetLastPInvokeError() != Interrupted)
			{
				return -1;
			}
		}
	}

	public static bool IsExecutable(string path)
	{
		return !Directory.Exists(path) && access(path, ExecuteAccess) == 0;
	}

	private static IntPtr AllocateZeroed()
	{
		var buffer = Marshal.AllocHGlobal(OpaqueSize);
		Marshal.Copy(_zeroes, 0, buffer, OpaqueSize);
		return buffer;
	}

	private static IntPtr[] ToNative(IReadOnlyList<string> values)
	{
		var native = new IntPtr[values.Count + 1];
		for (var index = 0; index < values.Count; index++)
		{
			native[index] = Marshal.StringToCoTaskMemUTF8(values[index]);
		}

		return native;
	}

	private static void Free(IntPtr[] native)
	{
		foreach (var value in native)
		{
			Marshal.FreeCoTaskMem(value);
		}
	}

	[DllImport(LibC, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
	private static extern int posix_spawn(
		out int pid,
		[MarshalAs(UnmanagedType.LPUTF8Str)] string path,
		IntPtr fileActions,
		IntPtr attributes,
		IntPtr[] argv,
		IntPtr[] envp);

	[DllImport(LibC)]
	private static extern int posix_spawnattr_init(IntPtr attributes);

	[DllImport(LibC)]
	private static extern int posix_spawnattr_destroy(IntPtr attributes);

	[DllImport(LibC)]
	private static extern int posix_spawnattr_setflags(IntPtr attributes, short flags);

	[DllImport(LibC)]
	private static extern int posix_spawnattr_setsigmask(IntPtr attributes, IntPtr signalMask);

	[DllImport(LibC)]
	private static extern int posix_spawn_file_actions_init(IntPtr actions);

	[DllImport(LibC)]
	private static extern int posix_spawn_file_actions_destroy(IntPtr actions);

	[DllImport(LibC, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
	private static extern int posix_spawn_file_actions_addopen(
		IntPtr actions,
		int descriptor,
		[MarshalAs(UnmanagedType.LPUTF8Str)] string path,
		int flags,
		int mode);

	[DllImport(LibC)]
	private static extern int posix_spawn_file_actions_adddup2(IntPtr actions, int descriptor, int target);

	// Every supported system has the _np variant (glibc 2.29, macOS 10.15); the standard name is newer.
	[DllImport(LibC, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
	private static extern int posix_spawn_file_actions_addchdir_np(
		IntPtr actions,
		[MarshalAs(UnmanagedType.LPUTF8Str)] string path);

	[DllImport(LibC)]
	private static extern int sigemptyset(IntPtr set);

	[DllImport(LibC, SetLastError = true)]
	private static extern int waitpid(int pid, out int status, int options);

	[DllImport(LibC,
		CharSet = CharSet.Ansi,
		BestFitMapping = false,
		ThrowOnUnmappableChar = true,
		SetLastError = true)]
	private static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
}
