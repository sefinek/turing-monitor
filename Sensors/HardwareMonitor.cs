using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace TuringMonitor.Sensors;

public sealed class HardwareMonitor : IDisposable
{
	private readonly Computer _computer;
	private readonly IHardware? _cpu;
	private readonly int _cpuBaseMhz;
	private readonly PerformanceCounter? _cpuPerfCounter;
	private readonly IHardware? _gpu;
	private readonly UpdateVisitor _visitor = new();

	public HardwareMonitor(int cpuBaseMhz)
	{
		_cpuBaseMhz = cpuBaseMhz;

		_computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
		_computer.Open();

		_cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
		_gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
		       ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
		       ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);

		// LHM only exposes per-core clock sensors when running elevated. Without admin rights,
		// fall back to this performance counter, which gives a turbo-adjusted estimate and needs no elevation.
		try
		{
			_cpuPerfCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
			_cpuPerfCounter.NextValue();
		}
		catch
		{
			_cpuPerfCounter = null;
		}
	}

	public bool CpuTemperatureAvailable { get; private set; }
	public bool GpuAvailable => _gpu is not null;
	public string GpuName => _gpu?.Name ?? "GPU";

	public void Dispose()
	{
		_cpuPerfCounter?.Dispose();
		_computer.Close();
	}

	public void Read(SystemStats stats)
	{
		_computer.Accept(_visitor);

		if (_cpu is not null)
		{
			stats.CpuClockMhz = ReadCpuClockMhz(_cpu);

			var temp = CpuTemperature(_cpu);
			CpuTemperatureAvailable = !double.IsNaN(temp);
			stats.CpuTempAvailable = CpuTemperatureAvailable;
			if (CpuTemperatureAvailable)
				stats.CpuTempC = temp;
		}

		if (_gpu is null)
			return;

		stats.GpuAvailable = true;
		stats.GpuName = _gpu.Name;

		var load = FindValue(_gpu, SensorType.Load, "GPU Core") ?? FindValue(_gpu, SensorType.Load, "D3D 3D");
		if (load is { } l)
			stats.GpuLoadPercent = l;

		var temperature = FindTemperature(_gpu, "GPU Core");
		if (temperature is { } t)
			stats.GpuTempC = t;

		var usedMem = FindValue(_gpu, SensorType.SmallData, "GPU Memory Used");
		var totalMem = FindValue(_gpu, SensorType.SmallData, "GPU Memory Total");
		if (usedMem is { } u && totalMem is > 0)
			stats.GpuMemUsedPercent = u / totalMem.Value * 100.0;
	}

	private double ReadCpuClockMhz(IHardware cpu)
	{
		var meanCoreClock = MeanClock(cpu);
		if (!double.IsNaN(meanCoreClock))
			return meanCoreClock;

		if (_cpuPerfCounter is not null && _cpuBaseMhz > 0)
			try
			{
				double performance = _cpuPerfCounter.NextValue();
				return _cpuBaseMhz * performance / 100.0;
			}
			catch
			{
			}

		return _cpuBaseMhz;
	}

	private static double MeanClock(IHardware cpu)
	{
		var values = cpu.Sensors
			.Where(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core #") && !s.Name.Contains("Effective") && s.Value is not null)
			.Select(s => (double)s.Value!.Value)
			.ToArray();
		return values.Length > 0 ? values.Average() : double.NaN;
	}

	private static double CpuTemperature(IHardware cpu)
	{
		return FindTemperature(cpu, "Core Average")
		       ?? FindTemperature(cpu, "Core Max")
		       ?? FindTemperature(cpu, "CPU Package")
		       ?? FindTemperature(cpu, "Core")
		       ?? double.NaN;
	}

	private static double? FindValue(IHardware hardware, SensorType type, string namePrefix)
	{
		ISensor? sensor = hardware.Sensors.FirstOrDefault(s =>
			s.SensorType == type && s.Name.StartsWith(namePrefix) && s.Value is not null);
		return sensor is null ? null : (double)sensor.Value!.Value;
	}

	// Without admin rights, some LHM temperature sensors exist but report a stuck 0 instead of
	// null when the underlying driver read fails - treat implausible values as unavailable too.
	private static double? FindTemperature(IHardware hardware, string namePrefix)
	{
		var value = FindValue(hardware, SensorType.Temperature, namePrefix);
		return value is > 0 and < 150 ? value : null;
	}

	private sealed class UpdateVisitor : IVisitor
	{
		public void VisitComputer(IComputer computer)
		{
			computer.Traverse(this);
		}

		public void VisitHardware(IHardware hardware)
		{
			hardware.Update();
			foreach (IHardware sub in hardware.SubHardware)
				sub.Accept(this);
		}

		public void VisitSensor(ISensor sensor)
		{
		}

		public void VisitParameter(IParameter parameter)
		{
		}
	}
}
