namespace MacroDeckHost.Infrastructure.Autostart;

public sealed class NullAutostartRegistrar : IAutostartRegistrar
{
	public bool IsSupported => false;

	public AutostartRegistration? Read() => null;

	public void Write(AutostartRegistration registration)
	{
	}

	public void Remove()
	{
	}
}
