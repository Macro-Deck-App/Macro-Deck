using System.Text.Json;
using MacroDeck.Plugin.Cli.Runtime;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Cli.Rendering;

internal static class PreviewRenderSession
{
	public static async Task<int> RunAsync(
		CliConsole console,
		PreviewRenderOptions options,
		Func<string, CancellationToken, Task<IPreviewScreenshotter>>? screenshotterFactory,
		CancellationToken cancellationToken)
	{
		if (!TryValidate(console, options, out var explicitSizes))
		{
			return ExitCode.UsageError;
		}

		var browser = BrowserLocator.Find(options.Browser, Environment.GetEnvironmentVariable, File.Exists);

		if (browser is null)
		{
			console.WriteError("browser-not-found",
				$"No Chrome, Chromium or Edge was found. Install one, or point --browser or {BrowserLocator.EnvironmentVariable} at it.");
			return ExitCode.InputUnreadable;
		}

		PluginLaunchSpec spec;

		try
		{
			spec = options.Artifact is { } artifact
				? await PluginLaunchSpec.ForArtifactAsync(artifact, cancellationToken).ConfigureAwait(false)
				: options.Project is { } project
					? await PluginSubjectResolver.ResolveProjectAsync(project, console, cancellationToken).ConfigureAwait(false)
					: PluginSubjectResolver.ResolveExecutable(options.Executable!);
		}
		catch (PluginSubjectException exception)
		{
			console.WriteError(exception.Code, exception.Message, exception.Detail);
			return ExitCode.InputUnreadable;
		}

		await using var host = await MacroDeckTestHost.StartAsync().ConfigureAwait(false);
		ExternalPlugin? plugin = null;
		PluginSessionView session;
		IReadOnlyList<UiPreviewDescriptorDto> previews;

		try
		{
			plugin = await host.LaunchAsync(spec).ConfigureAwait(false);
			session = await host.WaitForSessionAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
			previews = await session.Ui.GetPreviewsAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is PluginProcessExitedException
			or PluginTestTimeoutException
			or InvalidOperationException)
		{
			console.WriteError("subject-launch-failed", $"The plugin could not be launched: {exception.Message}");

			if (plugin is not null)
			{
				await plugin.DisposeAsync().ConfigureAwait(false);
			}

			return ExitCode.InputUnreadable;
		}

		await using (plugin.ConfigureAwait(false))
		{
			return await RenderAsync(console, options, explicitSizes, browser, session, previews, screenshotterFactory,
				cancellationToken).ConfigureAwait(false);
		}
	}

	private static async Task<int> RenderAsync(
		CliConsole console,
		PreviewRenderOptions options,
		List<PreviewSize> explicitSizes,
		string browser,
		PluginSessionView session,
		IReadOnlyList<UiPreviewDescriptorDto> declared,
		Func<string, CancellationToken, Task<IPreviewScreenshotter>>? screenshotterFactory,
		CancellationToken cancellationToken)
	{
		if (declared.Count == 0)
		{
			console.Info("The plugin declares no [UiPreview] scenarios, so there is nothing to render.");
			return ExitCode.Success;
		}

		var selected = Select(declared, options.Previews, out var unknown);

		if (unknown.Count > 0)
		{
			console.WriteError("unknown-preview",
				$"No preview matches '{string.Join("', '", unknown)}'. Available: " +
				string.Join(", ", declared.Select(preview => preview.Scenario)) + ".");
			return ExitCode.UsageError;
		}

		var names = PreviewFileNames.BaseNames(declared);
		Directory.CreateDirectory(options.Output);
		await using var screenshotter = await (screenshotterFactory ?? StartChromeAsync)(browser, cancellationToken)
			.ConfigureAwait(false);
		var failures = 0;
		var written = 0;
		var skipped = 0;

		foreach (var preview in selected)
		{
			var outcome = await session.Ui.OpenPreviewAsync(preview.Id, preview.Profile).ConfigureAwait(false);

			if (!outcome.Accepted)
			{
				console.WriteError("preview-failed", $"'{preview.Scenario}' could not be rendered: {outcome.FailureReason}");
				failures++;
				continue;
			}

			try
			{
				var root = outcome.Tree!.Value.GetProperty("root");
				var resources = CollectResources(session, root);
				var sizes = explicitSizes.Count > 0 ? explicitSizes : [PreviewSize.OfCells(1, 1)];

				foreach (var size in sizes)
				{
					var target = Path.Combine(options.Output, $"{names[preview.Id]}-{size}.png");

					try
					{
						await screenshotter.CaptureAsync(
							new PreviewShot(PreviewScene.Build(root, size, options, resources), size, options.Scale, target),
							cancellationToken).ConfigureAwait(false);
						console.Info($"Wrote {CliText.DisplayPath(target)}");
						written++;
					}
					catch (PreviewRenderException exception) when (exception.Code == "preview-unsupported")
					{
						console.WriteWarning(exception.Code,
							$"'{preview.Scenario}' was skipped: only widget previews can be rendered, and its root is {exception.Message}.");
						skipped++;
						break;
					}
					catch (PreviewRenderException exception)
					{
						console.WriteError(exception.Code, $"'{preview.Scenario}' at {size}: {exception.Message}");
						failures++;
					}
				}
			}
			finally
			{
				await session.Ui.CloseAsync(outcome.SessionId!).ConfigureAwait(false);
			}
		}

		console.Info($"Rendered {written} image(s) to {CliText.DisplayPath(options.Output)}" +
			(skipped > 0 ? $", skipped {skipped} preview(s) that are not widgets." : "."));

		return failures == 0 ? ExitCode.Success : ExitCode.SubjectInvalid;
	}

	private static async Task<IPreviewScreenshotter> StartChromeAsync(string browser, CancellationToken cancellationToken)
		=> await ChromeScreenshotter.StartAsync(browser, cancellationToken).ConfigureAwait(false);

	private static Dictionary<string, string> CollectResources(PluginSessionView session, JsonElement root)
	{
		var resources = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var id in PreviewScene.ResourceIds(root).Distinct(StringComparer.Ordinal))
		{
			if (session.Ui.FindResource(id) is { } resource)
			{
				resources[id] = $"data:{resource.Handle.MediaType};base64,{Convert.ToBase64String(resource.Content)}";
			}
		}

		return resources;
	}

	private static List<UiPreviewDescriptorDto> Select(
		IReadOnlyList<UiPreviewDescriptorDto> declared,
		IReadOnlyList<string> filters,
		out List<string> unknown)
	{
		unknown = [];

		if (filters.Count == 0)
		{
			return [.. declared];
		}

		var selected = new List<UiPreviewDescriptorDto>();

		foreach (var filter in filters)
		{
			var matches = declared
				.Where(preview => string.Equals(preview.Id, filter, StringComparison.Ordinal) ||
					string.Equals(preview.Scenario, filter, StringComparison.OrdinalIgnoreCase))
				.ToList();

			if (matches.Count == 0)
			{
				unknown.Add(filter);
			}

			selected.AddRange(matches.Where(match => !selected.Contains(match)));
		}

		return selected;
	}

	private static bool TryValidate(CliConsole console, PreviewRenderOptions options, out List<PreviewSize> sizes)
	{
		sizes = [];

		var selectors = new[] { options.Project, options.Executable, options.Artifact }.Count(value => value is not null);
		if (selectors != 1)
		{
			console.WriteError("invalid-selector-count", "Specify exactly one of --project, --executable or --artifact.");
			return false;
		}

		foreach (var (values, cells, flag) in new[] { (options.Sizes, false, "--size"), (options.Cells, true, "--cells") })
		{
			foreach (var value in values)
			{
				if (!PreviewSize.TryParse(value, cells, out var size))
				{
					console.WriteError("invalid-size",
						$"'{value}' is not a valid {flag} value. Expected WIDTHxHEIGHT, each 1 to {PreviewMetrics.MaxPixels}" +
						(cells ? " cells." : " pixels."));
					return false;
				}

				sizes.Add(size);
			}
		}

		if (!(options.Scale is > 0 and <= 8))
		{
			console.WriteError("invalid-scale", "--scale must be greater than 0 and at most 8.");
			return false;
		}

		if (options.Radius < 0)
		{
			console.WriteError("invalid-radius", "--radius must not be negative.");
			return false;
		}

		if (!IsColor(options.Background))
		{
			console.WriteError("invalid-background", "--background must be transparent, a color name or a #hex color.");
			return false;
		}

		return true;
	}

	private static bool IsColor(string value)
		=> value.Length > 0 &&
			(value.All(char.IsAsciiLetter) ||
				(value[0] == '#' && value.Length is 4 or 5 or 7 or 9 && value.Skip(1).All(char.IsAsciiHexDigit)));
}
