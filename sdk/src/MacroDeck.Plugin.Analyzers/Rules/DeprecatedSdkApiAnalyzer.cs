using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP5002 and MDP5004: use of an SDK API carrying <c>[MacroDeckDeprecated]</c>.
///
/// <para>
/// MDP5001 already reports plain <c>[Obsolete]</c> members of the SDK assemblies. This rule handles the
/// ones that additionally declare the Macro Deck lifecycle - when they were deprecated, which release
/// removes them, and what to use instead - so the message a plugin author reads names the migration
/// rather than just the fact. MDP5001 stands down on exactly these symbols, so a deprecated API is
/// reported once (see <see cref="ObsoleteSdkMemberAnalyzer" />).
/// </para>
///
/// <para>
/// MDP5004 is the same usage seen from the other end: an API whose declared removal version the SDK it
/// is being used from has already reached. That is a broken promise rather than a plugin author's
/// mistake, but it surfaces at the call site because that is the only place it is visible.
/// </para>
///
/// <para>
/// The rule is data-driven - it reads the attribute's own values out of metadata and never hard-codes a
/// deprecated API - so deprecating something needs no change here. See
/// <c>docs/src/content/docs/policies/deprecations.md</c>.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeprecatedSdkApiAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.DeprecatedSdkApi, DiagnosticDescriptors.RemovedSdkApi];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			// Nothing to find in a compilation that cannot even name the attribute - the common case for
			// a project that has no Macro Deck reference at all.
			if (startContext.Compilation.GetTypeByMetadataName(WellKnownTypeNames.MacroDeckDeprecatedAttribute)
				is null)
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

	/// <summary>
	/// The symbol a deprecation could sit on: the member itself, or - for a member that is merely a way
	/// of reaching a deprecated type - the containing type. Both are checked, member first, so a
	/// deprecated method on a deprecated type reports the method's own, more specific guidance.
	/// </summary>
	private static void Analyze(OperationAnalysisContext context)
	{
		var symbol = ReferencedSymbol(context.Operation);

		if (symbol is null)
		{
			return;
		}

		if (!DeprecationMetadata.TryRead(symbol, out var metadata))
		{
			if (symbol.ContainingType is null || !DeprecationMetadata.TryRead(symbol.ContainingType, out metadata))
			{
				return;
			}

			symbol = symbol.ContainingType;
		}

		var location = context.Operation.Syntax.GetLocation();
		var displayName = symbol.ToDisplayString();
		var sdkVersion = symbol.ContainingAssembly?.Identity.Version;

		if (sdkVersion is not null && DeprecationMetadata.IsAtOrBelow(metadata.RemovedIn, sdkVersion))
		{
			context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RemovedSdkApi,
				location,
				displayName,
				metadata.RemovedIn,
				sdkVersion.ToString(),
				metadata.DescribeGuidance()));

			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.DeprecatedSdkApi,
			location,
			displayName,
			metadata.DeprecatedIn ?? "an earlier version",
			metadata.RemovedIn ?? "a future version",
			metadata.DescribeGuidance()));
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
}
