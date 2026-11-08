using System.Runtime.Versioning;
using MacroDeck.Sdk.Input;
using MacroDeckHost.Integrations.Keyboard.Models;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeckHost.Integrations.Mouse.Models;
using MacroDeckHost.Integrations.Mouse.Native;
using Serilog;

namespace MacroDeckHost.Tests.InputProbe;

[SupportedOSPlatform("linux")]
internal static class X11StressMode
{
	public static async Task<int> RunAsync(ProbeOptions options)
	{
		var keyboardProvider = KeyboardInputProviderFactory.Create();
		var mouseProvider = MouseInputProviderFactory.Create();
		var keyboard = new KeyboardInputService(keyboardProvider, new KeyboardLayoutService(), Log.Logger);
		var mouse = new MouseInputService(mouseProvider);
		ProbeOutput.Write(new ProbeEvent
		{
			Type = "ready",
			Pid = Environment.ProcessId,
			Message = $"keyboard={keyboard.IsSupported},mouse={mouse.IsSupported}"
		});

		return options.Get("scenario") switch
		{
			"survive" => await SurviveAsync(keyboard, mouse),
			"parallel" => await ParallelAsync(keyboard,
				mouse,
				options.Get("target") ?? string.Empty,
				options.GetInt("count", 1000)),
			_ => 2
		};
	}

	private static async Task<int> SurviveAsync(KeyboardInputService keyboard, MouseInputService mouse)
	{
		while (Console.In.ReadLine() is { } line)
		{
			if (line.Trim() != "press")
			{
				continue;
			}

			await Attempt(() => keyboard.PressComboAsync(KeyModifier.None, KeyCode.A));
			await Attempt(() => keyboard.TypeTextAsync("a"));
			await Attempt(() => mouse.MoveAsync(MouseTarget.By(1, 1)));
			ProbeOutput.Write(new ProbeEvent { Type = "alive", Pid = Environment.ProcessId });
		}

		return 0;
	}

	private static async Task<int> ParallelAsync(
		KeyboardInputService keyboard,
		MouseInputService mouse,
		string target,
		int count)
	{
		var tasks = Enumerable.Range(0, count)
			.Select(index => Task.Run(async () =>
			{
				if (index % 2 == 0)
				{
					await keyboard.PressComboAsync(KeyModifier.None, KeyCode.A);
					return;
				}

				var opened = await keyboard.OpenSessionAsync(new KeyboardTarget(target,
					KeyboardTargetMode.Background));
				using var session = opened.Session
					?? throw new InvalidOperationException($"no background session: {opened.UnavailableReason}");
				await session.PressComboAsync(KeyModifier.None, KeyCode.B);
			}))
			.Append(Task.Run(async () =>
			{
				for (var i = 0; i < count / 10; i++)
				{
					await mouse.MoveAsync(MouseTarget.By(i % 2 == 0 ? 1 : -1, 0));
				}
			}));
		await Task.WhenAll(tasks);
		await keyboard.ReleaseAllAsync();
		ProbeOutput.Write(new ProbeEvent { Type = "done", Pid = Environment.ProcessId });
		return 0;
	}

	private static async Task Attempt(Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception)
		{
			ProbeOutput.Write(new ProbeEvent { Type = "log", Level = "Error", Message = exception.Message });
		}
	}
}
