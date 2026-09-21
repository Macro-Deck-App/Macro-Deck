using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Paths;

namespace MacroDeckHost.Infrastructure.Auth;

public sealed class LastServedRotation : ILastServedRotation
{
	private readonly string _path;

	public LastServedRotation(IMacroDeckPaths paths)
	{
		ArgumentNullException.ThrowIfNull(paths);

		_path = Path.Combine(paths.DataDirectory, "last-rotation");
	}

	public DateTime Read()
	{
		try
		{
			return File.Exists(_path) ? RefreshServingEpoch.Parse(File.ReadAllText(_path)) : DateTime.MinValue;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return DateTime.MinValue;
		}
	}

	// A failure here only narrows the grace the next host offers, so it is never worth throwing over.
	public void Record(DateTime rotatedAt)
	{
		var staging = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			File.WriteAllText(staging, RefreshServingEpoch.Format(rotatedAt));
			File.Move(staging, _path, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Delete(staging);
		}
	}

	private static void Delete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}
