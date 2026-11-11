using System.Runtime.Versioning;
using MacroDeckHost.Platform.Notifications;

namespace MacroDeckHost.Tests.UnitTests.Linux.Notifications;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
[NonParallelizable]
public class LinuxNotificationServiceTests
{
	private string _directory = null!;
	private string? _path;

	[SetUp]
	public void PutAStubNotifySendOnThePath()
	{
		_directory = Directory.CreateTempSubdirectory("notify-send-stub-").FullName;
		var stub = Path.Combine(_directory, "notify-send");
		File.WriteAllText(stub, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{Path.Combine(_directory, "argv")}'\n");
		File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		_path = Environment.GetEnvironmentVariable("PATH");
		Environment.SetEnvironmentVariable("PATH", _directory + ":" + _path);
	}

	[TearDown]
	public void RestoreThePath()
	{
		Environment.SetEnvironmentVariable("PATH", _path);
		Directory.Delete(_directory, true);
	}

	[Ignore("Red today: #1712 notify-send receives the title and message without --")]
	[TestCase("-x marks the spot")]
	[TestCase("--help")]
	[TestCase("Plain title")]
	public async Task The_title_and_message_reach_notify_send_as_text_even_when_they_start_with_a_dash(string title)
	{
		var service = new LinuxNotificationService();

		await service.ShowAsync(title, "-message");

		var arguments = File.ReadAllLines(Path.Combine(_directory, "argv"));
		Assert.That(Positionals(arguments), Is.EqualTo(new[] { title, "-message" }), string.Join(" | ", arguments));
	}

	// notify-send parses its arguments with GOption: anything after "--" is text, before it a leading dash is an option.
	private static List<string> Positionals(IEnumerable<string> arguments)
	{
		var positionals = new List<string>();
		var optionsEnded = false;
		foreach (var argument in arguments)
		{
			if (!optionsEnded && argument == "--")
			{
				optionsEnded = true;
			}
			else if (optionsEnded || !argument.StartsWith('-'))
			{
				positionals.Add(argument);
			}
		}

		return positionals;
	}
}
