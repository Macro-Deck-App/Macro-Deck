using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Platform.Applications;

internal sealed record RunningProcess(int Id, string Name, string? Path);

internal interface IProcessSnapshotSource
{
	IReadOnlyList<RunningProcess> Capture();
}

internal sealed class ProcessSnapshotSource : IProcessSnapshotSource
{
	private const int MacOsPathBufferSize = 4096;

	private readonly global::System.Threading.Lock _cacheLock = new();
	private Dictionary<int, CachedProcess> _paths = [];

	public IReadOnlyList<RunningProcess> Capture()
	{
		var processes = Process.GetProcesses();
		var result = new List<RunningProcess>(processes.Length);

		lock (_cacheLock)
		{
			var previous = _paths;
			var next = new Dictionary<int, CachedProcess>(processes.Length);
			foreach (var process in processes)
			{
				using (process)
				{
					string name;
					try
					{
						name = process.ProcessName;
					}
					catch (InvalidOperationException)
					{
						continue;
					}

					// A pid can be reused by another program, so a cached path only holds for the same name.
					var path = previous.TryGetValue(process.Id, out var cached) && cached.Name == name
						? cached.Path
						: TryReadPath(process.Id);
					next[process.Id] = new CachedProcess(name, path);
					result.Add(new RunningProcess(process.Id, name, path));
				}
			}

			_paths = next;
		}

		return result;
	}

	private static string? TryReadPath(int processId)
	{
		try
		{
			if (OperatingSystem.IsWindows())
			{
				return Win32Windows.TryQueryFullProcessImagePath((uint)processId);
			}

			if (OperatingSystem.IsLinux())
			{
				return File.ResolveLinkTarget($"/proc/{processId}/exe", true)?.FullName;
			}

			if (OperatingSystem.IsMacOS())
			{
				var buffer = new byte[MacOsPathBufferSize];
				var length = proc_pidpath(processId, buffer, (uint)buffer.Length);
				return length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null;
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}

		return null;
	}

	[DllImport("/usr/lib/libSystem.dylib")]
	private static extern int proc_pidpath(int pid, byte[] buffer, uint bufferSize);

	private sealed record CachedProcess(string Name, string? Path);
}
