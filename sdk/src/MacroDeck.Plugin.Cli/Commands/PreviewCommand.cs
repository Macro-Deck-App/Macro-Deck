using System.CommandLine;
using System.Globalization;
using MacroDeck.Plugin.Cli.Rendering;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary><c>macrodeck-plugin preview render</c>: renders a plugin's <c>[UiPreview]</c> scenarios to PNG files.</summary>
internal static class PreviewCommand
{
	public static Command Create(Func<string, CancellationToken, Task<IPreviewScreenshotter>>? screenshotterFactory = null)
	{
		var projectOption = new Option<string?>("--project") { Description = "A plugin's .csproj, or its directory." };
		var executableOption = new Option<string?>("--executable")
			{ Description = "An already-built executable or framework-dependent .dll." };
		var artifactOption = new Option<string?>("--artifact") { Description = "A packed .macroDeckPlugin artifact." };
		var sizeOption = new Option<string[]>("--size")
		{
			Description = "WIDTHxHEIGHT in pixels (repeatable). Defaults to one deck cell.",
			DefaultValueFactory = _ => []
		};
		var cellsOption = new Option<string[]>("--cells")
		{
			Description = "COLUMNSxROWS in deck cells (repeatable), 120 px per cell plus a 12 px gap.",
			DefaultValueFactory = _ => []
		};
		var previewOption = new Option<string[]>("--preview")
		{
			Description = "Render only the scenario with this name or id (repeatable). Defaults to every scenario.",
			DefaultValueFactory = _ => []
		};
		var scaleOption = new Option<double>("--scale")
			{ Description = "Device pixels per pixel. Defaults to 2.", DefaultValueFactory = _ => 2 };
		var themeOption = CreateThemeOption();
		var backgroundOption = new Option<string>("--background")
		{
			Description = "What shows behind the rounded tile: transparent, a color name or a #hex color. " +
				"Defaults to transparent.",
			DefaultValueFactory = _ => "transparent"
		};
		var radiusOption = new Option<int>("--radius")
		{
			Description = "The tile's corner radius in pixels. Defaults to the deck's 22.",
			DefaultValueFactory = _ => PreviewMetrics.DefaultRadius
		};
		var localeOption = new Option<string>("--locale")
			{ Description = "The culture dates and numbers format in. Defaults to en-US.", DefaultValueFactory = _ => "en-US" };
		var outputOption = new Option<string>("--output")
			{ Description = "The directory the PNG files go to. Defaults to ./previews.", DefaultValueFactory = _ => "previews" };
		var browserOption = new Option<string?>("--browser")
		{
			Description = $"A Chrome, Chromium or Edge executable. Defaults to {BrowserLocator.EnvironmentVariable}, then a " +
				"browser found in the usual places."
		};

		var render = new Command("render", "Render a plugin's [UiPreview] scenarios to PNG files.");
		foreach (var symbol in new Option[]
			{
				projectOption, executableOption, artifactOption, sizeOption, cellsOption, previewOption, scaleOption,
				themeOption, backgroundOption, radiusOption, localeOption, outputOption, browserOption
			})
		{
			render.Add(symbol);
		}

		render.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);

			var options = new PreviewRenderOptions
			{
				Project = parseResult.GetValue(projectOption),
				Executable = parseResult.GetValue(executableOption),
				Artifact = parseResult.GetValue(artifactOption),
				Sizes = parseResult.GetValue(sizeOption) ?? [],
				Cells = parseResult.GetValue(cellsOption) ?? [],
				Previews = parseResult.GetValue(previewOption) ?? [],
				Scale = parseResult.GetValue(scaleOption),
				Theme = parseResult.GetValue(themeOption),
				Background = parseResult.GetValue(backgroundOption) ?? "transparent",
				Radius = parseResult.GetValue(radiusOption),
				Locale = parseResult.GetValue(localeOption) ?? "en-US",
				Output = parseResult.GetValue(outputOption) ?? "previews",
				Browser = parseResult.GetValue(browserOption)
			};

			return await PreviewRenderSession.RunAsync(console, options, screenshotterFactory, cancellationToken)
				.ConfigureAwait(false);
		});

		var command = new Command("preview", "Work with a plugin's [UiPreview] scenarios.");
		command.Add(render);

		return command;
	}

	private static Option<PreviewTheme> CreateThemeOption()
	{
		var option = new Option<PreviewTheme>("--theme")
		{
			Description = "dark or light. Defaults to dark.",
			DefaultValueFactory = _ => PreviewTheme.Dark
		};

		option.CustomParser = result => CliOptionParsing.ParseToken(result,
			PreviewTheme.Dark,
			("dark", PreviewTheme.Dark),
			("light", PreviewTheme.Light));

		return option;
	}
}
