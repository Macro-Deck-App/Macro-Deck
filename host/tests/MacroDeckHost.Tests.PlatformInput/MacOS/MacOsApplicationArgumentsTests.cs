using System.Runtime.Versioning;
using MacroDeckHost.Platform.Applications;

namespace MacroDeckHost.Tests.PlatformInput.MacOS;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
[Category("MacOS")]
public sealed class MacOsApplicationArgumentsTests
{
	private static readonly string[] _expectedArguments = ["hello world", "second"];

	[Test]
	public async Task A_quoted_argument_with_spaces_reaches_an_app_bundle_as_one_argument()
	{
		var root = Directory.CreateTempSubdirectory("macrodeck-app-arguments-");
		try
		{
			var output = Path.Combine(root.FullName, "arguments.txt");
			var bundle = CreateBundle(root.FullName, output);
			var service = ApplicationServiceFactory.Create(Serilog.Core.Logger.None);

			await service.LaunchAsync(bundle, "\"hello world\" second", null, LaunchMode.Start, false);

			Assert.That(() => File.Exists(output) && File.ReadAllLines(output).Length == 2,
				Is.True.After(20000, 100),
				"the app records its arguments");
			Assert.That(File.ReadAllLines(output), Is.EqualTo(_expectedArguments));
		}
		finally
		{
			root.Delete(true);
		}
	}

	private static string CreateBundle(string directory, string output)
	{
		var name = "MacroDeckArgumentProbe" + Guid.NewGuid().ToString("N")[..8];
		var bundle = Path.Combine(directory, name + ".app");
		var executables = Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
		File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"),
			$"""
			 <?xml version="1.0" encoding="UTF-8"?>
			 <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
			 <plist version="1.0"><dict>
			 <key>CFBundleExecutable</key><string>{name}</string>
			 <key>CFBundleIdentifier</key><string>app.macro-deck.tests.{name.ToLowerInvariant()}</string>
			 <key>CFBundlePackageType</key><string>APPL</string>
			 </dict></plist>
			 """);
		var executable = Path.Combine(executables.FullName, name);
		File.WriteAllText(executable, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{output}'\n");
		File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return bundle;
	}
}
