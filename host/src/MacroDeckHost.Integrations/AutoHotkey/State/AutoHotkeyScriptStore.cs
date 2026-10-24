using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Sdk.Storage;
using Serilog;

namespace MacroDeckHost.Integrations.AutoHotkey.State;

internal sealed class AutoHotkeyScriptStore : IDisposable
{
	public const string IndexKey = "scripts";
	public const int MaxSourceBytes = 1024 * 1024;
	public const int MaxNameLength = 100;

	private const int IndexVersion = 1;

	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private static readonly UTF8Encoding _strictUtf8 = new(false, true);
	private static readonly UTF8Encoding _utf8WithBom = new(true);

	private readonly IIntegrationStorage _storage;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private IReadOnlyList<AutoHotkeyScript> _scripts = [];

	public AutoHotkeyScriptStore(IIntegrationStorage storage, TimeProvider time, ILogger logger)
	{
		_storage = storage;
		_time = time;
		_logger = logger;
	}

	public event Action<AutoHotkeyLibraryChange>? Changed;

	public bool IsReadOnly { get; private set; }

	public IReadOnlyList<AutoHotkeyScript> Scripts => _scripts;

	public static string SourceKey(Guid id)
	{
		return "script-" + id.ToString("N");
	}

	public void Dispose()
	{
		_gate.Dispose();
	}

	public AutoHotkeyScript? Find(Guid id)
	{
		return _scripts.FirstOrDefault(script => script.Id == id);
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var result = await _storage.ReadAsync(IndexKey, IsValidIndex, cancellationToken).ConfigureAwait(false);
			switch (result.Status)
			{
				case IntegrationStorageReadStatus.Found:
					_scripts = [.. Deserialize(result.Content!).Scripts!.Select(ToScript)];
					IsReadOnly = false;
					break;
				case IntegrationStorageReadStatus.Missing:
					_scripts = [];
					IsReadOnly = false;
					break;
				default:
					_logger.Warning("The AutoHotkey script index could not be read; the library stays read-only");
					_scripts = [];
					IsReadOnly = true;
					break;
			}
		}
		finally
		{
			_gate.Release();
		}

		Raise(AutoHotkeyLibraryChangeKind.Loaded, null);
	}

	public async Task<AutoHotkeySource> ReadSourceAsync(Guid id, CancellationToken cancellationToken = default)
	{
		if (Find(id) is null)
		{
			throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.NotFound);
		}

		var result = await _storage.ReadAsync(SourceKey(id), IsValidSource, cancellationToken).ConfigureAwait(false);
		switch (result.Status)
		{
			case IntegrationStorageReadStatus.Found:
				return new AutoHotkeySource(AutoHotkeySourceStatus.Found,
					JsonSerializer.Deserialize<SourceDocument>(result.Content!, _json)!.Source ?? string.Empty);
			case IntegrationStorageReadStatus.Missing:
				return new AutoHotkeySource(AutoHotkeySourceStatus.Missing, string.Empty);
			default:
				_logger.Warning("The source of AutoHotkey script {ScriptId} could not be read", id);
				Update(id, script => script with { SourceUnreadable = true });
				return new AutoHotkeySource(AutoHotkeySourceStatus.Unreadable, string.Empty);
		}
	}

	public async Task<AutoHotkeyScript> CreateAsync(
		string name,
		string source = "",
		CancellationToken cancellationToken = default)
	{
		var trimmed = ValidName(name);
		CheckSize(source);

		AutoHotkeyScript created;
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			EnsureWritable();
			var id = Guid.NewGuid();
			var now = _time.GetUtcNow();
			created = new AutoHotkeyScript
			{
				Id = id,
				Name = trimmed,
				Slug = AutoHotkeyScriptSlugs.Create(trimmed, id, _scripts.Select(script => script.Slug)),
				CreatedAt = now,
				UpdatedAt = now
			};

			await WriteSourceAsync(id, source, cancellationToken).ConfigureAwait(false);
			await WriteIndexAsync([.. _scripts, created], cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}

		Raise(AutoHotkeyLibraryChangeKind.Added, created.Id);
		return created;
	}

	public async Task<AutoHotkeyScript> DuplicateAsync(
		Guid id,
		string name,
		CancellationToken cancellationToken = default)
	{
		var original = Find(id) ?? throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.NotFound);
		var source = await ReadSourceAsync(id, cancellationToken).ConfigureAwait(false);
		if (!source.IsWritable)
		{
			throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.SourceUnreadable);
		}

		var copy = await CreateAsync(name, source.Text, cancellationToken).ConfigureAwait(false);
		return await UpdateSettingsAsync(copy.Id,
				new AutoHotkeyScriptSettings(original.Description,
					false,
					original.IncludeHelper,
					original.WorkingDirectory),
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<AutoHotkeyScript> RenameAsync(
		Guid id,
		string name,
		CancellationToken cancellationToken = default)
	{
		var trimmed = ValidName(name);
		var renamed = await MutateIndexAsync(id,
				script => script with { Name = trimmed, UpdatedAt = _time.GetUtcNow() },
				cancellationToken)
			.ConfigureAwait(false);
		Raise(AutoHotkeyLibraryChangeKind.Renamed, id);
		return renamed;
	}

	public async Task<AutoHotkeyScript> UpdateSettingsAsync(
		Guid id,
		AutoHotkeyScriptSettings settings,
		CancellationToken cancellationToken = default)
	{
		var updated = await MutateIndexAsync(id,
				script => script with
				{
					Description = string.IsNullOrWhiteSpace(settings.Description) ? null : settings.Description.Trim(),
					StartWithMacroDeck = settings.StartWithMacroDeck,
					IncludeHelper = settings.IncludeHelper,
					WorkingDirectory = string.IsNullOrWhiteSpace(settings.WorkingDirectory)
						? null
						: settings.WorkingDirectory.Trim(),
					UpdatedAt = _time.GetUtcNow()
				},
				cancellationToken)
			.ConfigureAwait(false);
		Raise(AutoHotkeyLibraryChangeKind.SettingsChanged, id);
		return updated;
	}

	public async Task SaveSourceAsync(Guid id, string source, CancellationToken cancellationToken = default)
	{
		CheckSize(source);
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			EnsureWritable();
			var script = Find(id) ?? throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.NotFound);
			if (script.SourceUnreadable)
			{
				throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.SourceUnreadable);
			}

			await WriteSourceAsync(id, source, cancellationToken).ConfigureAwait(false);
			await WriteIndexAsync(Replace(script with { UpdatedAt = _time.GetUtcNow() }), cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}

		Raise(AutoHotkeyLibraryChangeKind.SourceSaved, id);
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			EnsureWritable();
			if (Find(id) is null)
			{
				throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.NotFound);
			}

			await WriteIndexAsync([.. _scripts.Where(script => script.Id != id)], cancellationToken)
				.ConfigureAwait(false);
			try
			{
				await _storage.DeleteAsync(SourceKey(id), cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_logger.Warning(ex, "The source document of a deleted AutoHotkey script could not be removed");
			}
		}
		finally
		{
			_gate.Release();
		}

		Raise(AutoHotkeyLibraryChangeKind.Deleted, id);
	}

	public Task<AutoHotkeyScript> ImportAsync(
		string fileName,
		ReadOnlyMemory<byte> content,
		string fallbackName,
		CancellationToken cancellationToken = default)
	{
		var source = DecodeImport(content);
		var name = Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/').Split('/')[^1]).Trim();
		if (name.Length > MaxNameLength)
		{
			name = name[..MaxNameLength].Trim();
		}

		return CreateAsync(name.Length > 0 ? name : fallbackName, source, cancellationToken);
	}

	public async Task<AutoHotkeyExportResult> ExportAsync(
		Guid id,
		string folder,
		bool overwrite,
		CancellationToken cancellationToken = default)
	{
		var script = Find(id) ?? throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.NotFound);
		var source = await ReadSourceAsync(id, cancellationToken).ConfigureAwait(false);
		if (!source.IsWritable)
		{
			throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.SourceUnreadable);
		}

		var path = Path.Combine(folder, ExportFileName(script.Name));
		if (!overwrite && File.Exists(path))
		{
			return new AutoHotkeyExportResult(AutoHotkeyExportStatus.AlreadyExists, path);
		}

		try
		{
			await File.WriteAllTextAsync(path, source.Text, _utf8WithBom, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException
			or UnauthorizedAccessException
			or ArgumentException
			or NotSupportedException)
		{
			throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.ExportFailed, ex);
		}

		return new AutoHotkeyExportResult(AutoHotkeyExportStatus.Exported, path);
	}

	internal static string DecodeImport(ReadOnlyMemory<byte> content)
	{
		if (content.Length > MaxSourceBytes)
		{
			throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.TooLarge);
		}

		var bytes = content.Span;
		if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
		{
			bytes = bytes[3..];
		}

		try
		{
			return _strictUtf8.GetString(bytes);
		}
		catch (DecoderFallbackException ex)
		{
			throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.NotUtf8, ex);
		}
	}

	internal static string ExportFileName(string name)
	{
		var builder = new StringBuilder(name.Length);
		foreach (var character in name)
		{
			builder.Append(char.IsControl(character) || "<>:\"/\\|?*".Contains(character) ? '_' : character);
		}

		var cleaned = builder.ToString().Trim().TrimEnd('.', ' ');
		if (cleaned.Length == 0)
		{
			cleaned = "script";
		}

		var stem = cleaned.Split('.')[0];
		if (IsReservedDeviceName(stem))
		{
			cleaned = "_" + cleaned;
		}

		return cleaned + ".ahk";
	}

	internal static bool IsValidIndex(string content)
	{
		try
		{
			var document = Deserialize(content);
			var ids = new HashSet<Guid>();
			return document.Scripts!.All(entry => entry.Id != Guid.Empty
				&& ids.Add(entry.Id)
				&& !string.IsNullOrWhiteSpace(entry.Name)
				&& AutoHotkeyScriptSlugs.IsValid(entry.Slug));
		}
		catch (JsonException)
		{
			return false;
		}
	}

	internal static bool IsValidSource(string content)
	{
		try
		{
			return JsonSerializer.Deserialize<SourceDocument>(content, _json) is { Source: not null };
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static IndexDocument Deserialize(string content)
	{
		return JsonSerializer.Deserialize<IndexDocument>(content, _json) is { Scripts: not null } document
			? document
			: throw new JsonException("The AutoHotkey script index has no script list.");
	}

	private static bool IsReservedDeviceName(string stem)
	{
		var upper = stem.Trim().ToUpperInvariant();
		if (upper is "CON" or "PRN" or "AUX" or "NUL")
		{
			return true;
		}

		return upper.Length == 4 && upper[..3] is "COM" or "LPT" && char.IsDigit(upper[3]);
	}

	private static string ValidName(string name)
	{
		var trimmed = name.Trim();
		if (trimmed.Length is 0 or > MaxNameLength || trimmed.Any(char.IsControl))
		{
			throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.InvalidName);
		}

		return trimmed;
	}

	private static void CheckSize(string source)
	{
		if (Encoding.UTF8.GetByteCount(source) > MaxSourceBytes)
		{
			throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.TooLarge);
		}
	}

	private static AutoHotkeyScript ToScript(IndexEntry entry)
	{
		return new AutoHotkeyScript
		{
			Id = entry.Id,
			Name = entry.Name!,
			Description = entry.Description,
			Slug = entry.Slug!,
			CreatedAt = entry.CreatedAt,
			UpdatedAt = entry.UpdatedAt,
			StartWithMacroDeck = entry.StartWithMacroDeck,
			IncludeHelper = entry.IncludeHelper,
			WorkingDirectory = entry.WorkingDirectory
		};
	}

	private static IndexEntry ToEntry(AutoHotkeyScript script)
	{
		return new IndexEntry
		{
			Id = script.Id,
			Name = script.Name,
			Description = script.Description,
			Slug = script.Slug,
			CreatedAt = script.CreatedAt,
			UpdatedAt = script.UpdatedAt,
			StartWithMacroDeck = script.StartWithMacroDeck,
			IncludeHelper = script.IncludeHelper,
			WorkingDirectory = script.WorkingDirectory
		};
	}

	private async Task<AutoHotkeyScript> MutateIndexAsync(
		Guid id,
		Func<AutoHotkeyScript, AutoHotkeyScript> change,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			EnsureWritable();
			var script = Find(id) ?? throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.NotFound);
			var updated = change(script);
			await WriteIndexAsync(Replace(updated), cancellationToken).ConfigureAwait(false);
			return updated;
		}
		finally
		{
			_gate.Release();
		}
	}

	private void EnsureWritable()
	{
		if (IsReadOnly)
		{
			throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.ReadOnly);
		}
	}

	private IReadOnlyList<AutoHotkeyScript> Replace(AutoHotkeyScript updated)
	{
		return [.. _scripts.Select(script => script.Id == updated.Id ? updated : script)];
	}

	private void Update(Guid id, Func<AutoHotkeyScript, AutoHotkeyScript> change)
	{
		_scripts = [.. _scripts.Select(script => script.Id == id ? change(script) : script)];
	}

	private async Task WriteSourceAsync(Guid id, string source, CancellationToken cancellationToken)
	{
		var content = JsonSerializer.Serialize(new SourceDocument { Source = source }, _json);
		await _storage.WriteAsync(SourceKey(id), content, cancellationToken).ConfigureAwait(false);
	}

	private async Task WriteIndexAsync(IReadOnlyList<AutoHotkeyScript> scripts, CancellationToken cancellationToken)
	{
		var document = new IndexDocument { Version = IndexVersion, Scripts = [.. scripts.Select(ToEntry)] };
		await _storage.WriteAsync(IndexKey, JsonSerializer.Serialize(document, _json), cancellationToken)
			.ConfigureAwait(false);
		_scripts = scripts;
	}

	private void Raise(AutoHotkeyLibraryChangeKind kind, Guid? id)
	{
		try
		{
			Changed?.Invoke(new AutoHotkeyLibraryChange(kind, id));
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "An AutoHotkey script library listener failed");
		}
	}

	private sealed record IndexDocument
	{
		public int Version { get; init; }

		public List<IndexEntry>? Scripts { get; init; }
	}

	private sealed record IndexEntry
	{
		public Guid Id { get; init; }

		public string? Name { get; init; }

		public string? Description { get; init; }

		public string? Slug { get; init; }

		public DateTimeOffset CreatedAt { get; init; }

		public DateTimeOffset UpdatedAt { get; init; }

		public bool StartWithMacroDeck { get; init; }

		public bool IncludeHelper { get; init; } = true;

		public string? WorkingDirectory { get; init; }
	}

	private sealed record SourceDocument
	{
		public string? Source { get; init; }
	}
}
