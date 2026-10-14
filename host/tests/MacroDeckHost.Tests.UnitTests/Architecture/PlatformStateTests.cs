using System.Reflection;
using System.Runtime.CompilerServices;
using MacroDeckHost.Platform.Native;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Tests.UnitTests.Architecture;

[TestFixture]
internal sealed class PlatformStateTests
{
	private const string PlatformNamespace = "MacroDeckHost.Platform";

	private const BindingFlags DeclaredStatics =
		BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

	private const BindingFlags DeclaredInstanceFields =
		BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

	private static readonly string[] _allowed =
	[
		"MacroDeckHost.Platform.Focus.MacOsFocusedApplicationWatcher._instances",
		"MacroDeckHost.Platform.Lock.MacOsLockStateWatcher._instances",
		"MacroDeckHost.Platform.Lock.MacOsLockStateWatcher._nextToken",
		"MacroDeckHost.Platform.Native.MacOsAccessibility._startupPromptClaimed",
		"MacroDeckHost.Platform.Native.MacOsMainRunLoop._isPumping",
		"MacroDeckHost.Platform.Native.Win32Input._lastBlockedReportTicks",
		"MacroDeckHost.Platform.Notifications.WindowsNotificationService._nextId"
	];

	[Test]
	public void The_platform_library_keeps_no_shared_static_state()
	{
		var found = AssemblyDependencies.TypesOf(typeof(MacOsMainRunLoop).Assembly)
			.Where(type => IsPlatformCode(type) && !IsCompilerGenerated(type))
			.SelectMany(type => type.GetFields(DeclaredStatics))
			.Where(field => !field.IsLiteral && !(field.IsInitOnly && IsStateless(field.FieldType, [])))
			.Select(field => $"{field.DeclaringType!.FullName}.{field.Name}")
			.ToHashSet(StringComparer.Ordinal);

		var added = found.Except(_allowed).Order(StringComparer.Ordinal).ToList();
		var resolved = _allowed.Except(found).Order(StringComparer.Ordinal).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(added,
				Is.Empty,
				"MacroDeckHost.Platform holds static state the host and integrations could share. Keep state in instances each "
				+ "consumer creates, or add a process fact to the allow-list:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, added));
			Assert.That(resolved,
				Is.Empty,
				"These allow-list entries no longer occur. Remove them from PlatformStateTests:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, resolved));
		});
	}

	private static bool IsPlatformCode(Type type)
	{
		return type.Namespace is { } ns
			&& (ns == PlatformNamespace || ns.StartsWith(PlatformNamespace + ".", StringComparison.Ordinal));
	}

	private static bool IsCompilerGenerated(Type type)
	{
		for (var current = type; current is not null; current = current.DeclaringType)
		{
			if (current.IsDefined(typeof(CompilerGeneratedAttribute), false))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsStateless(Type type, HashSet<Type> visiting)
	{
		if (type.IsPrimitive
			|| type.IsEnum
			|| type == typeof(string)
			|| type == typeof(decimal)
			|| type == typeof(Type)
			|| type == typeof(Lock)
			|| type == typeof(ILogger)
			|| typeof(Delegate).IsAssignableFrom(type))
		{
			return true;
		}

		if (type.IsArray)
		{
			return IsStateless(type.GetElementType()!, visiting);
		}

		if (type.IsInterface || !visiting.Add(type))
		{
			return !type.IsInterface;
		}

		var fields = new List<FieldInfo>();
		for (var current = type;
			current is not null && current != typeof(object) && current != typeof(ValueType);
			current = current.BaseType)
		{
			fields.AddRange(current.GetFields(DeclaredInstanceFields));
		}

		return type.IsValueType
			? fields.All(field => IsStateless(field.FieldType, visiting))
			: fields.Count == 0;
	}
}
