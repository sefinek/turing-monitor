using System.Runtime.InteropServices;
using System.Text;

namespace TuringMonitor.Sensors;

public sealed class NvidiaGpu : IDisposable
{

	private IntPtr _device;
	private bool _initialized;

	public bool Available { get; private set; }
	public string Name { get; private set; } = "GPU";

	public void Dispose()
	{
		if (_initialized)
		{
			try
			{
				_ = NvmlShutdown();
			}
			catch
			{
			}

			_initialized = false;
		}
	}

	[DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
	private static extern int NvmlInit();

	[DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
	private static extern int NvmlShutdown();

	[DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
	private static extern int NvmlGetHandle(uint index, out IntPtr device);

	[DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")]
	private static extern int NvmlGetName(IntPtr device, byte[] name, uint length);

	[DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
	private static extern int NvmlGetUtilization(IntPtr device, out NvmlUtilization utilization);

	[DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
	private static extern int NvmlGetTemperature(IntPtr device, int sensorType, out uint temp);

	[DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
	private static extern int NvmlGetMemory(IntPtr device, out NvmlMemory memory);

	public bool TryInitialize()
	{
		try
		{
			if (NvmlInit() != 0)
				return false;
			_initialized = true;

			if (NvmlGetHandle(0, out _device) != 0)
				return false;

			var buffer = new byte[96];
			if (NvmlGetName(_device, buffer, (uint)buffer.Length) == 0)
			{
				var len = Array.IndexOf(buffer, (byte)0);
				if (len < 0) len = buffer.Length;
				Name = Encoding.ASCII.GetString(buffer, 0, len);
			}

			Available = true;
			return true;
		}
		catch (DllNotFoundException)
		{
			return false;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public void Read(SystemStats stats)
	{
		if (!Available)
			return;

		try
		{
			stats.GpuAvailable = true;
			stats.GpuName = Name;

			if (NvmlGetUtilization(_device, out NvmlUtilization util) == 0)
				stats.GpuLoadPercent = util.Gpu;

			if (NvmlGetTemperature(_device, 0, out var temp) == 0)
				stats.GpuTempC = temp;

			if (NvmlGetMemory(_device, out NvmlMemory mem) == 0 && mem.Total > 0)
				stats.GpuMemUsedPercent = mem.Used / (double)mem.Total * 100.0;
		}
		catch
		{
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NvmlUtilization
	{
		public uint Gpu;
		public uint Memory;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NvmlMemory
	{
		public ulong Total;
		public ulong Free;
		public ulong Used;
	}
}
