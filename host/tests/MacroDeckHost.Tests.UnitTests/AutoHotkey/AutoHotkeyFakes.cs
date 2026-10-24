using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Storage;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.State;
using MacroDeckHost.Integrations.AutoHotkey.Widgets;

namespace MacroDeckHost.Tests.UnitTests.AutoHotkey;

internal sealed class ManualClock : TimeProvider
{
	private long _ticks;

	public override long TimestampFrequency => TimeSpan.TicksPerSecond;

	public override long GetTimestamp()
	{
		return Interlocked.Read(ref _ticks);
	}

	public override DateTimeOffset GetUtcNow()
	{
		return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
	}

	public void Advance(TimeSpan delta)
	{
		Interlocked.Add(ref _ticks, delta.Ticks);
	}
}

internal sealed class InMemoryStorage : IIntegrationStorage
{
	private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);
	private readonly HashSet<string> _unreadable = new(StringComparer.Ordinal);

	public IReadOnlyDictionary<string, string> Documents => _documents;

	public void Put(string key, string content)
	{
		_documents[key] = content;
	}

	public void MakeUnreadable(string key)
	{
		_unreadable.Add(key);
	}

	public Task<IntegrationStorageReadResult> ReadAsync(
		string key,
		Func<string, bool>? isValid = null,
		CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		if (_unreadable.Contains(key))
		{
			return Task.FromResult(IntegrationStorageReadResult.Unreadable);
		}

		if (!_documents.TryGetValue(key, out var content))
		{
			return Task.FromResult(IntegrationStorageReadResult.Missing);
		}

		return Task.FromResult(isValid is null || isValid(content)
			? IntegrationStorageReadResult.Found(content)
			: IntegrationStorageReadResult.Unreadable);
	}

	public Task WriteAsync(string key, string content, CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		if (string.IsNullOrEmpty(content))
		{
			throw new ArgumentException("Storage rejects empty content.", nameof(content));
		}

		_documents[key] = content;
		return Task.CompletedTask;
	}

	public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		_documents.Remove(key);
		return Task.CompletedTask;
	}
}

internal sealed class FakeMachine : IAutoHotkeyMachine
{
	public Dictionary<(AutoHotkeyRegistryScope Scope, string Key, string Value), string> Registry { get; } = [];

	public Dictionary<string, AutoHotkeyFileVersion> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

	public HashSet<string> NotRunnable { get; } = new(StringComparer.OrdinalIgnoreCase);

	public bool Is64BitOperatingSystem { get; set; } = true;

	public string? ProgramFilesFolder { get; set; }

	public string? LocalAppDataFolder { get; set; }

	public string? ReadRegistryString(AutoHotkeyRegistryScope scope, string keyPath, string valueName)
	{
		return Registry.GetValueOrDefault((scope, keyPath, valueName));
	}

	public bool FileExists(string path)
	{
		return Files.ContainsKey(path);
	}

	public AutoHotkeyFileVersion? ReadFileVersion(string path)
	{
		return Files.GetValueOrDefault(path);
	}

	public Task<bool> CanRunAsync(string executablePath, CancellationToken cancellationToken)
	{
		return Task.FromResult(!NotRunnable.Contains(executablePath));
	}
}

internal sealed class PipeReader : TextReader
{
	private readonly Lock _gate = new();
	private readonly StringBuilder _buffer = new();
	private readonly SemaphoreSlim _signal = new(0);
	private bool _completed;

	public void Write(string text)
	{
		lock (_gate)
		{
			_buffer.Append(text);
		}

		_signal.Release();
	}

	public void Complete()
	{
		lock (_gate)
		{
			_completed = true;
		}

		_signal.Release();
	}

	public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
	{
		while (true)
		{
			lock (_gate)
			{
				if (_buffer.Length > 0)
				{
					var count = Math.Min(buffer.Length, _buffer.Length);
					_buffer.CopyTo(0, buffer.Span, count);
					_buffer.Remove(0, count);
					return count;
				}

				if (_completed)
				{
					return 0;
				}
			}

			await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
	{
		var line = new StringBuilder();
		var one = new char[1];
		while (await ReadAsync(one, cancellationToken).ConfigureAwait(false) == 1)
		{
			if (one[0] == '\n')
			{
				return line.ToString();
			}

			line.Append(one[0]);
		}

		return line.Length > 0 ? line.ToString() : null;
	}

	public override async Task<string> ReadToEndAsync(CancellationToken cancellationToken)
	{
		var text = new StringBuilder();
		var chunk = new char[256];
		int read;
		while ((read = await ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
		{
			text.Append(chunk, 0, read);
		}

		return text.ToString();
	}
}

internal sealed class RecordingWriter : TextWriter
{
	private readonly Lock _gate = new();
	private readonly StringBuilder _text = new();

	public event Action<string>? Written;

	public override Encoding Encoding => Encoding.UTF8;

	public string Text
	{
		get
		{
			lock (_gate)
			{
				return _text.ToString();
			}
		}
	}

	public override void Write(char value)
	{
		Write(value.ToString());
	}

	public override void Write(string? value)
	{
		if (value is null)
		{
			return;
		}

		lock (_gate)
		{
			_text.Append(value);
		}

		Written?.Invoke(value);
	}

	public override Task WriteAsync(string? value)
	{
		Write(value);
		return Task.CompletedTask;
	}

	public override Task FlushAsync()
	{
		return Task.CompletedTask;
	}
}

internal sealed class FakeProcess : IAutoHotkeyProcess
{
	private static int _nextId = 1000;

	private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _exitCode;

	public FakeProcess(AutoHotkeyStartInfo info)
	{
		Info = info;
		Id = Interlocked.Increment(ref _nextId);
	}

	public AutoHotkeyStartInfo Info { get; }

	public int Id { get; }

	public PipeReader OutputPipe { get; } = new();

	public PipeReader ErrorPipe { get; } = new();

	public RecordingWriter InputWriter { get; } = new();

	public TextReader Output => OutputPipe;

	public TextReader Error => ErrorPipe;

	public TextWriter Input => InputWriter;

	public bool HonoursClose { get; set; } = true;

	public int CloseRequests { get; private set; }

	public bool Killed { get; private set; }

	public bool HasExited => _exited.Task.IsCompleted;

	public int ExitCode => _exitCode;

	public string PreludePath => Info.Arguments[Info.Arguments.ToList().IndexOf("/include") + 1];

	public void Ready()
	{
		Send("{\"t\":\"ready\",\"v\":1}");
	}

	public void Send(string json)
	{
		OutputPipe.Write("\u001e" + json + "\n");
	}

	public void Exit(int code)
	{
		_exitCode = code;
		OutputPipe.Complete();
		ErrorPipe.Complete();
		_exited.TrySetResult();
	}

	public Task WaitForExitAsync(CancellationToken cancellationToken)
	{
		return _exited.Task.WaitAsync(cancellationToken);
	}

	public bool RequestClose()
	{
		CloseRequests++;
		if (HonoursClose)
		{
			_ = Task.Run(() => Exit(0));
		}

		return true;
	}

	public void Kill()
	{
		Killed = true;
		Exit(1);
	}

	public void Dispose()
	{
	}
}

internal sealed class FakeLauncher : IAutoHotkeyLauncher
{
	private readonly Lock _gate = new();
	private readonly List<FakeProcess> _started = [];

	public Action<FakeProcess>? OnStart { get; set; }

	public bool Fails { get; set; }

	public bool Disposed { get; private set; }

	public IReadOnlyList<FakeProcess> Started
	{
		get
		{
			lock (_gate)
			{
				return [.. _started];
			}
		}
	}

	public IAutoHotkeyProcess Start(AutoHotkeyStartInfo info)
	{
		if (Fails)
		{
			throw new InvalidOperationException("start failed");
		}

		var process = new FakeProcess(info);
		lock (_gate)
		{
			_started.Add(process);
		}

		OnStart?.Invoke(process);
		return process;
	}

	public void Dispose()
	{
		Disposed = true;
	}
}

internal sealed class RecordingUserVariables : IUserVariableApi
{
	public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

	public List<(string Name, UserVariableContext Context)> Reads { get; } = [];

	public Task<UserVariableWriteResult> ApplyAsync(
		string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		lock (Values)
		{
			Values[name] = value ?? string.Empty;
		}

		return Task.FromResult(UserVariableWriteResult.Applied());
	}

	public Task<UserVariableReadResult> ReadInContextAsync(
		string name,
		UserVariableContext context,
		CancellationToken cancellationToken = default)
	{
		lock (Values)
		{
			Reads.Add((name, context));
			return Task.FromResult(Values.TryGetValue(name, out var value)
				? UserVariableReadResult.Found(VariableType.Text, value, value)
				: UserVariableReadResult.Failed(UserVariableReadStatus.NotFound, "not found"));
		}
	}
}

internal sealed class RecordingNotifier : IUserNotifier
{
	public List<UserNotificationRequest> Notifications { get; } = [];

	public void Notify(UserNotificationRequest notification)
	{
		lock (Notifications)
		{
			Notifications.Add(notification);
		}
	}

	public void Dismiss(string key)
	{
	}
}

internal sealed class FakeResources : IUiResourceRegistry
{
	public Dictionary<string, byte[]> Registered { get; } = new(StringComparer.Ordinal);

	public List<string> Removed { get; } = [];

	public bool FailRegistrations { get; set; }

	public static string IdOf(string name)
	{
		return "test." + name;
	}

	public Task<UiResource> RegisterAsync(
		string name,
		ReadOnlyMemory<byte> content,
		string mediaType,
		CancellationToken cancellationToken = default)
	{
		if (FailRegistrations)
		{
			throw new UiResourceException(UiResourceErrorCode.Unsupported, "Registration failed.");
		}

		Assert.That(mediaType, Is.EqualTo("text/plain"));
		Registered[name] = content.ToArray();
		return Task.FromResult(new UiResource
		{
			ResourceId = IdOf(name),
			MediaType = mediaType,
			ByteLength = content.Length,
			ContentHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content.Span))
		});
	}

	public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
	{
		Removed.Add(name);
		return Task.CompletedTask;
	}
}

internal sealed class FakeOrigin : IUiEventOrigin
{
	public Dictionary<string, byte[]> Uploads { get; } = new(StringComparer.Ordinal);

	public List<Uri> OpenedUrls { get; } = [];

	public string? ClientId => "desktop";

	public Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(true);
	}

	public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(false);
	}

	public Task<ModalResult<T>> ShowModalAsync<T>(
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(ModalResult.FromCancellation<T>());
	}

	public Task<bool> OpenUrlAsync(Uri url, CancellationToken cancellationToken = default)
	{
		OpenedUrls.Add(url);
		return Task.FromResult(true);
	}

	public Task<ReadOnlyMemory<byte>> ReadUploadAsync(
		string uploadId,
		CancellationToken cancellationToken = default)
	{
		return Uploads.TryGetValue(uploadId, out var content)
			? Task.FromResult<ReadOnlyMemory<byte>>(content)
			: Task.FromException<ReadOnlyMemory<byte>>(new UiUploadNotFoundException());
	}
}

internal sealed class TestPageHost : IAutoHotkeyPageHost
{
	public AutoHotkeyScriptStore? Library { get; set; }

	public AutoHotkeyProcessManager? Runs { get; set; }

	public IUiResourceRegistry? Resources { get; set; }

	public AutoHotkeyInstallation Installation { get; set; } = AutoHotkeyInstallation.NotFound;

	public event Action? RuntimeChanged;

	public void Raise()
	{
		RuntimeChanged?.Invoke();
	}
}
