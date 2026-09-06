using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP5001: use of an <c>[Obsolete]</c> member from <c>MacroDeck.Sdk</c>, <c>MacroDeck.Plugin.Hosting</c>
/// or <c>MacroDeck.Plugin.Protocol</c>.
///
/// <para>
/// The compiler already reports CS0618/CS0619 for any obsolete member, from any assembly. This rule
/// reports the same usages a second time, but only for the SDK's own deprecations, under an id a plugin
/// author can gate independently - <c>-warnaserror:MDP5001</c> escalates just the SDK's own obsolete
/// surface without also escalating an obsolete BCL or third-party API used elsewhere in the same project.
/// </para>
///
/// <para>
/// #416 ships this mechanism; #418 owns the policy of which SDK members are actually marked
/// <c>[Obsolete]</c>. The SDK has none today, so this rule fires on nothing in the shipped SDK - it is
/// tested against a synthetic <c>[Obsolete]</c> member declared in the test compilation itself.
/// </para>
///
/// <para>
/// #418 added <c>[MacroDeckDeprecated]</c>, which carries the removal version and the migration
/// guidance. Where that attribute is present this rule stands down in favour of MDP5002/MDP5004, so
/// this one now covers the remaining case: an SDK member marked <c>[Obsolete]</c> alone.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ObsoleteSdkMemberAnalyzer : DiagnosticAnalyzer
{
	private static readonly ImmutableHashSet<string> _trackedAssemblyNames = ImmutableHashSet.Create("MacroDeck.Sdk",
		"MacroDeck.Plugin.Hosting",
		"MacroDeck.Plugin.Protocol");

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.ObsoleteSdkMember];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;

			var referencesTrackedAssembly = _trackedAssemblyNames.Contains(compilation.AssemblyName ?? string.Empty) ||
				compilation.SourceModule.ReferencedAssemblySymbols.Any(assembly
					=> _trackedAssemblyNames.Contains(assembly.Name));

			if (!referencesTrackedAssembly)
			{
				return;
			}

			startContext.RegisterOperationAction(Analyze,
				OperationKind.Invocation,
				OperationKind.PropertyReference,
				OperationKind.FieldReference,
				OperationKind.EventReference,
				OperationKind.ObjectCreation,
				OperationKind.MethodReference);
		});
	}

	private static void Analyze(OperationAnalysisContext context)
	{
		var symbol = ReferencedSymbol(context.Operation);

		if (symbol is null ||
			!_trackedAssemblyNames.Contains(symbol.ContainingAssembly?.Name ?? string.Empty) ||
			!HasObsoleteAttribute(symbol))
		{
			return;
		}

		// MDP5002/MDP5004 report the same usage with the removal version, the replacement and the
		// migration guidance attached. Standing down here rather than reporting both keeps one
		// deprecated call site to one warning - the richer one.
		if (DeprecationMetadata.TryRead(symbol, out _))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ObsoleteSdkMember,
			context.Operation.Syntax.GetLocation(),
			symbol.Name));
	}

	private static ISymbol? ReferencedSymbol(IOperation operation) => operation switch
	{
		IInvocationOperation invocation => invocation.TargetMethod,
		IPropertyReferenceOperation property => property.Property,
		IFieldReferenceOperation field => field.Field,
		IEventReferenceOperation eventReference => eventReference.Event,
		IObjectCreationOperation creation => creation.Constructor,
		IMethodReferenceOperation methodReference => methodReference.Method,
		_ => null
	};

	private static bool HasObsoleteAttribute(ISymbol symbol)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (attribute.AttributeClass?.ToDisplayString() == WellKnownTypeNames.ObsoleteAttribute)
			{
				return true;
			}
		}

		return false;
	}
}
