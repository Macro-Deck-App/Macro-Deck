using System.Reflection;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Mcp;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public class McpToolSurfaceTests
{
	private static readonly string[] _forbiddenMessageNamespaces =
	[
		"MacroDeckHost.Application.Ui.Transport.Messages.Secrets",
		"MacroDeckHost.Application.Ui.Transport.Messages.Backups",
		"MacroDeckHost.Application.Ui.Transport.Messages.Portable",
		"MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow",
		"MacroDeckHost.Application.Ui.Transport.Messages.Security",
		"MacroDeckHost.Application.Ui.Transport.Messages.Logging",
		"MacroDeckHost.Application.Ui.Transport.Messages.Filesystem",
		"MacroDeckHost.Application.Ui.Transport.Messages.Plugins"
	];

	private static readonly Type[] _forbiddenServices =
	[
		typeof(ISecretService),
		typeof(IBackupService),
		typeof(IRestoreService),
		typeof(IBackupRecoveryKeyService),
		typeof(IIntegrationConfigStore),
		typeof(IConnectCredentialStore)
	];

	[Test]
	public void No_mcp_tool_can_reach_a_path_that_returns_a_secret_value()
	{
		var reachable = ToolTypes()
			.SelectMany(type => type.GetConstructors()
				.SelectMany(constructor => constructor.GetParameters())
				.Concat(type.GetMethods().SelectMany(method => method.GetParameters()))
				.Select(parameter => (Tool: type.Name, Dependency: parameter.ParameterType)))
			.Where(entry => IsForbidden(entry.Dependency))
			.Select(entry => $"{entry.Tool} -> {entry.Dependency}")
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ToolTypes(), Is.Not.Empty);
			Assert.That(reachable, Is.Empty);
		});
	}

	private static IEnumerable<Type> ToolTypes()
	{
		return typeof(McpServerSetup).Assembly.GetTypes()
			.Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);
	}

	private static bool IsForbidden(Type dependency)
	{
		if (_forbiddenServices.Contains(dependency))
		{
			return true;
		}

		return dependency.IsGenericType
			&& dependency.GetGenericTypeDefinition() == typeof(IUiTransportMessageHandler<,>)
			&& dependency.GetGenericArguments()
				.Any(argument => _forbiddenMessageNamespaces.Contains(argument.Namespace));
	}
}
