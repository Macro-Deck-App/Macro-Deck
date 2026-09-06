namespace MacroDeckHost.Infrastructure.Autostart;

public record AutostartRegistration(string ExecutablePath, bool OpenMinimized);

public interface IAutostartRegistrar
{
	bool IsSupported { get; }

	AutostartRegistration? Read();

	void Write(AutostartRegistration registration);

	void Remove();
}
