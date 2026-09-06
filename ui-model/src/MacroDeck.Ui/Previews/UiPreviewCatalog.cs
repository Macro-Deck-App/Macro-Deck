using System.Reflection;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Previews;

/// <summary>
/// Discovers the <see cref="UiPreviewAttribute" /> scenarios an assembly declares.
/// </summary>
/// <remarks>
/// Nothing a scenario declares is invoked here - <see cref="Scan" /> reads metadata and binds a factory,
/// so discovering previews cannot run a scenario's mock setup, start a subscription, or otherwise change
/// how the process behaves.
/// </remarks>
public static class UiPreviewCatalog
{
	private const string ViewSuffix = "Previews";

	/// <summary>
	/// Scans <paramref name="assemblies" /> for preview scenarios. A malformed method is skipped and
	/// reported; the well-formed scenarios beside it still register. Results are ordered by id, so the
	/// same assemblies always produce the same catalogue.
	/// </summary>
	public static UiPreviewScanResult Scan(params IReadOnlyList<Assembly> assemblies)
	{
		ArgumentNullException.ThrowIfNull(assemblies);

		var registrations = new List<UiPreviewRegistration>();
		var diagnostics = new List<UiPreviewDiagnostic>();
		var claimed = new HashSet<string>(StringComparer.Ordinal);

		foreach (var method in assemblies.Distinct().SelectMany(DeclaredPreviewMethods)
			.OrderBy(Id, StringComparer.Ordinal))
		{
			var attribute = method.GetCustomAttribute<UiPreviewAttribute>()!;

			if (Reject(method) is { } reason)
			{
				diagnostics.Add(new UiPreviewDiagnostic { Member = Member(method), Reason = reason });
				continue;
			}

			var id = Id(method);
			if (!claimed.Add(id))
			{
				diagnostics.Add(new UiPreviewDiagnostic
				{
					Member = Member(method),
					Reason = $"Another preview already uses the id '{id}'. Overloads cannot both be previews."
				});
				continue;
			}

			var declaration = new UiPreviewDeclaration
			{
				Id = id,
				View = attribute.View ?? ViewNameOf(method.DeclaringType!),
				Scenario = attribute.Scenario,
				Profile = attribute.Profile
			};

			registrations.Add(new UiPreviewRegistration(declaration, surface => Build(method, surface)));
		}

		return new UiPreviewScanResult { Registrations = registrations, Diagnostics = diagnostics };
	}

	private static IEnumerable<MethodInfo> DeclaredPreviewMethods(Assembly assembly)
		=> SafeTypes(assembly)
			.SelectMany(type => type.GetMethods(BindingFlags.Public |
				BindingFlags.NonPublic |
				BindingFlags.Static |
				BindingFlags.Instance |
				BindingFlags.DeclaredOnly))
			.Where(method => method.IsDefined(typeof(UiPreviewAttribute), inherit: false));

	// A plugin can reference an assembly it does not ship every type's dependencies for. The types that
	// did load are still discoverable, and one unresolvable reference must not blank the whole catalogue.
	private static IEnumerable<Type> SafeTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException exception)
		{
			return exception.Types.OfType<Type>();
		}
	}

	private static string? Reject(MethodInfo method)
	{
		if (!method.IsStatic)
		{
			return "A preview method must be static.";
		}

		if (method.GetParameters().Length > 0)
		{
			return "A preview method must take no parameters.";
		}

		if (method.IsGenericMethodDefinition || method.DeclaringType!.IsGenericTypeDefinition)
		{
			return "A preview method must not be generic.";
		}

		return typeof(UiElement).IsAssignableFrom(method.ReturnType) ||
			typeof(UiView).IsAssignableFrom(method.ReturnType) ||
			typeof(UiPreview).IsAssignableFrom(method.ReturnType)
				? null
				: "A preview method must return a UiElement, a UiView or a UiPreview.";
	}

	private static UiPreviewInstance Build(MethodInfo method, UiSurface surface)
	{
		var built = method.Invoke(null, null);

		return built switch
		{
			UiPreview preview => new UiPreviewInstance(new UiView(surface, preview.Root), preview.Resources),
			UiView view => new UiPreviewInstance(view, resources: null),
			UiElement element => new UiPreviewInstance(new UiView(surface, element), resources: null),
			_ => throw new UiViewException($"The preview '{Member(method)}' returned null.")
		};
	}

	private static string Id(MethodInfo method)
		=> $"{method.DeclaringType!.Assembly.GetName().Name}:{method.DeclaringType.FullName}.{method.Name}";

	private static string Member(MethodInfo method) => $"{method.DeclaringType!.FullName}.{method.Name}";

	private static string ViewNameOf(Type declaringType)
	{
		var name = declaringType.Name;

		return name.Length > ViewSuffix.Length && name.EndsWith(ViewSuffix, StringComparison.Ordinal)
			? name[..^ViewSuffix.Length]
			: name;
	}
}

/// <summary>What <see cref="UiPreviewCatalog.Scan" /> found.</summary>
public sealed record UiPreviewScanResult
{
	/// <summary>The scenarios that registered, ordered by <see cref="UiPreviewDeclaration.Id" />.</summary>
	public required IReadOnlyList<UiPreviewRegistration> Registrations { get; init; }

	/// <summary>The methods that carried the attribute but could not register.</summary>
	public required IReadOnlyList<UiPreviewDiagnostic> Diagnostics { get; init; }
}
