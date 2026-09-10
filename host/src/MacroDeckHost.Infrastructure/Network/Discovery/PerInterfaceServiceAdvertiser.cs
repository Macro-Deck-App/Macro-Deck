using MacroDeckHost.Application.Network.Discovery;

namespace MacroDeckHost.Infrastructure.Network.Discovery;

internal abstract class PerInterfaceServiceAdvertiser<TRegistration> : IServiceAdvertiser
	where TRegistration : class
{
	private readonly Dictionary<int, (AdvertisedInterface Target, TRegistration Registration)> _registered = [];
	private ServiceAdvertisement? _service;
	private bool _incomplete;

	public event Action? StateChanged
	{
		add { }
		remove { }
	}

	public abstract bool IsAvailable { get; }

	public bool NeedsRepublish => _incomplete || _registered.Values.Any(entry => !IsAlive(entry.Registration));

	protected IEnumerable<TRegistration> Registrations => _registered.Values.Select(entry => entry.Registration);

	public virtual void CheckLiveness()
	{
	}

	public void Publish(ServiceAdvertisement advertisement)
	{
		if (!advertisement.DescribesSameService(_service))
		{
			Withdraw();
		}

		_service = advertisement;
		_incomplete = false;

		foreach (var (index, entry) in _registered.ToList())
		{
			if (advertisement.Interfaces.Contains(entry.Target) && IsAlive(entry.Registration))
			{
				continue;
			}

			Unregister(entry.Registration);
			_registered.Remove(index);
		}

		Exception? failure = null;
		foreach (var target in advertisement.Interfaces)
		{
			if (_registered.ContainsKey(target.Index))
			{
				continue;
			}

			try
			{
				_registered[target.Index] = (target, Register(advertisement, target));
			}
			catch (Exception exception)
			{
				_incomplete = true;
				failure ??= exception;
			}
		}

		if (failure is not null)
		{
			throw new InvalidOperationException($"Advertising failed on at least one interface: {failure.Message}",
				failure);
		}
	}

	public void Withdraw()
	{
		foreach (var entry in _registered.Values)
		{
			Unregister(entry.Registration);
		}

		_registered.Clear();
		_service = null;
		_incomplete = false;
	}

	public void Dispose()
	{
		Withdraw();
		GC.SuppressFinalize(this);
	}

	protected abstract TRegistration Register(ServiceAdvertisement service, AdvertisedInterface target);

	protected abstract void Unregister(TRegistration registration);

	protected abstract bool IsAlive(TRegistration registration);
}
