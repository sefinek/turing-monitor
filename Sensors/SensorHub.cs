using System.Globalization;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using NLog;

namespace TuringMonitor.Sensors;

public sealed class SensorHub : IDisposable
{
	private const int InterfaceRefreshSeconds = 15;

	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private readonly int _cpuBaseMhz;
	private readonly string _cpuName;

	private readonly HardwareMonitor _hardware;

	private readonly Dictionary<string, (long Recv, long Sent)> _netPrev = new();
	private readonly string _systemDriveRoot;

	private List<NetworkInterface> _cachedInterfaces = new();

	private bool _hasCpuBaseline;
	private bool _hasNetBaseline;
	private DateTime _interfacesNextRefresh;

	private ulong _prevIdle, _prevKernel, _prevUser;
	private DateTime _prevNetTime;

	private ManagementObjectSearcher? _thermalSearcher;
	private double _wmiTempC;
	private bool _wmiTempAvailable;

	public SensorHub(bool log = true)
	{
		_cpuName = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "CPU");
		_cpuBaseMhz = ReadRegistryInt(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", 0);
		_systemDriveRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
		_hardware = new HardwareMonitor(_cpuBaseMhz);

		if (!log)
			return;

		Logger.Info($"CPU: {_cpuName} (base {_cpuBaseMhz} MHz)");

		_hardware.Read(new SystemStats());
		if (_hardware.CpuTemperatureAvailable)
		{
			Logger.Info("CPU temperature: available");
		}
		else
		{
			InitThermalZoneFallback();
			if (_wmiTempAvailable)
			{
				Logger.Info($"CPU temperature: available via WMI thermal zone fallback ({_wmiTempC:0}°C)");
			}
			else if (!IsElevated())
			{
				Logger.Info("CPU temperature: unavailable - run the app as administrator to enable it");
			}
			else
			{
				Logger.Info("CPU temperature: unavailable on this hardware");
				if (_hardware.CpuTemperatureSensorBlocked)
					Logger.Info("A temperature sensor was found but reads 0 - likely blocked by Windows Memory Integrity "
					            + "(Core Isolation) or another tool holding exclusive access (Ryzen Master, HWiNFO, Armoury Crate, MSI Center, etc.)");
				var sensors = string.Join(" | ", _hardware.DescribeCpuSensors());
				Logger.Debug($"CPU sensors reported by LibreHardwareMonitor: {(sensors.Length == 0 ? "(none)" : sensors)}");
			}
		}

		Logger.Info(_hardware.GpuAvailable ? $"GPU: {_hardware.GpuName}" : "GPU: none detected");
	}

	public bool GpuAvailable => _hardware.GpuAvailable;

	public string? NetInterfaceId { get; set; }
	public string DiskRoot { get; set; } = "";

	public void Dispose()
	{
		_hardware.Dispose();
		_thermalSearcher?.Dispose();
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
			CpuClockMhz = _cpuBaseMhz
		};

		_hardware.Read(stats);

		if (!stats.CpuTempAvailable && _thermalSearcher is not null)
		{
			ReadThermalZone();
			if (_wmiTempAvailable)
			{
				stats.CpuTempAvailable = true;
				stats.CpuTempC = _wmiTempC;
			}
		}

		ReadMemory(stats);
		ReadDisk(stats);
		ReadNetwork(stats);

		return stats;
	}

	private void InitThermalZoneFallback()
	{
		try
		{
			var scope = new ManagementScope(@"\\.\root\WMI");
			var query = new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
			_thermalSearcher = new ManagementObjectSearcher(scope, query);
			ReadThermalZone();
		}
		catch
		{
			_thermalSearcher = null;
		}

		if (!_wmiTempAvailable)
		{
			_thermalSearcher?.Dispose();
			_thermalSearcher = null;
		}
	}

	private void ReadThermalZone()
	{
		if (_thermalSearcher is null)
			return;

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
				_wmiTempC = best;
				_wmiTempAvailable = true;
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

		foreach (NetworkInterface ni in GetInterfaces())
		{
			long speed;
			IPv4InterfaceStatistics ipStats;
			try
			{
				speed = ni.Speed;
				ipStats = ni.GetIPv4Statistics();
			}
			catch
			{
				continue;
			}

			if (speed > maxSpeed)
				maxSpeed = speed;

			long recv = ipStats.BytesReceived, sent = ipStats.BytesSent;

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

		stats.NetLinkMbps = maxSpeed > 0 ? maxSpeed / 1_000_000.0 : 0;
		_prevNetTime = now;
		_hasNetBaseline = true;

		if (bestDelta < 0)
			return;

		stats.NetDownKbps = down;
		stats.NetUpKbps = up;
	}

	private List<NetworkInterface> GetInterfaces()
	{
		DateTime now = DateTime.UtcNow;
		if (now < _interfacesNextRefresh)
			return _cachedInterfaces;

		_interfacesNextRefresh = now.AddSeconds(InterfaceRefreshSeconds);
		try
		{
			_cachedInterfaces = NetworkInterface.GetAllNetworkInterfaces()
				.Where(ni => ni.OperationalStatus == OperationalStatus.Up)
				.Where(ni => ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
				.Where(ni => string.IsNullOrEmpty(NetInterfaceId) || ni.Id == NetInterfaceId)
				.ToList();
		}
		catch
		{
			_cachedInterfaces = new List<NetworkInterface>();
		}

		return _cachedInterfaces;
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

	private static bool IsElevated()
	{
		try
		{
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
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
