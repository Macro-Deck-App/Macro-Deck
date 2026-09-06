using System.Globalization;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>Renders a <see cref="PluginInspectionReport" /> as text or JSON.</summary>
internal static class InspectionReportWriter
{
	// CamelCase explicitly: the nested records (InspectedEntrypoint and friends) are serialized directly
	// rather than remapped field-by-field into an anonymous object the way ManifestProblem is in
	// ValidationResultWriter, so without this policy their C# PascalCase property names would leak into
	// the JSON verbatim, inconsistent with every other property in this same document.
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static void Write(CliConsole console, CliOutputFormat format, PluginInspectionReport report, bool showDigest)
	{
		if (format == CliOutputFormat.Json)
		{
			WriteJson(console, report, showDigest);
			return;
		}

		WriteText(console, report, showDigest);
	}

	private static void WriteText(CliConsole console, PluginInspectionReport report, bool showDigest)
	{
		console.WriteLine($"{report.PluginId} {report.Version} ({report.Name})");

		if (report.Description is { Length: > 0 })
		{
			console.WriteLine(report.Description);
		}

		console.WriteLine();
		console.WriteLine("Entrypoints:");

		foreach (var entrypoint in report.Entrypoints)
		{
			var arguments = entrypoint.Arguments.Count > 0
				? " " + string.Join(' ', entrypoint.Arguments)
				: string.Empty;
			var runtime = entrypoint.RuntimeKind == nameof(PluginEntrypointRuntimeKind.FrameworkDependent)
				? $" (dotnet {entrypoint.DotnetVersion})"
				: string.Empty;
			var missing = entrypoint.Present == false ? " (missing)" : string.Empty;

			console.WriteLine($"  {entrypoint.Rid}: {entrypoint.Executable}{arguments}{runtime}{missing}");
		}

		console.WriteLine();
		console.WriteLine(report.Permissions.Count == 0 ? "Permissions: (none declared)" : "Permissions:");

		foreach (var permission in report.Permissions)
		{
			console.WriteLine($"  {permission.Name}" + (permission.Known ? string.Empty : " (unknown)"));
		}

		console.WriteLine();
		console.WriteLine(report.Languages.Count == 0
			? "Languages: (none declared)"
			: "Languages: " + string.Join(", ", report.Languages));

		WriteRelationships(console, "Dependencies", report.Dependencies);
		WriteRelationships(console, "Conflicts", report.Conflicts);
		WriteRelationships(console, "Icon packs", report.IconPacks);

		console.WriteLine();

		if (report.Compatibility is { } compatibility)
		{
			console.WriteLine("Compatibility:");

			if (compatibility.Sdk is { Length: > 0 })
			{
				console.WriteLine($"  sdk: {compatibility.Sdk}");
			}

			if (compatibility.ProtocolMinimum is { } minimum)
			{
				console.WriteLine($"  protocol: [{minimum}, {compatibility.ProtocolMaximum}]");
			}

			if (compatibility.MacroDeck is { Length: > 0 })
			{
				console.WriteLine($"  macroDeck: {compatibility.MacroDeck}");
			}
		}
		else
		{
			console.WriteLine("Compatibility: (none declared)");
		}

		console.WriteLine();
		console.WriteLine(report.Signature is { } signature
			? $"Signature: {signature.Algorithm} keyId={signature.KeyId} ({signature.ShapeDescription})"
			: "Signature: (not signed)");

		console.WriteLine();

		var sizes = $"Entries: {report.EntryCount}, uncompressed: {report.TotalUncompressedBytes} bytes";
		if (report.ArchiveBytes is { } archiveBytes)
		{
			var ratio = report.CompressionRatio?.ToString("F1", CultureInfo.InvariantCulture) ?? "n/a";
			sizes += $", archive: {archiveBytes} bytes, ratio: {ratio}:1";
		}

		console.WriteLine(sizes);

		if (showDigest)
		{
			var digest = Convert.ToBase64String(PluginArtifactDigest.Compute(report.Manifest));
			console.WriteLine();
			console.WriteLine("Digest to sign (base64): " + digest);
		}
	}

	private static void WriteRelationships(CliConsole console,
		string label,
		IReadOnlyList<InspectedRelationship> relationships)
	{
		console.WriteLine();
		console.WriteLine(relationships.Count == 0 ? $"{label}: (none declared)" : $"{label}:");

		foreach (var relationship in relationships)
		{
			var range = relationship.VersionRange is null ? string.Empty : $" {relationship.VersionRange}";
			console.WriteLine($"  {relationship.Id}{range}" + (relationship.Optional ? " (optional)" : string.Empty));
		}
	}

	private static void WriteJson(CliConsole console, PluginInspectionReport report, bool showDigest)
	{
		var payload = new
		{
			pluginId = report.PluginId,
			name = report.Name,
			version = report.Version,
			description = report.Description,
			entrypoints = report.Entrypoints,
			permissions = report.Permissions,
			languages = report.Languages,
			dependencies = report.Dependencies,
			conflicts = report.Conflicts,
			iconPacks = report.IconPacks,
			compatibility = report.Compatibility,
			signature = report.Signature,
			entryCount = report.EntryCount,
			totalUncompressedBytes = report.TotalUncompressedBytes,
			archiveBytes = report.ArchiveBytes,
			compressionRatio = report.CompressionRatio,
			warnings = report.Warnings,
			digestBase64 = showDigest ? Convert.ToBase64String(PluginArtifactDigest.Compute(report.Manifest)) : null
		};

		console.WriteLine(JsonSerializer.Serialize(payload, _jsonOptions));
	}
}
