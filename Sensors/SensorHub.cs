using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NLog;

namespace TuringMonitor.Sensors;

public sealed class SensorHub : IDisposable
{

	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private readonly int _cpuBaseMhz;
	private readonly string _cpuName;

	private readonly PerformanceCounter? _cpuPerfCounter;

	private readonly NvidiaGpu _gpu = new();

	private readonly Dictionary<string, (long Recv, long Sent)> _netPrev = new();
	private readonly string _systemDriveRoot;
	private bool _cpuTempAvailable;

	private double _cpuTempC;
	private DateTime _cpuTempNextRead;
	private bool _hasCpuBaseline;
	private bool _hasNetBaseline;

	private ulong _prevIdle, _prevKernel, _prevUser;
	private DateTime _prevNetTime;
	private ManagementObjectSearcher? _thermalSearcher;

	public SensorHub(bool log = true)
	{
		_cpuName = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "CPU");
		_cpuBaseMhz = ReadRegistryInt(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", 0);
		_systemDriveRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

		try
		{
			_cpuPerfCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
			_cpuPerfCounter.NextValue();
		}
		catch
		{
			_cpuPerfCounter = null;
		}

		_gpu.TryInitialize();

		try
		{
			var scope = new ManagementScope(@"\\.\root\WMI");
			var query = new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
			_thermalSearcher = new ManagementObjectSearcher(scope, query);
			ReadCpuTemp(true);
		}
		catch
		{
			_thermalSearcher = null;
		}

		if (!_cpuTempAvailable)
		{
			_thermalSearcher?.Dispose();
			_thermalSearcher = null;
		}

		if (!log)
			return;

		Logger.Info($"CPU: {_cpuName} (base {_cpuBaseMhz} MHz)");
		Logger.Info(_cpuTempAvailable
			? $"CPU temperature: {_cpuTempC:0}°C (WMI thermal zone)"
			: "CPU temperature: unavailable (WMI MSAcpi_ThermalZoneTemperature not exposed)");
		Logger.Info(_gpu.Available ? $"GPU: {_gpu.Name}" : "GPU: none (NVIDIA NVML unavailable)");
	}

	public bool GpuAvailable => _gpu.Available;

	public string? NetInterfaceId { get; set; }
	public string DiskRoot { get; set; } = "";

	public void Dispose()
	{
		_cpuPerfCounter?.Dispose();
		_thermalSearcher?.Dispose();
		_gpu.Dispose();
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

	public SystemStats Read()
	{
		var stats = new SystemStats
		{
			CpuName = _cpuName,
			CpuLoadPercent = ReadCpuLoad(),
			CpuClockMhz = ReadCpuClock()
		};

		ReadCpuTemp();
		stats.CpuTempAvailable = _cpuTempAvailable;
		stats.CpuTempC = _cpuTempC;

		ReadMemory(stats);
		ReadDisk(stats);
		ReadNetwork(stats);
		_gpu.Read(stats);

		return stats;
	}

	private void ReadCpuTemp(bool force = false)
	{
		if (_thermalSearcher is null)
			return;

		DateTime now = DateTime.UtcNow;
		if (!force && now < _cpuTempNextRead)
			return;
		_cpuTempNextRead = now.AddSeconds(3);

		try
		{
			var best = double.NaN;
			foreach (ManagementBaseObject obj in _thermalSearcher.Get())
			{
				var raw = obj["CurrentTemperature"];
				if (raw is null)
					continue;

				var celsius = Convert.ToDouble(raw, CultureInfo.InvariantCulture) / 10.0 - 273.15;
				if (celsius > 0 && celsius < 150 && (double.IsNaN(best) || celsius > best))
					best = celsius;
			}

			if (!double.IsNaN(best))
			{
				_cpuTempC = best;
				_cpuTempAvailable = true;
			}
		}
		catch
		{
			_thermalSearcher?.Dispose();
			_thermalSearcher = null;
		}
	}

	private double ReadCpuLoad()
	{
		if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
			return 0;

		ulong idleV = idle.Value, kernelV = kernel.Value, userV = user.Value;

		if (!_hasCpuBaseline)
		{
			_prevIdle = idleV;
			_prevKernel = kernelV;
			_prevUser = userV;
			_hasCpuBaseline = true;
			return 0;
		}

		var idleDelta = idleV - _prevIdle;
		var kernelDelta = kernelV - _prevKernel;
		var userDelta = userV - _prevUser;

		_prevIdle = idleV;
		_prevKernel = kernelV;
		_prevUser = userV;

		var total = kernelDelta + userDelta;
		if (total == 0)
			return 0;

		double busy = total - idleDelta;
		return Math.Clamp(busy / total * 100.0, 0, 100);
	}

	private double ReadCpuClock()
	{
		if (_cpuPerfCounter is null || _cpuBaseMhz <= 0)
			return _cpuBaseMhz;

		try
		{
			double performance = _cpuPerfCounter.NextValue();
			return _cpuBaseMhz * performance / 100.0;
		}
		catch
		{
			return _cpuBaseMhz;
		}
	}

	private static void ReadMemory(SystemStats stats)
	{
		var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
		if (!GlobalMemoryStatusEx(ref mem))
			return;

		var totalGb = mem.TotalPhys / 1073741824.0;
		var usedGb = (mem.TotalPhys - mem.AvailPhys) / 1073741824.0;
		stats.RamTotalGb = totalGb;
		stats.RamUsedGb = usedGb;
		stats.RamUsedPercent = mem.MemoryLoad;
	}

	private void ReadDisk(SystemStats stats)
	{
		try
		{
			var root = string.IsNullOrEmpty(DiskRoot) ? _systemDriveRoot : DiskRoot;
			var drive = new DriveInfo(root);
			if (!drive.IsReady)
				return;

			var totalGb = drive.TotalSize / 1073741824.0;
			var usedGb = (drive.TotalSize - drive.TotalFreeSpace) / 1073741824.0;
			stats.DiskName = drive.Name.TrimEnd('\\', '/');
			stats.DiskTotalGb = totalGb;
			stats.DiskUsedGb = usedGb;
			stats.DiskUsedPercent = drive.TotalSize > 0 ? usedGb / totalGb * 100.0 : 0;
		}
		catch
		{
		}
	}

	private void ReadNetwork(SystemStats stats)
	{
		DateTime now = DateTime.UtcNow;
		var seconds = (now - _prevNetTime).TotalSeconds;

		double down = 0, up = 0;
		var bestDelta = -1L;
		long maxSpeed = 0;

		try
		{
			foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (ni.OperationalStatus != OperationalStatus.Up)
					continue;
				if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
					continue;
				if (!string.IsNullOrEmpty(NetInterfaceId) && ni.Id != NetInterfaceId)
					continue;

				if (ni.Speed > maxSpeed)
					maxSpeed = ni.Speed;

				IPv4InterfaceStatistics s = ni.GetIPv4Statistics();
				long recv = s.BytesReceived, sent = s.BytesSent;

				if (_hasNetBaseline && seconds > 0 && _netPrev.TryGetValue(ni.Id, out (long Recv, long Sent) prev))
				{
					var delta = recv - prev.Recv + (sent - prev.Sent);
					if (delta > bestDelta)
					{
						bestDelta = delta;
						down = Math.Max(0, recv - prev.Recv) / seconds / 1024.0;
						up = Math.Max(0, sent - prev.Sent) / seconds / 1024.0;
					}
				}

				_netPrev[ni.Id] = (recv, sent);
			}
		}
		catch
		{
			return;
		}

		stats.NetLinkMbps = maxSpeed > 0 ? maxSpeed / 1_000_000.0 : 0;
		_prevNetTime = now;
		_hasNetBaseline = true;

		if (bestDelta < 0)
			return;

		stats.NetDownKbps = down;
		stats.NetUpKbps = up;
	}

	private static string ReadRegistryString(string path, string name, string fallback)
	{
		try
		{
			using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
			return (key?.GetValue(name) as string)?.Trim() ?? fallback;
		}
		catch
		{
			return fallback;
		}
	}

	private static int ReadRegistryInt(string path, string name, int fallback)
	{
		try
		{
			using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
			var value = key?.GetValue(name);
			return value is int i ? i : fallback;
		}
		catch
		{
			return fallback;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct FileTime
	{
		public uint Low;
		public uint High;
		public readonly ulong Value => ((ulong)High << 32) | Low;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatusEx
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhys;
		public ulong AvailPhys;
		public ulong TotalPageFile;
		public ulong AvailPageFile;
		public ulong TotalVirtual;
		public ulong AvailVirtual;
		public ulong AvailExtendedVirtual;
	}
}
