using System.Text.Json;
using MacroDeck.Plugin.Cli.Manifests;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>
/// Orchestrates <c>new</c> once a <see cref="PluginScaffoldRequest" /> has already been validated: manage
/// the template, invoke <see cref="IPluginScaffoldGenerator.CreateAsync" />, rewrite the generated
/// manifest, and write the sibling build config. A directory this scaffolder itself created is removed on
/// any failure past that point; a directory that already existed (empty, per the caller's own
/// <c>output-exists</c> check) is left alone either way.
/// </summary>
internal static class PluginScaffolder
{
	public static async Task<PluginScaffoldResult> ScaffoldAsync(PluginScaffoldRequest request,
		IPluginScaffoldGenerator generator,
		CancellationToken cancellationToken = default)
	{
		var outputDirectoryPreexisted = Directory.Exists(request.Output);
		var weCreatedOutputDirectory = false;

		try
		{
			if (!request.SkipTemplateInstall)
			{
				var status = await generator.ProbeAsync(cancellationToken).ConfigureAwait(false);

				if (status == PluginTemplateStatus.NotInstalled || request.TemplateVersion is not null)
				{
					var install = await generator.InstallAsync(request.TemplateVersion, cancellationToken)
						.ConfigureAwait(false);

					if (!install.Success)
					{
						return PluginScaffoldResult.Fail(PluginScaffoldFailureReason.TemplateInstallFailed,
							"Installing the plugin project template failed.",
							install.Output);
					}
				}
				else if (status == PluginTemplateStatus.Outdated)
				{
					var update = await generator.UpdateAsync(cancellationToken).ConfigureAwait(false);

					if (!update.Success)
					{
						return PluginScaffoldResult.Fail(PluginScaffoldFailureReason.TemplateInstallFailed,
							"Updating the plugin project template failed.",
							update.Output);
					}
				}
			}

			if (!outputDirectoryPreexisted)
			{
				Directory.CreateDirectory(request.Output);
				weCreatedOutputDirectory = true;
			}

			var create = await generator.CreateAsync(request, cancellationToken).ConfigureAwait(false);

			if (!create.Success)
			{
				CleanUpIfWeCreatedIt(request.Output, weCreatedOutputDirectory);
				return PluginScaffoldResult.Fail(PluginScaffoldFailureReason.TemplateCreateFailed,
					"'dotnet new' failed to create the plugin project.",
					create.Output);
			}

			var manifestPath = FindManifest(request.Output, out var manifestCount);
			if (manifestPath is null)
			{
				CleanUpIfWeCreatedIt(request.Output, weCreatedOutputDirectory);
				return PluginScaffoldResult.Fail(PluginScaffoldFailureReason.ManifestNotGenerated,
					manifestCount == 0
						? "The template did not generate a manifest.json."
						: $"The template generated {manifestCount} manifest.json files; expected exactly one.");
			}

			return await RewriteManifestAndWriteBuildConfigAsync(request,
					manifestPath,
					weCreatedOutputDirectory,
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (PluginScaffoldGeneratorException ex)
		{
			CleanUpIfWeCreatedIt(request.Output, weCreatedOutputDirectory);
			return PluginScaffoldResult.Fail(ex.Reason, ex.Message, ex.Detail);
		}
	}

	private static async Task<PluginScaffoldResult> RewriteManifestAndWriteBuildConfigAsync(
		PluginScaffoldRequest request,
		string manifestPath,
		bool weCreatedOutputDirectory,
		CancellationToken cancellationToken)
	{
		string existingJson;
		try
		{
			existingJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			CleanUpIfWeCreatedIt(request.Output, weCreatedOutputDirectory);
			return PluginScaffoldResult.Fail(PluginScaffoldFailureReason.WriteFailed, ex.Message);
		}

		string rewrittenJson;
		try
		{
			rewrittenJson = ScaffoldManifestWriter.BuildManifestJson(existingJson, request);
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException)
		{
			CleanUpIfWeCreatedIt(request.Output, weCreatedOutputDirectory);
			return PluginScaffoldResult.Fail(PluginScaffoldFailureReason.ManifestRewriteFailed, ex.Message);
		}

		using (var document = JsonDocument.Parse(rewrittenJson))
		{
			var problems = PluginManifestSchema.Validate(document.RootElement);
			if (problems.Any(problem => problem.Severity == ManifestProblemSeverity.Error))
			{
				CleanUpIfWeCreatedIt(request.Output, weCreatedOutputDirectory);
				return PluginScaffoldResult.Fail(PluginScaffoldFailureReason.ManifestRewriteFailed,
					"The rewritten manifest.json failed schema validation - this is a bug in 'new', not the " +
					"supplied values.",
					string.Join(Environment.NewLine, problems.Select(problem => problem.Message)));
			}
		}

		try
		{
			await File.WriteAllTextAsync(manifestPath, rewrittenJson, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			CleanUpIfWeCreatedIt(request.Output, weCreatedOutputDirectory);
			return PluginScaffoldResult.Fail(PluginScaffoldFailureReason.WriteFailed, ex.Message);
		}

		var buildConfigPath
			= Path.Combine(Path.GetDirectoryName(manifestPath)!, PluginScaffoldDefaults.BuildConfigFileName);

		try
		{
			await File.WriteAllTextAsync(buildConfigPath, PluginBuildConfig.Build(request), cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			CleanUpIfWeCreatedIt(request.Output, weCreatedOutputDirectory);
			return PluginScaffoldResult.Fail(PluginScaffoldFailureReason.WriteFailed, ex.Message);
		}

		return PluginScaffoldResult.Ok(request.Output, manifestPath, buildConfigPath);
	}

	private static void CleanUpIfWeCreatedIt(string outputDirectory, bool weCreatedIt)
	{
		if (!weCreatedIt || !Directory.Exists(outputDirectory))
		{
			return;
		}

		try
		{
			Directory.Delete(outputDirectory, recursive: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Best effort: a failed cleanup must not mask the original failure this method is returning.
		}
	}

	private static string? FindManifest(string outputDirectory, out int count)
	{
		var matches = Directory
			.EnumerateFiles(outputDirectory, "manifest.json", SearchOption.AllDirectories)
			.Where(path => !IsUnderBuildOutput(outputDirectory, path))
			.ToList();

		count = matches.Count;
		return matches.Count == 1 ? matches[0] : null;
	}

	private static bool IsUnderBuildOutput(string root, string path)
	{
		var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
		var segments = relative.Split('/');
		return segments.Contains("bin") || segments.Contains("obj");
	}
}
