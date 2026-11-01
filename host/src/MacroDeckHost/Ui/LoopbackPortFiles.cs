using System.Globalization;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Ui;

public readonly record struct PublishedPortFile(string Path, DateTime WrittenAtUtc);

public static class LoopbackPortFiles
{
	private static readonly ILogger _logger = Log.ForContext(typeof(LoopbackPortFiles));

	public static IReadOnlyList<PublishedPortFile> Publish(int port, IReadOnlyList<string> paths)
	{
		var content = port.ToString(CultureInfo.InvariantCulture);
		var published = new List<PublishedPortFile>();
		var failures = new List<(string Path, Exception Exception)>();

		foreach (var path in paths)
		{
			try
			{
				CreateDirectory(path);
				File.WriteAllText(path, content);
				published.Add(new PublishedPortFile(path, File.GetLastWriteTimeUtc(path)));
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				failures.Add((path, e));
			}
		}

		foreach (var (path, exception) in failures)
		{
			if (published.Count == 0)
			{
				_logger.Error(exception,
					"Failed to write port file {File}. The desktop UI cannot discover the host",
					path);
			}
			else
			{
				_logger.Warning("Could not write port file {File}: {Reason}", path, exception.Message);
			}
		}

		return published;
	}

	// A replacing host usually gets the same port, so the number alone cannot tell its file from ours.
	public static void DeleteOwn(int port, IEnumerable<PublishedPortFile> published)
	{
		foreach (var file in published)
		{
			try
			{
				if (SingleInstanceGuard.ParsePort(File.ReadAllText(file.Path)) == port
					&& File.GetLastWriteTimeUtc(file.Path) == file.WrittenAtUtc)
				{
					File.Delete(file.Path);
				}
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				_logger.Debug("Could not remove port file {File}: {Reason}", file.Path, e.Message);
			}
		}
	}

	private static void CreateDirectory(string path)
	{
		var directory = Path.GetDirectoryName(path);
		if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
		{
			return;
		}

		if (OperatingSystem.IsWindows())
		{
			Directory.CreateDirectory(directory);
		}
		else
		{
			Directory.CreateDirectory(directory,
				UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
	}
}
