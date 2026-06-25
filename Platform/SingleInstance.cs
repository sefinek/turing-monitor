namespace TuringMonitor.Platform;

public sealed class SingleInstance : IDisposable
{
	private const string EventName = @"Local\TuringMonitor.SingleInstance";

	private readonly EventWaitHandle _handle;
	private readonly RegisteredWaitHandle _registration;

	public SingleInstance()
	{
		_handle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName, out var createdNew);

		if (!createdNew)
			_handle.Set();

		_registration = ThreadPool.RegisterWaitForSingleObject(_handle, (_, _) => Superseded?.Invoke(), null,
			Timeout.Infinite, true);
	}

	public void Dispose()
	{
		_registration.Unregister(null);
		_handle.Dispose();
	}

	public event Action? Superseded;
}
