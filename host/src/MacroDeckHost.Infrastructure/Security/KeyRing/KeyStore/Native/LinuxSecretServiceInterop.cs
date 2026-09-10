using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Infrastructure.Native;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore.Native;

/// <summary>
/// libsecret bound through its non-variadic entry points. The documented
/// <c>secret_password_lookup_sync</c> family takes a NULL-terminated attribute varargs list, and C
/// variadics cannot be P/Invoked portably; the <c>*v_sync</c> forms take a <c>GHashTable</c> instead,
/// which is why glib is bound here as well.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxSecretServiceInterop
{
	private const string Secret = "libsecret-1.so.0";
	private const string Glib = "libglib-2.0.so.0";

	private const int SecretSchemaDontMatchName = 2;
	private const int SecretSchemaAttributeString = 0;

	// SecretSchema is { const gchar *name; SecretSchemaFlags flags; struct { const gchar *name; int
	// type; } attributes[32]; gint reserved; gpointer reserved1..7 }. Only the first attribute is
	// filled; the rest stay zeroed, which is how libsecret finds the end of the list.
	private const int SchemaSize = 640;
	private const int SchemaNameOffset = 0;
	private const int SchemaFlagsOffset = 8;
	private const int SchemaFirstAttributeNameOffset = 16;
	private const int SchemaFirstAttributeTypeOffset = 24;

	private const int GErrorMessageOffset = 8;

	private static readonly Lazy<LinuxSecretServiceBinding?> Binding = new(Bind);

	public static bool IsLoadable => Binding.Value is not null;

	public static IntPtr Schema => Binding.Value?.Schema ?? IntPtr.Zero;

	/// <summary>
	/// The table is created without destroy notifies, so glib frees its buckets and nothing else. The
	/// key and value allocations come back with it and are the caller's to release.
	/// </summary>
	public static LinuxSecretAttributes CreateAttributes(string account)
	{
		if (Binding.Value is not { } binding)
		{
			return LinuxSecretAttributes.None;
		}

		var table = HashTableNew(binding.StringHash, binding.StringEqual);
		if (table == IntPtr.Zero)
		{
			return LinuxSecretAttributes.None;
		}

		var key = NativeUtf8.Alloc("account");
		var value = NativeUtf8.Alloc(account);
		HashTableInsert(table, key, value);

		return new LinuxSecretAttributes(table, key, value);
	}

	public static string? TakeError(IntPtr error)
	{
		if (error == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			var message = Marshal.ReadIntPtr(error, GErrorMessageOffset);

			return message == IntPtr.Zero ? "libsecret reported an error" : Marshal.PtrToStringUTF8(message);
		}
		finally
		{
			ErrorFree(error);
		}
	}

	private static LinuxSecretServiceBinding? Bind()
	{
		if (!OperatingSystem.IsLinux())
		{
			return null;
		}

		try
		{
			if (!NativeLibrary.TryLoad(Secret, out var secret) || !NativeLibrary.TryLoad(Glib, out var glib))
			{
				return null;
			}

			if (!NativeLibrary.TryGetExport(secret, "secret_password_lookupv_sync", out _) ||
				!NativeLibrary.TryGetExport(secret, "secret_password_storev_sync", out _) ||
				!NativeLibrary.TryGetExport(glib, "g_hash_table_new", out _) ||
				!NativeLibrary.TryGetExport(glib, "g_str_hash", out var hash) ||
				!NativeLibrary.TryGetExport(glib, "g_str_equal", out var equal))
			{
				return null;
			}

			return new LinuxSecretServiceBinding(hash, equal, CreateSchema("app.macrodeck.KeyRing", "account"));
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// Allocated once and never freed: libsecret keeps the pointer for the lifetime of every entry
	/// created under it, and there is exactly one schema per process.
	/// </summary>
	private static IntPtr CreateSchema(string name, string attribute)
	{
		var schema = Marshal.AllocHGlobal(SchemaSize);
		for (var offset = 0; offset < SchemaSize; offset += IntPtr.Size)
		{
			Marshal.WriteIntPtr(schema, offset, IntPtr.Zero);
		}

		Marshal.WriteIntPtr(schema, SchemaNameOffset, NativeUtf8.Alloc(name));
		Marshal.WriteInt32(schema, SchemaFlagsOffset, SecretSchemaDontMatchName);
		Marshal.WriteIntPtr(schema, SchemaFirstAttributeNameOffset, NativeUtf8.Alloc(attribute));
		Marshal.WriteInt32(schema, SchemaFirstAttributeTypeOffset, SecretSchemaAttributeString);

		return schema;
	}

	[DllImport(Secret, EntryPoint = "secret_password_lookupv_sync")]
	public static extern IntPtr LookupSync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

	[DllImport(Secret, EntryPoint = "secret_password_storev_sync")]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool StoreSync(IntPtr schema,
		IntPtr attributes,
		IntPtr collection,
		IntPtr label,
		IntPtr password,
		IntPtr cancellable,
		out IntPtr error);

	[DllImport(Secret, EntryPoint = "secret_password_clearv_sync")]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool ClearSync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

	[DllImport(Secret, EntryPoint = "secret_password_free")]
	public static extern void PasswordFree(IntPtr password);

	[DllImport(Glib, EntryPoint = "g_hash_table_new")]
	private static extern IntPtr HashTableNew(IntPtr hashFunc, IntPtr equalFunc);

	[DllImport(Glib, EntryPoint = "g_hash_table_insert")]
	private static extern void HashTableInsert(IntPtr table, IntPtr key, IntPtr value);

	[DllImport(Glib, EntryPoint = "g_hash_table_destroy")]
	public static extern void HashTableDestroy(IntPtr table);

	[DllImport(Glib, EntryPoint = "g_error_free")]
	private static extern void ErrorFree(IntPtr error);

	private sealed record LinuxSecretServiceBinding(IntPtr StringHash, IntPtr StringEqual, IntPtr Schema);

	internal readonly record struct LinuxSecretAttributes(IntPtr Table, IntPtr Key, IntPtr Value) : IDisposable
	{
		public static LinuxSecretAttributes None => default;

		public bool IsValid => Table != IntPtr.Zero;

		public void Dispose()
		{
			if (Table != IntPtr.Zero)
			{
				HashTableDestroy(Table);
			}

			if (Key != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(Key);
			}

			if (Value != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(Value);
			}
		}
	}
}
