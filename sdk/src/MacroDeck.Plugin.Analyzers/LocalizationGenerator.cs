using System.Collections.Immutable;
using System.Text;
using MacroDeck.Localization.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// Turns a project's <c>Localization/*.resx</c> into a strongly typed API - <c>Strings.ConnectedAs(userName)</c>
/// returning a <see cref="MacroDeck.Localization.LocalizedString" /> - together with the catalog data the
/// host registers when the plugin loads, and reports MDLOC001-MDLOC005 for resources that cannot produce
/// a coherent API.
///
/// <para>
/// A generator rather than the .NET SDK's own <c>ResXFileCodeGenerator</c>: that emits members returning
/// already-resolved <see cref="string" />, which forecloses the deferred resolution this framework exists
/// for, and it has no notion of a placeholder's type.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class LocalizationGenerator : IIncrementalGenerator
{
	private static readonly char[] _pathSeparators = { '/', '\\' };

	private const string ResourceExtension = ".resx";
	private const string ManifestFileName = "manifest.json";

	private static readonly string[] _manifestIdKey = { "id" };
	private const string ScopeProperty = "build_property.MacroDeckLocalizationScope";
	private const string ClassNameProperty = "build_property.MacroDeckLocalizationClassName";
	private const string RootNamespaceProperty = "build_property.RootNamespace";

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var resources = context.AdditionalTextsProvider
			.Where(static text => HasExtension(text.Path, ResourceExtension))
			.Select(static (text, token) => new ResourceInput(text.Path,
				FileNameOf(text.Path),
				text.GetText(token)?.ToString() ?? string.Empty))
			.Collect();

		var manifestIds = context.AdditionalTextsProvider
			.Where(static text => string.Equals(FileNameOf(text.Path),
				ManifestFileName,
				StringComparison.OrdinalIgnoreCase))
			.Select(static (text, token) => ReadManifestId(text.GetText(token)?.ToString()))
			.Collect();

		var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
			new GeneratorOptions(Value(provider.GlobalOptions, ScopeProperty),
				Value(provider.GlobalOptions, ClassNameProperty),
				Value(provider.GlobalOptions, RootNamespaceProperty)));

		context.RegisterSourceOutput(resources.Combine(manifestIds).Combine(options),
			static (sourceContext, source) =>
				Emit(sourceContext, source.Left.Left, source.Left.Right, source.Right));
	}

	private static void Emit(SourceProductionContext context,
		ImmutableArray<ResourceInput> resources,
		ImmutableArray<string?> manifestIds,
		GeneratorOptions options)
	{
		if (resources.IsDefaultOrEmpty)
		{
			return;
		}

		var scope = ResolveScope(options, manifestIds);
		if (scope is null)
		{
			// No manifest id and no explicit scope: this project cannot own a namespace yet. MDP1001
			// already reports the missing manifest identity; inventing a second diagnostic for the same
			// cause would just double the noise.
			return;
		}

		foreach (var set in GroupIntoSets(scope, resources))
		{
			var result = LocalizationCompilation.Compile(set.Value);

			foreach (var finding in result.Findings)
			{
				context.ReportDiagnostic(Diagnostic.Create(DescriptorFor(finding.Id),
					LocationOf(resources, finding),
					finding.Message));
			}

			if (result.Entries.Count == 0)
			{
				continue;
			}

			var className = options.ClassName ?? set.Value.BaseName;
			var source = LocalizationSourceEmitter.Emit(options.RootNamespace, className, result);
			context.AddSource($"{className}.Localization.g.cs", SourceText.From(source, Encoding.UTF8));
		}
	}

	private static string? ResolveScope(GeneratorOptions options, ImmutableArray<string?> manifestIds)
	{
		if (!string.IsNullOrWhiteSpace(options.Scope))
		{
			return options.Scope;
		}

		foreach (var id in manifestIds)
		{
			if (!string.IsNullOrWhiteSpace(id))
			{
				return LocalizationScopeNames.PluginPrefix + id;
			}
		}

		return null;
	}

	private static List<KeyValuePair<string, LocalizationResourceSet>> GroupIntoSets(string scope,
		ImmutableArray<ResourceInput> resources)
	{
		var sets = new Dictionary<string, LocalizationResourceSet>(StringComparer.OrdinalIgnoreCase);
		var ordered = new List<KeyValuePair<string, LocalizationResourceSet>>();

		foreach (var resource in resources.OrderBy(static resource => resource.Path, StringComparer.Ordinal))
		{
			if (!ResxDocument.TrySplitFileName(resource.FileName, out var baseName, out var culture))
			{
				continue;
			}

			var setKey = DirectoryOf(resource.Path) + "|" + baseName;

			if (!sets.TryGetValue(setKey, out var set))
			{
				set = new LocalizationResourceSet(scope, baseName);
				sets.Add(setKey, set);
				ordered.Add(new KeyValuePair<string, LocalizationResourceSet>(setKey, set));
			}

			set.Files.Add(new LocalizationResourceFile(resource.Path,
				resource.FileName,
				culture,
				ResxDocument.Parse(resource.Content)));
		}

		return ordered;
	}

	private static DiagnosticDescriptor DescriptorFor(string id)
	{
		switch (id)
		{
			case LocalizationDiagnosticIds.PlaceholderMismatch:
				return DiagnosticDescriptors.LocalizationPlaceholderMismatch;
			case LocalizationDiagnosticIds.DuplicateKey:
				return DiagnosticDescriptors.DuplicateLocalizationKey;
			case LocalizationDiagnosticIds.UnknownParameterType:
				return DiagnosticDescriptors.UnknownLocalizationParameterType;
			case LocalizationDiagnosticIds.InvalidCultureName:
				return DiagnosticDescriptors.InvalidLocalizationCulture;
			case LocalizationDiagnosticIds.MissingDefaultResource:
				return DiagnosticDescriptors.MissingDefaultLocalizationResource;
			case LocalizationDiagnosticIds.InvalidPluralFamily:
				return DiagnosticDescriptors.InvalidLocalizationPluralFamily;
			case LocalizationDiagnosticIds.KeyIsAlsoAGroup:
				return DiagnosticDescriptors.LocalizationKeyIsAlsoAGroup;
			default:
				throw new InvalidOperationException($"No descriptor is mapped to '{id}'.");
		}
	}

	/// <summary>Anchors a finding at the offending entry's <c>name="…"</c> attribute where there is one,
	/// and at the top of the file otherwise, so a build error points at the resource rather than at the
	/// project.</summary>
	private static Location LocationOf(ImmutableArray<ResourceInput> resources, LocalizationFinding finding)
	{
		var content = string.Empty;
		foreach (var resource in resources)
		{
			if (string.Equals(resource.Path, finding.FilePath, StringComparison.Ordinal))
			{
				content = resource.Content;
				break;
			}
		}

		var start = 0;
		var length = 0;

		if (finding.EntryName is { Length: > 0 } entryName)
		{
			var needle = "name=\"" + entryName + "\"";
			var found = content.IndexOf(needle, StringComparison.Ordinal);
			if (found >= 0)
			{
				start = found;
				length = needle.Length;
			}
		}

		return Location.Create(finding.FilePath,
			new TextSpan(start, length),
			LinePositionOf(content, start, length));
	}

	private static LinePositionSpan LinePositionOf(string content, int start, int length)
	{
		var line = 0;
		var lineStart = 0;

		for (var index = 0; index < start && index < content.Length; index++)
		{
			if (content[index] == '\n')
			{
				line++;
				lineStart = index + 1;
			}
		}

		var character = start - lineStart;
		return new LinePositionSpan(new LinePosition(line, character),
			new LinePosition(line, character + length));
	}

	private static string? ReadManifestId(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		var properties = ManifestJsonReader.ReadTopLevelStrings(json!, _manifestIdKey);
		return properties.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id.Value)
			? id.Value
			: null;
	}

	private static string? Value(AnalyzerConfigOptions options, string key)
		=> options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

	private static bool HasExtension(string path, string extension)
		=> path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

	private static string FileNameOf(string path)
	{
		var separator = path.LastIndexOfAny(_pathSeparators);
		return separator < 0 ? path : path.Substring(separator + 1);
	}

	private static string DirectoryOf(string path)
	{
		var separator = path.LastIndexOfAny(_pathSeparators);
		return separator < 0 ? string.Empty : path.Substring(0, separator);
	}

	private readonly struct ResourceInput : IEquatable<ResourceInput>
	{
		public ResourceInput(string path, string fileName, string content)
		{
			Path = path;
			FileName = fileName;
			Content = content;
		}

		public string Path { get; }

		public string FileName { get; }

		public string Content { get; }

		public bool Equals(ResourceInput other)
			=> string.Equals(Path, other.Path, StringComparison.Ordinal) &&
				string.Equals(Content, other.Content, StringComparison.Ordinal);

		public override bool Equals(object? obj) => obj is ResourceInput other && Equals(other);

		public override int GetHashCode() => (Path.GetHashCode() * 397) ^ Content.GetHashCode();
	}

	private readonly struct GeneratorOptions : IEquatable<GeneratorOptions>
	{
		public GeneratorOptions(string? scope, string? className, string? rootNamespace)
		{
			Scope = scope;
			ClassName = className;
			RootNamespace = rootNamespace;
		}

		public string? Scope { get; }

		public string? ClassName { get; }

		public string? RootNamespace { get; }

		public bool Equals(GeneratorOptions other)
			=> Scope == other.Scope && ClassName == other.ClassName && RootNamespace == other.RootNamespace;

		public override bool Equals(object? obj) => obj is GeneratorOptions other && Equals(other);

		public override int GetHashCode()
			=> ((Scope?.GetHashCode() ?? 0) * 397 ^ (ClassName?.GetHashCode() ?? 0)) * 397 ^
				(RootNamespace?.GetHashCode() ?? 0);
	}
}
