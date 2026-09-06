namespace MacroDeckHost.Infrastructure.Autostart;

public abstract class FileAutostartRegistrar : IAutostartRegistrar
{
	private readonly string _directory;

	protected FileAutostartRegistrar(string directory)
	{
		_directory = directory;
	}

	public bool IsSupported => true;

	protected abstract string FileName { get; }

	protected abstract string BuildContent(AutostartRegistration registration);

	protected abstract AutostartRegistration? ParseContent(string content);

	public AutostartRegistration? Read()
	{
		var path = EntryPath();
		if (!File.Exists(path))
		{
			return null;
		}

		return ParseContent(File.ReadAllText(path));
	}

	public void Write(AutostartRegistration registration)
	{
		Directory.CreateDirectory(_directory);
		File.WriteAllText(EntryPath(), BuildContent(registration));
	}

	public void Remove()
	{
		var path = EntryPath();
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private string EntryPath() => Path.Combine(_directory, FileName);
}
