using System.Globalization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Delegation;

internal sealed record DelegateSharedImport(VariableDefinition Definition, DelegateRemote Remote, string RemoteName);

internal sealed class DelegateSharedImports
{
	public static readonly DelegateSharedImports Empty = new([], new Dictionary<string, IReadOnlyList<string>>());

	private readonly Dictionary<string, DelegateSharedImport> _byDefinitionId;

	private DelegateSharedImports(
		IReadOnlyList<DelegateSharedImport> imports,
		IReadOnlyDictionary<string, IReadOnlyList<string>> skipped)
	{
		Imports = imports;
		Skipped = skipped;
		_byDefinitionId = imports.ToDictionary(i => i.Definition.ResolvedId!, StringComparer.Ordinal);
	}

	public IReadOnlyList<DelegateSharedImport> Imports { get; }

	public IReadOnlyDictionary<string, IReadOnlyList<string>> Skipped { get; }

	public DelegateSharedImport? Find(string definitionId) => _byDefinitionId.GetValueOrDefault(definitionId);

	public static string LocalName(string variableKey, string remoteName)
		=> VariableNameSanitizer.Sanitize($"{variableKey}_{remoteName}");

	public static DelegateSharedImports Build(IReadOnlyList<DelegateRemote> remotes)
	{
		var taken = new HashSet<string>(remotes.Select(r => DelegateVariables.Declare(r.Instance.VariableKey).Name!),
			StringComparer.Ordinal);
		var room = VariableLimits.MaxEagerVariablesPerProvider - taken.Count;
		var imports = new List<DelegateSharedImport>();
		var skipped = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

		foreach (var remote in remotes.Where(r => r.ImportsSharedVariables)
					.OrderBy(r => r.Instance.InstanceId, StringComparer.Ordinal))
		{
			var skippedHere = new List<string>();
			foreach (var variable in remote.SharedVariables.OrderBy(v => v.Name, StringComparer.Ordinal))
			{
				var definition = Declare(remote, variable);
				if (definition.ResolvedId is null || imports.Count >= room || !taken.Add(definition.Name!))
				{
					skippedHere.Add(variable.Name);
					continue;
				}

				imports.Add(new DelegateSharedImport(definition, remote, variable.Name));
			}

			if (skippedHere.Count > 0)
			{
				skipped[remote.Instance.InstanceId] = skippedHere;
			}
		}

		return new DelegateSharedImports(imports, skipped);
	}

	public static VariableReading Read(DelegateSharedImport import)
	{
		if (import.Remote.ReadShared(import.RemoteName) is not { } variable)
		{
			return VariableReading.Unavailable;
		}

		object? value = variable.Type switch
		{
			VariableType.Numeric => decimal.TryParse(variable.Value,
				NumberStyles.Number,
				CultureInfo.InvariantCulture,
				out var number)
				? number
				: null,
			VariableType.Boolean => bool.TryParse(variable.Value, out var flag) ? flag : null,
			_ => variable.Value
		};

		return value is null
			? VariableReading.Unavailable
			: VariableReading.Of(value, variable.Min, variable.Max, variable.Step);
	}

	public static string? Format(object? value) => value switch
	{
		null => null,
		bool flag => flag ? "true" : "false",
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString()
	};

	private static VariableDefinition Declare(DelegateRemote remote, DelegateSharedVariable variable)
		=> VariableDefinition.Eager(LocalName(remote.Instance.VariableKey, variable.Name),
				variable.Type,
				variable.DecimalPlaces,
				DelegateRemote.SharedVariableInterval) with
			{
				DisplayName = variable.Name,
				Unit = variable.Unit,
				Configuration = new VariableConfiguration(remote.Instance.EntryId.ToString("N"),
					remote.Instance.MachineName),
				Write = variable.CanWrite
					? new VariableWriteCapability { CommitOnRelease = variable.CommitOnRelease }
					: null
			};
}
