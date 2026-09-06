using Spectre.Console;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>
/// Collects a <see cref="PluginScaffoldRequest" /> from <see cref="PluginScaffoldInputs" />, one method for
/// both the interactive and the non-interactive path: a value already supplied on the command line is
/// validated immediately and never prompted for (even when interactive - see issue #589's acceptance
/// scenario e3), a missing value is either prompted for with a re-prompt loop (interactive) or, for a
/// required field, collected into a single <c>missing-required-option</c> failure (non-interactive). This
/// is also why <see cref="PluginScaffoldValidation" /> exists separately: the exact same validators run on
/// both paths, so they can never quietly accept different things.
/// </summary>
internal sealed class PluginScaffoldWizard
{
	private readonly CliPromptReader _prompt;
	private readonly CliConsole _console;

	public PluginScaffoldWizard(CliPromptReader prompt, CliConsole console)
	{
		_prompt = prompt;
		_console = console;
	}

	public PluginWizardOutcome Run(PluginScaffoldInputs inputs, bool interactive, CancellationToken cancellationToken)
	{
		var missingRequired = new List<string>();

		var name = ResolveName(inputs, interactive, missingRequired, cancellationToken);
		var (id, idError) = ResolveId(inputs, name, interactive, missingRequired, cancellationToken);
		if (idError is { } idErrorValue)
		{
			return PluginWizardOutcome.Invalid(idErrorValue);
		}

		var publisher = ResolvePublisher(inputs, interactive, missingRequired, cancellationToken);

		if (missingRequired.Count > 0)
		{
			return PluginWizardOutcome.Invalid(new CliDiagnostic("missing-required-option",
				$"--{string.Join(" and --", missingRequired)} must be supplied when running " +
				"non-interactively."));
		}

		var description = ResolveDescription(inputs, interactive, cancellationToken);

		var (repository, repositoryError) =
			ResolveUrl(inputs.Repository, "repository", "Repository URL", interactive, cancellationToken);
		if (repositoryError is { } repositoryErrorValue)
		{
			return PluginWizardOutcome.Invalid(repositoryErrorValue);
		}

		// The repository URL is the homepage's default, per the issue's 'Homepage URL [same as repository]:'.
		var (homepage, homepageError) =
			ResolveUrl(inputs.Homepage, "homepage", "Homepage URL", interactive, cancellationToken, repository);
		if (homepageError is { } homepageErrorValue)
		{
			return PluginWizardOutcome.Invalid(homepageErrorValue);
		}

		var license = ResolveLicense(inputs, interactive, cancellationToken);

		var (projectName, projectNameError) = ResolveProjectName(inputs, name, interactive, cancellationToken);
		if (projectNameError is { } projectNameErrorValue)
		{
			return PluginWizardOutcome.Invalid(projectNameErrorValue);
		}

		var (platforms, platformsError, platformsCancelled) = ResolvePlatforms(inputs, interactive, cancellationToken);
		if (platformsCancelled)
		{
			return PluginWizardOutcome.Decline();
		}

		if (platformsError is { } platformsErrorValue)
		{
			return PluginWizardOutcome.Invalid(platformsErrorValue);
		}

		var output = ResolveOutput(inputs, projectName, interactive, cancellationToken);
		if (PluginScaffoldValidation.ValidateOutputDirectory(output) is { } outputError)
		{
			return PluginWizardOutcome.Invalid(outputError);
		}

		var request = new PluginScaffoldRequest
		{
			Name = name,
			Id = id,
			Publisher = publisher,
			Description = string.IsNullOrWhiteSpace(description)
				? PluginScaffoldDefaults.DefaultDescription
				: description,
			Repository = repository,
			Homepage = homepage,
			License = license,
			ProjectName = projectName,
			Output = output,
			Platforms = platforms,
			TemplateVersion = inputs.TemplateVersion,
			SkipTemplateInstall = inputs.SkipTemplateInstall,
			NoRestore = inputs.NoRestore
		};

		if (!interactive)
		{
			return PluginWizardOutcome.Accept(request);
		}

		PrintSummary(request);
		cancellationToken.ThrowIfCancellationRequested();
		_console.Write("Create plugin? [Y/n]: ");
		var confirmation = ReadLineOrThrow(cancellationToken).Trim();

		return string.Equals(confirmation, "n", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(confirmation, "no", StringComparison.OrdinalIgnoreCase)
				? PluginWizardOutcome.Decline()
				: PluginWizardOutcome.Accept(request);
	}

	private string ResolveName(PluginScaffoldInputs inputs,
		bool interactive,
		List<string> missingRequired,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(inputs.Name))
		{
			return inputs.Name.Trim();
		}

		if (interactive)
		{
			return PromptRequired("Plugin name", cancellationToken);
		}

		missingRequired.Add("name");
		return string.Empty;
	}

	private (string Id, CliDiagnostic? Error) ResolveId(PluginScaffoldInputs inputs,
		string name,
		bool interactive,
		List<string> missingRequired,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(inputs.Id))
		{
			var trimmed = inputs.Id.Trim();
			var error = PluginScaffoldValidation.ValidatePluginId(trimmed);
			return error is null ? (trimmed, null) : (string.Empty, error);
		}

		var derived = PluginIdDerivation.Derive(name);

		if (interactive)
		{
			var id = PromptWithValidation("Plugin id",
				derived,
				PluginScaffoldValidation.ValidatePluginId,
				cancellationToken);

			return (id, null);
		}

		// --yes means "accept every default", so it takes the derived id the wizard would have offered.
		// --non-interactive only suppresses prompting and still requires the value to be supplied.
		if (inputs.Yes && derived is not null)
		{
			return (derived, null);
		}

		missingRequired.Add("id");
		return (string.Empty, null);
	}

	private string ResolvePublisher(PluginScaffoldInputs inputs,
		bool interactive,
		List<string> missingRequired,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(inputs.Publisher))
		{
			return inputs.Publisher.Trim();
		}

		if (interactive)
		{
			return PromptRequired("Publisher name", cancellationToken);
		}

		missingRequired.Add("publisher");
		return string.Empty;
	}

	private string? ResolveDescription(PluginScaffoldInputs inputs,
		bool interactive,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(inputs.Description))
		{
			return inputs.Description.Trim();
		}

		return interactive
			? PromptOptional("Description", PluginScaffoldDefaults.DefaultDescription, cancellationToken)
			: null;
	}

	private (string? Value, CliDiagnostic? Error) ResolveUrl(string? suppliedValue,
		string fieldName,
		string label,
		bool interactive,
		CancellationToken cancellationToken,
		string? defaultValue = null)
	{
		if (!string.IsNullOrWhiteSpace(suppliedValue))
		{
			var trimmed = suppliedValue.Trim();
			var error = PluginScaffoldValidation.ValidateUrl(trimmed, fieldName);
			return error is null ? (trimmed, null) : (null, error);
		}

		// Only offered, never applied silently: accepting a prompt's default is the user supplying the value,
		// whereas omitting the flag is not, and an unsupplied optional field stays out of the manifest.
		if (!interactive)
		{
			return (null, null);
		}

		var value = PromptOptionalWithValidation(label,
			defaultValue,
			candidate => PluginScaffoldValidation.ValidateUrl(candidate, fieldName),
			cancellationToken);
		return (value, null);
	}

	private string ResolveLicense(PluginScaffoldInputs inputs, bool interactive, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(inputs.License))
		{
			return inputs.License.Trim();
		}

		return interactive
			? PromptRequired("License", cancellationToken, PluginScaffoldDefaults.License)
			: PluginScaffoldDefaults.License;
	}

	private (string ProjectName, CliDiagnostic? Error) ResolveProjectName(PluginScaffoldInputs inputs,
		string name,
		bool interactive,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(inputs.ProjectName))
		{
			var trimmed = inputs.ProjectName.Trim();
			return ProjectNameDerivation.Validate(trimmed)
				? (trimmed, null)
				: (string.Empty,
					new CliDiagnostic("invalid-project-name", $"'{trimmed}' is not a valid project name."));
		}

		var derived = ProjectNameDerivation.Derive(name);
		if (derived is not null && ProjectNameDerivation.Validate(derived))
		{
			return (derived, null);
		}

		if (interactive)
		{
			var projectName = PromptWithValidation("Project name",
				null,
				candidate => ProjectNameDerivation.Validate(candidate)
					? null
					: new CliDiagnostic("invalid-project-name", $"'{candidate}' is not a valid project name."),
				cancellationToken);

			return (projectName, null);
		}

		return (string.Empty, new CliDiagnostic("invalid-project-name",
			$"'{name}' does not derive a valid project name; supply --project-name explicitly."));
	}

	private (IReadOnlyList<string> Platforms, CliDiagnostic? Error, bool Cancelled) ResolvePlatforms(
		PluginScaffoldInputs inputs,
		bool interactive,
		CancellationToken cancellationToken)
	{
		if (inputs.Platforms is { Count: > 0 })
		{
			var deduped = Dedupe(inputs.Platforms);
			var error = PluginScaffoldValidation.ValidatePlatforms(deduped);
			return error is null ? (Reorder(deduped), null, false) : ((IReadOnlyList<string>)[], error, false);
		}

		if (interactive)
		{
			var ansiConsole = _prompt.CreateInteractiveConsole(_console.Output, _console.NoColor);
			if (ansiConsole is null)
			{
				return (PromptPlatformsNumbered(cancellationToken), null, false);
			}

			var selected = PromptPlatformsChecked(ansiConsole, cancellationToken);
			return selected is null ? ([], null, true) : (selected, null, false);
		}

		return (PluginScaffoldDefaults.DefaultPlatforms, null, false);
	}

	private string ResolveOutput(PluginScaffoldInputs inputs,
		string projectName,
		bool interactive,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(inputs.Output))
		{
			return inputs.Output.Trim();
		}

		var defaultOutput = Path.Combine(".", projectName);
		return interactive ? PromptRequired("Output directory", cancellationToken, defaultOutput) : defaultOutput;
	}

	private void PrintSummary(PluginScaffoldRequest request)
	{
		_console.WriteLine();
		_console.WriteLine("Summary:");
		_console.WriteLine($"  Name:      {request.Name}");
		_console.WriteLine($"  Id:        {request.Id}");
		_console.WriteLine($"  Publisher: {request.Publisher}");
		_console.WriteLine($"  License:   {request.License}");
		_console.WriteLine($"  Platforms: {string.Join(", ", request.Platforms)}");
		_console.WriteLine($"  Output:    {CliText.DisplayPath(request.Output)}");
	}

	private void WritePrompt(string label, string? defaultValue)
		=> _console.Write(string.IsNullOrEmpty(defaultValue) ? $"{label}: " : $"{label} [{defaultValue}]: ");

	private string ReadLineOrThrow(CancellationToken cancellationToken)
	{
		var line = _prompt.ReadLine();
		if (line is not null)
		{
			return line;
		}

		cancellationToken.ThrowIfCancellationRequested();

		// The wizard cannot make progress once its input stream has closed - treated as a cancellation so
		// it shares CliEntryPoint's existing exit(4)/no-partial-output handling rather than a new one.
		throw new OperationCanceledException("The input stream closed before the wizard finished.");
	}

	private string PromptRequired(string label, CancellationToken cancellationToken, string? defaultValue = null)
	{
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			WritePrompt(label, defaultValue);
			var trimmed = ReadLineOrThrow(cancellationToken).Trim();

			if (trimmed.Length > 0)
			{
				return trimmed;
			}

			if (!string.IsNullOrWhiteSpace(defaultValue))
			{
				return defaultValue;
			}
		}
	}

	private string PromptWithValidation(string label,
		string? defaultValue,
		Func<string, CliDiagnostic?> validate,
		CancellationToken cancellationToken)
	{
		while (true)
		{
			var candidate = PromptRequired(label, cancellationToken, defaultValue);
			var error = validate(candidate);
			if (error is null)
			{
				return candidate;
			}

			_console.WriteError(error.Value.Code, error.Value.Message);
		}
	}

	private string? PromptOptional(string label, string? defaultValue, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		WritePrompt(label, defaultValue);
		var trimmed = ReadLineOrThrow(cancellationToken).Trim();
		return trimmed.Length == 0 ? defaultValue : trimmed;
	}

	private string? PromptOptionalWithValidation(string label,
		string? defaultValue,
		Func<string, CliDiagnostic?> validate,
		CancellationToken cancellationToken)
	{
		while (true)
		{
			var value = PromptOptional(label, defaultValue, cancellationToken);
			if (value is null)
			{
				return null;
			}

			var error = validate(value);
			if (error is null)
			{
				return value;
			}

			_console.WriteError(error.Value.Code, error.Value.Message);
		}
	}

	private IReadOnlyList<string> PromptPlatformsNumbered(CancellationToken cancellationToken)
	{
		var offered = PluginScaffoldDefaults.OfferedPlatforms;
		var defaults = PluginScaffoldDefaults.DefaultPlatforms;

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_console.WriteLine($"Target platforms (default: {string.Join(", ", defaults)}):");
			for (var i = 0; i < offered.Count; i++)
			{
				_console.WriteLine($"  {i + 1}) {offered[i]}");
			}

			_console.Write("Selection (comma-separated numbers, 0 for none, blank for default): ");
			var trimmed = ReadLineOrThrow(cancellationToken).Trim();

			if (trimmed.Length == 0)
			{
				return defaults;
			}

			if (trimmed == "0")
			{
				var noneError = PluginScaffoldValidation.ValidatePlatforms([]);
				_console.WriteError(noneError!.Value.Code, noneError.Value.Message);
				continue;
			}

			var tokens = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			var selected = new List<string>();
			var allValid = true;

			foreach (var token in tokens)
			{
				if (!int.TryParse(token, out var index) || index < 1 || index > offered.Count)
				{
					_console.WriteError("unknown-platform", $"'{token}' is not a valid platform selection.");
					allValid = false;
					break;
				}

				selected.Add(offered[index - 1]);
			}

			if (allValid)
			{
				return Reorder(Dedupe(selected));
			}
		}
	}

	private static List<string>? PromptPlatformsChecked(IAnsiConsole ansiConsole,
		CancellationToken cancellationToken)
	{
		var prompt = new MultiSelectionPrompt<string>()
			.Title("Target platforms")
			.InstructionsText("(Space toggles, Enter confirms, Esc cancels)")
			.AddChoices(PluginScaffoldDefaults.OfferedPlatforms)
			.AddCancelResult();

		foreach (var platform in PluginScaffoldDefaults.DefaultPlatforms)
		{
			prompt.Select(platform);
		}

		List<string> selected;
		try
		{
			// The synchronous Show overload passes CancellationToken.None, which would leave Ctrl+C inert
			// for as long as the prompt is on screen.
			selected = prompt.ShowAsync(ansiConsole, cancellationToken).GetAwaiter().GetResult();
		}
		catch (OperationCanceledException)
		{
			// The prompt hides the cursor and only restores it on its own way out, so a cancelled key read
			// would otherwise leave the user's terminal without a cursor after the process exits.
			ansiConsole.Cursor.Show();
			throw;
		}

		// Required defaults to true, so an empty result is reachable only via Esc/AddCancelResult - never
		// by confirming with nothing selected. That makes an empty list synonymous with "cancelled".
		return selected.Count == 0 ? null : Reorder(Dedupe(selected));
	}

	private static List<string> Dedupe(IReadOnlyList<string> platforms)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var result = new List<string>();

		foreach (var platform in platforms)
		{
			if (seen.Add(platform))
			{
				result.Add(platform);
			}
		}

		return result;
	}

	private static List<string> Reorder(IReadOnlyList<string> platforms)
	{
		var set = new HashSet<string>(platforms, StringComparer.Ordinal);
		return PluginScaffoldDefaults.KnownPlatforms.Where(set.Contains).ToList();
	}
}
