using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP4002: the plugin overriding its own listener URL - <c>UseUrls(...)</c>,
/// <c>Configuration["urls"] = ...</c>, or <c>ASPNETCORE_URLS</c> in <c>launchSettings.json</c>.
///
/// <para>
/// The <c>launchSettings.json</c> check needs that file listed as an <c>AdditionalFiles</c> item -
/// neither <c>Microsoft.NET.Sdk</c> nor <c>Microsoft.NET.Sdk.Web</c> does this by default (it is a
/// <c>None</c>/tooling-only item instead). The package's own <c>build/MacroDeck.Plugin.Analyzers.props</c>
/// adds it automatically, alongside <c>manifest.json</c> for <see cref="ManifestIdentityAnalyzer" />, so a
/// consuming project needs no opt-in for either.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ListenerUrlOverrideAnalyzer : DiagnosticAnalyzer
{
	private const string ServerUrlsConfigurationKey = "urls";
	private const string AspNetCoreUrlsVariableName = "ASPNETCORE_URLS";

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.ListenerUrlOverride];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var webHostBuilderType = compilation.GetTypeByMetadataName(WellKnownTypeNames.WebHostBuilder);
			var configurationType = compilation.GetTypeByMetadataName(WellKnownTypeNames.Configuration);

			if (webHostBuilderType is not null)
			{
				startContext.RegisterOperationAction(
					operationContext => AnalyzeUseUrls(operationContext, webHostBuilderType),
					OperationKind.Invocation);
			}

			if (configurationType is not null)
			{
				startContext.RegisterOperationAction(
					operationContext => AnalyzeConfigurationIndexerAssignment(operationContext, configurationType),
					OperationKind.SimpleAssignment);
			}
		});

		context.RegisterAdditionalFileAction(AnalyzeLaunchSettings);
	}

	private static void AnalyzeUseUrls(OperationAnalysisContext context, INamedTypeSymbol webHostBuilderType)
	{
		var invocation = (IInvocationOperation)context.Operation;
		var receiverType = invocation.GetReceiverType();

		if (invocation.TargetMethod.Name != "UseUrls" || !receiverType.IsOrImplements(webHostBuilderType))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ListenerUrlOverride,
			invocation.Syntax.GetLocation(),
			"UseUrls(...)"));
	}

	private static void AnalyzeConfigurationIndexerAssignment(OperationAnalysisContext context,
		INamedTypeSymbol configurationType)
	{
		var assignment = (ISimpleAssignmentOperation)context.Operation;

		if (assignment.Target is not IPropertyReferenceOperation { Arguments.Length: 1 } propertyReference)
		{
			return;
		}

		var receiverType = propertyReference.Instance?.Type;
		if (!receiverType.IsOrImplements(configurationType))
		{
			return;
		}

		var key = propertyReference.Arguments[0].Value.GetConstantStringValueOrNull();
		if (key is null || !string.Equals(key, ServerUrlsConfigurationKey, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ListenerUrlOverride,
			assignment.Syntax.GetLocation(),
			$"Setting configuration key '{key}'"));
	}

	private static void AnalyzeLaunchSettings(AdditionalFileAnalysisContext context)
	{
		if (!string.Equals(Path.GetFileName(context.AdditionalFile.Path),
			"launchSettings.json",
			StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var text = context.AdditionalFile.GetText(context.CancellationToken);
		if (text is null)
		{
			return;
		}

		var content = text.ToString();
		var needle = "\"" + AspNetCoreUrlsVariableName + "\"";
		var index = content.IndexOf(needle, StringComparison.Ordinal);
		if (index < 0)
		{
			return;
		}

		var span = new TextSpan(index, needle.Length);
		var location = Location.Create(context.AdditionalFile.Path, span, text.Lines.GetLinePositionSpan(span));

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ListenerUrlOverride,
			location,
			"launchSettings.json's ASPNETCORE_URLS"));
	}
}
