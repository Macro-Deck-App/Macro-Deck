using System.Reflection;
using System.Reflection.Emit;

namespace MacroDeckHost.Tests.UnitTests.Architecture;

internal static class AssemblyDependencies
{
	private const BindingFlags DeclaredMembers = BindingFlags.Public
		| BindingFlags.NonPublic
		| BindingFlags.Instance
		| BindingFlags.Static
		| BindingFlags.DeclaredOnly;

	private static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Select(field => (OpCode)field.GetValue(null)!)
		.ToDictionary(code => code.Value);

	public static IReadOnlyList<Type> TypesOf(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException exception)
		{
			return exception.Types.OfType<Type>().ToList();
		}
	}

	public static Type Outermost(Type type)
	{
		while (type.DeclaringType is { } declaring)
		{
			type = declaring;
		}

		return type;
	}

	public static IReadOnlySet<Type> ReferencedTypes(Type type)
	{
		var found = new HashSet<Type>();

		Add(found, type.BaseType);
		foreach (var implemented in type.GetInterfaces())
		{
			Add(found, implemented);
		}

		foreach (var field in type.GetFields(DeclaredMembers))
		{
			Add(found, field.FieldType);
		}

		foreach (var property in type.GetProperties(DeclaredMembers))
		{
			Add(found, property.PropertyType);
		}

		foreach (var method in type.GetMethods(DeclaredMembers)
			.Cast<MethodBase>()
			.Concat(type.GetConstructors(DeclaredMembers)))
		{
			if (method is MethodInfo info)
			{
				Add(found, info.ReturnType);
			}

			foreach (var parameter in method.GetParameters())
			{
				Add(found, parameter.ParameterType);
			}

			ScanBody(method, found, null);
		}

		return found;
	}

	public static IReadOnlySet<string> StringLiterals(Type type)
	{
		var found = new HashSet<string>(StringComparer.Ordinal);

		foreach (var field in type.GetFields(DeclaredMembers))
		{
			if (field.IsLiteral && field.GetRawConstantValue() is string constant)
			{
				found.Add(constant);
			}
		}

		foreach (var method in type.GetMethods(DeclaredMembers)
			.Cast<MethodBase>()
			.Concat(type.GetConstructors(DeclaredMembers)))
		{
			ScanBody(method, null, found);
		}

		return found;
	}

	private static void ScanBody(MethodBase method, HashSet<Type>? types, HashSet<string>? strings)
	{
		MethodBody? body;
		try
		{
			body = method.GetMethodBody();
		}
		catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException)
		{
			return;
		}

		if (body?.GetILAsByteArray() is not { } il)
		{
			return;
		}

		if (types is not null)
		{
			foreach (var local in body.LocalVariables)
			{
				Add(types, local.LocalType);
			}
		}

		var typeArguments = method.DeclaringType is { IsGenericType: true } declaring
			? declaring.GetGenericArguments()
			: null;
		var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
		var position = 0;

		while (position < il.Length)
		{
			short value = il[position++];
			if (value == 0xFE && position < il.Length)
			{
				value = (short)(0xFE00 | il[position++]);
			}

			if (!_opCodes.TryGetValue(value, out var code))
			{
				return;
			}

			switch (code.OperandType)
			{
				case OperandType.InlineNone:
					break;
				case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
					position += 1;
					break;
				case OperandType.InlineVar:
					position += 2;
					break;
				case OperandType.InlineI8 or OperandType.InlineR:
					position += 8;
					break;
				case OperandType.InlineSwitch:
					var count = BitConverter.ToInt32(il, position);
					position += 4 + count * 4;
					break;
				case OperandType.InlineString:
					if (strings is not null)
					{
						strings.Add(method.Module.ResolveString(BitConverter.ToInt32(il, position)));
					}

					position += 4;
					break;
				case OperandType.InlineField
					or OperandType.InlineMethod
					or OperandType.InlineTok
					or OperandType.InlineType:
					if (types is not null)
					{
						AddMember(types,
							method.Module,
							BitConverter.ToInt32(il, position),
							typeArguments,
							methodArguments);
					}

					position += 4;
					break;
				default:
					position += 4;
					break;
			}
		}
	}

	private static void AddMember(
		HashSet<Type> found,
		Module module,
		int token,
		Type[]? typeArguments,
		Type[]? methodArguments)
	{
		MemberInfo? member;
		try
		{
			member = module.ResolveMember(token, typeArguments, methodArguments);
		}
		catch (Exception exception) when (exception is ArgumentException
			or BadImageFormatException
			or TypeLoadException
			or FileNotFoundException
			or MissingMemberException)
		{
			return;
		}

		switch (member)
		{
			case Type type:
				Add(found, type);
				break;
			case FieldInfo field:
				Add(found, field.DeclaringType);
				Add(found, field.FieldType);
				break;
			case MethodBase method:
				Add(found, method.DeclaringType);
				if (method is MethodInfo info)
				{
					Add(found, info.ReturnType);
				}

				break;
		}
	}

	private static void Add(HashSet<Type> found, Type? type)
	{
		if (type is null || type.IsGenericParameter)
		{
			return;
		}

		if (type.HasElementType)
		{
			Add(found, type.GetElementType());
			return;
		}

		if (type.IsGenericType && !type.IsGenericTypeDefinition)
		{
			foreach (var argument in type.GetGenericArguments())
			{
				Add(found, argument);
			}

			type = type.GetGenericTypeDefinition();
		}

		found.Add(type);
	}
}
