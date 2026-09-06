using System.Globalization;
using System.Text;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestLogFiles
{
	public const string Stamp = "20260806";

	public static string HostLine(
		string message,
		int second = 0,
		string level = "INF",
		string origin = "Host",
		string stamp = Stamp)
	{
		var date = DateOnly.ParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture);
		var time = TimeSpan.FromSeconds(second);

		// The real sink stamps local time, and the bootstrapper's dateless lines can only be read as
		// local time - so both sets have to be written in the same frame for ordering to be testable.
		var offset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(date, TimeOnly.FromTimeSpan(time)));

		return string.Create(CultureInfo.InvariantCulture,
			$"{date:yyyy-MM-dd} {time:hh\\:mm\\:ss}.000 {(offset < TimeSpan.Zero ? '-' : '+')}{offset.Duration():hh\\:mm} " +
			$"[{level}] [{origin}] {message}");
	}

	public static DateTimeOffset Timestamp(int second, string stamp = Stamp)
	{
		var date = DateOnly.ParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture);
		var local = new DateTime(date, TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(second)));

		return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
	}

	public static string ExceptionLine(string text) => $" {text}";

	public static string BootstrapperLine(string message, int second = 0, string level = "INF")
		=> string.Create(CultureInfo.InvariantCulture,
			$"[{TimeSpan.FromSeconds(second):hh\\:mm\\:ss} {level}] {message}");

	public static void WriteHost(string logsDirectory, string stamp, params string[] lines)
		=> Write(Path.Combine(logsDirectory, $"host-{stamp}.log"), lines);

	public static void WriteBootstrapper(string logsDirectory, string stamp, params string[] lines)
		=> Write(Path.Combine(logsDirectory, $"bootstrapper-{stamp}.log"), lines);

	public static void AppendHost(string logsDirectory, string stamp, params string[] lines)
		=> Append(Path.Combine(logsDirectory, $"host-{stamp}.log"), lines);

	private static void Write(string path, string[] lines)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, Join(lines), new UTF8Encoding(false));
	}

	private static void Append(string path, string[] lines)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.AppendAllText(path, Join(lines), new UTF8Encoding(false));
	}

	// Every line is newline-terminated, like a real sink leaves it: a line without one is still
	// being written, and the readers deliberately hold it back.
	private static string Join(string[] lines) => lines.Length == 0 ? string.Empty : string.Join('\n', lines) + "\n";
}
