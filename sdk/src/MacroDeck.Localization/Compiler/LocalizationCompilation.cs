namespace MacroDeck.Localization.Compiler;

/// <summary>One key of a compiled resource set: its default template and the parameters callers pass.</summary>
internal sealed class LocalizationCompiledEntry
{
	public LocalizationCompiledEntry(string key,
		string defaultTemplate,
		List<LocalizationCompiledParameter> parameters,
		string? removedGuidance)
	{
		Key = key;
		DefaultTemplate = defaultTemplate;
		Parameters = parameters;
		RemovedGuidance = removedGuidance;
	}

	/// <summary>The dotted key name.</summary>
	public string Key { get; }

	/// <summary>The default-language template, the one every translation is checked against.</summary>
	public string DefaultTemplate { get; }

	/// <summary>The placeholders, in first-appearance order - which is the generated parameter order.</summary>
	public List<LocalizationCompiledParameter> Parameters { get; }

	/// <summary>What to use instead, when the key has been retired; <c>null</c> while it is current.</summary>
	public string? RemovedGuidance { get; }
}

/// <summary>A placeholder resolved to the type a generated method takes for it.</summary>
internal sealed class LocalizationCompiledParameter
{
	public LocalizationCompiledParameter(string name, LocalizationParameterType type)
	{
		Name = name;
		Type = type;
	}

	/// <summary>The placeholder name, used verbatim as the parameter name.</summary>
	public string Name { get; }

	/// <summary>The declared or inferred type.</summary>
	public LocalizationParameterType Type { get; }
}

/// <summary>The outcome of compiling one scope's resources.</summary>
internal sealed class LocalizationCompilationResult
{
	public LocalizationCompilationResult(string scope,
		string defaultCulture,
		List<string> cultures,
		List<LocalizationCompiledEntry> entries,
		Dictionary<string, Dictionary<string, string>> catalog,
		List<LocalizationFinding> findings)
	{
		Scope = scope;
		DefaultCulture = defaultCulture;
		Cultures = cultures;
		Entries = entries;
		Catalog = catalog;
		Findings = findings;
	}

	public string Scope { get; }

	/// <summary>The culture the default-language file is taken to be written in.</summary>
	public string DefaultCulture { get; }

	/// <summary>Every culture the set ships, the default one included.</summary>
	public List<string> Cultures { get; }

	/// <summary>The keys a typed API is generated for - default-language keys only, since a key that
	/// exists solely in a translation is MDLOC001 rather than a callable member.</summary>
	public List<LocalizationCompiledEntry> Entries { get; }

	/// <summary>Culture to key to template - the runtime catalog, ready to be embedded or transported.</summary>
	public Dictionary<string, Dictionary<string, string>> Catalog { get; }

	/// <summary>Everything wrong with the resources.</summary>
	public List<LocalizationFinding> Findings { get; }
}

/// <summary>
/// Turns parsed resource files into the typed API's shape, the runtime catalog, and the MDLOC findings.
/// One implementation drives the C# generator, the TypeScript emitter and the host-side validation of a
/// plugin's submitted catalog, so all three agree on what a valid resource set is.
/// </summary>
internal static class LocalizationCompilation
{
	/// <summary>The culture the default-language resource file is taken to be written in.</summary>
	public const string DefaultCulture = "en";

	/// <summary>Compiles one resource set.</summary>
	public static LocalizationCompilationResult Compile(LocalizationResourceSet set)
	{
		var findings = new List<LocalizationFinding>();
		var cultures = new List<string>();
		var catalog = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
		var entries = new List<LocalizationCompiledEntry>();

		var usable = new List<LocalizationResourceFile>();
		foreach (var file in set.Files)
		{
			if (file.Culture != null && !LocalizationCultureName.IsValid(file.Culture))
			{
				findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.InvalidCultureName,
					file.Path,
					null,
					$"'{file.Culture}' in '{file.FileName}' is not a well-formed culture name."));
				continue;
			}

			usable.Add(file);
		}

		var defaultFile = FindDefault(usable);
		if (defaultFile == null)
		{
			foreach (var file in usable)
			{
				findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.MissingDefaultResource,
					file.Path,
					null,
					$"'{set.BaseName}' has no default-language '{set.BaseName}.resx'; every key needs one."));
			}

			return new LocalizationCompilationResult(set.Scope,
				DefaultCulture,
				cultures,
				entries,
				catalog,
				findings);
		}

		var defaultEntries = CollectEntries(defaultFile, findings);
		var families = CollectPluralFamilies(defaultFile, defaultEntries, findings);

		foreach (var pair in defaultEntries)
		{
			if (families.Forms.ContainsKey(pair.Key))
			{
				continue;
			}

			var placeholders = LocalizationTemplate.ParsePlaceholders(pair.Value.Value);
			var parameters = ResolveParameters(defaultFile, pair.Value, placeholders, placeholders, findings);
			entries.Add(new LocalizationCompiledEntry(pair.Key,
				pair.Value.Value,
				parameters,
				LocalizationParameterDeclarations.ParseRemoval(pair.Value.Comment)));
		}

		foreach (var family in families.Families)
		{
			entries.Add(family.Compile(defaultFile, findings));
		}

		entries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));

		ReportGroupCollisions(defaultFile, entries, findings);

		foreach (var file in usable)
		{
			var culture = file.Culture ?? DefaultCulture;
			cultures.Add(culture);

			var fileEntries = file.IsDefault ? defaultEntries : CollectEntries(file, findings);
			var templates = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var pair in fileEntries)
			{
				templates[pair.Key] = pair.Value.Value;

				if (file.IsDefault)
				{
					continue;
				}

				if (!defaultEntries.TryGetValue(pair.Key, out var fallback))
				{
					findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.MissingDefaultResource,
						file.Path,
						pair.Key,
						$"'{pair.Key}' is translated in '{file.FileName}' but missing from '{set.BaseName}.resx'."));
					continue;
				}

				// A plural form may legitimately drop a placeholder the other form needs - "one icon"
				// against "{count} icons" - so a form is checked against the family's placeholders and
				// only an unknown one is a finding.
				if (families.Forms.TryGetValue(pair.Key, out var form))
				{
					ComparePlaceholders(file,
						pair.Key,
						form.Family.Placeholders,
						pair.Value.Value,
						true,
						findings);
					continue;
				}

				ComparePlaceholders(file,
					pair.Key,
					LocalizationTemplate.ParsePlaceholders(fallback.Value),
					pair.Value.Value,
					false,
					findings);
			}

			catalog[culture] = templates;
		}

		return new LocalizationCompilationResult(set.Scope,
			DefaultCulture,
			cultures,
			entries,
			catalog,
			findings);
	}

	/// <summary>
	/// Reports a key that is both a member and the group other keys nest under - <c>Filters</c> alongside
	/// <c>Filters.Date</c>. A dotted key becomes a nested class, so the generated code would declare a
	/// method and a class of the same name in the same scope; without this the author sees a duplicate
	/// definition error inside generated source they did not write.
	/// </summary>
	private static void ReportGroupCollisions(LocalizationResourceFile file,
		List<LocalizationCompiledEntry> entries,
		List<LocalizationFinding> findings)
	{
		var groups = new HashSet<string>(StringComparer.Ordinal);

		foreach (var entry in entries)
		{
			var separator = entry.Key.IndexOf('.');

			while (separator >= 0)
			{
				groups.Add(entry.Key.Substring(0, separator));
				separator = entry.Key.IndexOf('.', separator + 1);
			}
		}

		foreach (var entry in entries)
		{
			if (groups.Contains(entry.Key))
			{
				findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.KeyIsAlsoAGroup,
					file.Path,
					entry.Key,
					$"'{entry.Key}' is a key and also the group '{entry.Key}.…' keys nest under; " +
					"rename one of them."));
			}
		}
	}

	private static LocalizationResourceFile? FindDefault(List<LocalizationResourceFile> files)
	{
		foreach (var file in files)
		{
			if (file.IsDefault)
			{
				return file;
			}
		}

		return null;
	}

	private static Dictionary<string, ResxEntry> CollectEntries(LocalizationResourceFile file,
		List<LocalizationFinding> findings)
	{
		var collected = new Dictionary<string, ResxEntry>(StringComparer.Ordinal);

		foreach (var entry in file.Entries)
		{
			if (collected.ContainsKey(entry.Name))
			{
				findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.DuplicateKey,
					file.Path,
					entry.Name,
					$"'{entry.Name}' is declared more than once in '{file.FileName}'."));
				continue;
			}

			collected.Add(entry.Name, entry);
		}

		return collected;
	}

	/// <summary>
	/// Groups the entries marked <c>[plural]</c> into families. A family that cannot produce a coherent
	/// member - a form name outside the closed set, no <c>Other</c> to fall back on, or a base key some
	/// ordinary entry already owns - is reported and its forms fall back to being ordinary entries, so a
	/// broken declaration costs a diagnostic rather than silently removing members.
	/// </summary>
	private static PluralFamilies CollectPluralFamilies(LocalizationResourceFile file,
		Dictionary<string, ResxEntry> defaultEntries,
		List<LocalizationFinding> findings)
	{
		var byBaseKey = new Dictionary<string, PluralFamily>(StringComparer.Ordinal);
		var ordered = new List<PluralFamily>();

		foreach (var pair in defaultEntries)
		{
			if (!LocalizationParameterDeclarations.DeclaresPlural(pair.Value.Comment))
			{
				continue;
			}

			if (!LocalizationPluralForms.TrySplit(pair.Key, out var baseKey, out var form))
			{
				findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.InvalidPluralFamily,
					file.Path,
					pair.Key,
					$"'{pair.Key}' is declared '[{LocalizationPluralForms.Marker}]' but does not end in a " +
					$"plural form; expected '{string.Join("' or '", LocalizationPluralForms.All)}'."));
				continue;
			}

			if (!byBaseKey.TryGetValue(baseKey, out var family))
			{
				family = new PluralFamily(baseKey);
				byBaseKey.Add(baseKey, family);
				ordered.Add(family);
			}

			family.Add(form, pair.Value);
		}

		var forms = new Dictionary<string, PluralForm>(StringComparer.Ordinal);
		var families = new List<PluralFamily>();

		foreach (var family in ordered)
		{
			if (!family.Has(LocalizationPluralForms.Other))
			{
				findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.InvalidPluralFamily,
					file.Path,
					family.BaseKey + "." + LocalizationPluralForms.One,
					$"'{family.BaseKey}' has no '{LocalizationPluralForms.Other}' form; every count that " +
					"is not one resolves through it."));
				continue;
			}

			if (defaultEntries.ContainsKey(family.BaseKey))
			{
				findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.InvalidPluralFamily,
					file.Path,
					family.BaseKey,
					$"'{family.BaseKey}' is both an ordinary key and the base of a plural family; " +
					"one of them has to be renamed."));
				continue;
			}

			families.Add(family);

			foreach (var form in family.Forms)
			{
				forms.Add(LocalizationPluralForms.FormKey(family.BaseKey, form.Form), form);
			}
		}

		return new PluralFamilies(families, forms);
	}

	private static List<LocalizationCompiledParameter> ResolveParameters(LocalizationResourceFile file,
		ResxEntry entry,
		List<string> placeholders,
		List<string> declarable,
		List<LocalizationFinding> findings)
	{
		var declarations = LocalizationParameterDeclarations.Parse(entry.Comment);
		var declared = new Dictionary<string, LocalizationParameterDeclaration>(StringComparer.Ordinal);

		foreach (var declaration in declarations)
		{
			declared[declaration.Name] = declaration;

			if (!declaration.IsKnownType)
			{
				findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.UnknownParameterType,
					file.Path,
					entry.Name,
					$"'{entry.Name}' declares '{declaration.Name}' as '{declaration.DeclaredType}'; " +
					$"known types are {LocalizationParameterTypes.KnownNames}."));
				continue;
			}

			if (!declarable.Contains(declaration.Name))
			{
				findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.UnknownParameterType,
					file.Path,
					entry.Name,
					$"'{entry.Name}' declares a type for '{declaration.Name}', which it does not use."));
			}
		}

		var parameters = new List<LocalizationCompiledParameter>();
		foreach (var placeholder in placeholders)
		{
			var type = LocalizationParameterType.String;
			if (declared.TryGetValue(placeholder, out var declaration) && declaration.IsKnownType)
			{
				type = declaration.Type;
			}

			parameters.Add(new LocalizationCompiledParameter(placeholder, type));
		}

		return parameters;
	}

	private static void ComparePlaceholders(LocalizationResourceFile file,
		string key,
		List<string> expected,
		string translation,
		bool allowMissing,
		List<LocalizationFinding> findings)
	{
		var actual = LocalizationTemplate.ParsePlaceholders(translation);

		var missing = allowMissing ? new List<string>() : Except(expected, actual);
		var unexpected = Except(actual, expected);

		if (missing.Count == 0 && unexpected.Count == 0)
		{
			return;
		}

		var detail = missing.Count > 0
			? $"missing {Quote(missing)}"
			: string.Empty;

		if (unexpected.Count > 0)
		{
			detail = detail.Length > 0
				? $"{detail} and has unexpected {Quote(unexpected)}"
				: $"has unexpected {Quote(unexpected)}";
		}

		findings.Add(new LocalizationFinding(LocalizationDiagnosticIds.PlaceholderMismatch,
			file.Path,
			key,
			$"'{key}' in '{file.FileName}' {detail}."));
	}

	private static List<string> Except(List<string> left, List<string> right)
	{
		var result = new List<string>();
		foreach (var value in left)
		{
			if (!right.Contains(value))
			{
				result.Add(value);
			}
		}

		return result;
	}

	private static string Quote(List<string> names)
	{
		for (var index = 0; index < names.Count; index++)
		{
			names[index] = "{" + names[index] + "}";
		}

		return string.Join(", ", names.ToArray());
	}

	/// <summary>The plural families of one resource set, and the form keys they consumed.</summary>
	private sealed class PluralFamilies
	{
		public PluralFamilies(List<PluralFamily> families, Dictionary<string, PluralForm> forms)
		{
			Families = families;
			Forms = forms;
		}

		/// <summary>The families that produced a member, in declaration order.</summary>
		public List<PluralFamily> Families { get; }

		/// <summary>Form key to its form, for the keys a family took over from ordinary generation.</summary>
		public Dictionary<string, PluralForm> Forms { get; }
	}

	/// <summary>One form of one family, and the family it belongs to.</summary>
	private sealed class PluralForm
	{
		public PluralForm(PluralFamily family, string form, ResxEntry entry)
		{
			Family = family;
			Form = form;
			Entry = entry;
		}

		public PluralFamily Family { get; }

		public string Form { get; }

		public ResxEntry Entry { get; }
	}

	/// <summary>
	/// The forms of one plural family. The generated member takes the count plus the union of every
	/// form's placeholders: a caller cannot know which form its count will select, so every value any
	/// form might need has to be passed for all of them.
	/// </summary>
	private sealed class PluralFamily
	{
		private readonly List<PluralForm> _forms = new();

		public PluralFamily(string baseKey) => BaseKey = baseKey;

		public string BaseKey { get; }

		public List<PluralForm> Forms => _forms;

		/// <summary>The union of every form's placeholders, the count first.</summary>
		public List<string> Placeholders
		{
			get
			{
				var union = new List<string> { LocalizationPluralForms.CountParameter };

				foreach (var form in Ordered())
				{
					foreach (var placeholder in LocalizationTemplate.ParsePlaceholders(form.Entry.Value))
					{
						if (!union.Contains(placeholder))
						{
							union.Add(placeholder);
						}
					}
				}

				return union;
			}
		}

		public void Add(string form, ResxEntry entry) => _forms.Add(new PluralForm(this, form, entry));

		public bool Has(string form)
		{
			foreach (var candidate in _forms)
			{
				if (string.Equals(candidate.Form, form, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		public LocalizationCompiledEntry Compile(LocalizationResourceFile file,
			List<LocalizationFinding> findings)
		{
			var other = Form(LocalizationPluralForms.Other)!;
			var placeholders = Placeholders;

			// Types and the MDLOC004 check come off the Other form, the one every family carries, but are
			// checked against the family's placeholders so declaring a type for a value only the One form
			// uses is not reported as unused.
			var parameters = ResolveParameters(file, other.Entry, placeholders, placeholders, findings);

			// The count selects the form, so it is numeric whether or not any template happens to print
			// it - "one icon" needs the value it does not show.
			for (var index = 0; index < parameters.Count; index++)
			{
				if (parameters[index].Name == LocalizationPluralForms.CountParameter &&
					parameters[index].Type == LocalizationParameterType.String)
				{
					parameters[index] = new LocalizationCompiledParameter(LocalizationPluralForms.CountParameter,
						LocalizationParameterType.Int);
				}
			}

			return new LocalizationCompiledEntry(BaseKey,
				other.Entry.Value,
				parameters,
				LocalizationParameterDeclarations.ParseRemoval(other.Entry.Comment));
		}

		private PluralForm? Form(string form)
		{
			foreach (var candidate in _forms)
			{
				if (string.Equals(candidate.Form, form, StringComparison.Ordinal))
				{
					return candidate;
				}
			}

			return null;
		}

		/// <summary>The forms in the closed set's order, so the generated parameter order is stable.</summary>
		private List<PluralForm> Ordered()
		{
			var ordered = new List<PluralForm>();

			foreach (var name in LocalizationPluralForms.All)
			{
				var form = Form(name);
				if (form != null)
				{
					ordered.Add(form);
				}
			}

			return ordered;
		}
	}
}
