// A tiny, deliberately dumb "plugin" process for real-child-process tests: PluginProcessLauncherTests
// (cross-platform) and the per-platform kill-tree tests. Understands a handful of flags, processed in
// order so a single invocation can combine behaviours (e.g. print an env var, then sleep).

using System.Diagnostics;

var exitCode = 0;

for (var i = 0; i < args.Length; i++)
{
	switch (args[i])
	{
		case "--sleep":
			var ms = int.Parse(args[++i]);
			Thread.Sleep(ms);
			break;

		case "--exit":
			exitCode = int.Parse(args[++i]);
			return exitCode;

		case "--print-env":
			var name = args[++i];
			Console.Out.WriteLine(Environment.GetEnvironmentVariable(name) ?? string.Empty);
			Console.Out.Flush();
			break;

		case "--fail-bootstrap":
			for (var line = 0; line < 50_000; line++)
			{
				Console.Error.WriteLine($"bootstrap failure line {line:D5}: " + new string('x', 120));
			}

			Console.Error.Flush();
			return 1;

		case "--spawn-child":
			var self = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
			var startInfo = new ProcessStartInfo
			{
				FileName = self,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			if (string.Equals(Path.GetFileNameWithoutExtension(self), "dotnet", StringComparison.OrdinalIgnoreCase))
			{
				var dll = Path.ChangeExtension(Environment.GetCommandLineArgs()[0], ".dll");
				startInfo.ArgumentList.Add(dll);
			}

			startInfo.ArgumentList.Add("--sleep");
			startInfo.ArgumentList.Add("120000");

			var child = Process.Start(startInfo)!;
			Console.Out.WriteLine($"CHILD_PID={child.Id}");
			Console.Out.Flush();
			break;
	}
}

Thread.Sleep(Timeout.Infinite);
return exitCode;
