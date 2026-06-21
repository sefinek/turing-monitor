namespace TuringMonitor.Sensors;

public sealed class SystemStats
{
	public DateTime Timestamp { get; init; } = DateTime.Now;

	public string CpuName { get; set; } = "CPU";
	public double CpuLoadPercent { get; set; }
	public double CpuClockMhz { get; set; }
	public bool CpuTempAvailable { get; set; }
	public double CpuTempC { get; set; }

	public double RamUsedPercent { get; set; }
	public double RamUsedGb { get; set; }
	public double RamTotalGb { get; set; }

	public string DiskName { get; set; } = "";
	public double DiskUsedPercent { get; set; }
	public double DiskUsedGb { get; set; }
	public double DiskTotalGb { get; set; }

	public double NetUpKbps { get; set; }
	public double NetDownKbps { get; set; }
	public double NetLinkMbps { get; set; }

	public bool GpuAvailable { get; set; }
	public string GpuName { get; set; } = "GPU";
	public double GpuLoadPercent { get; set; }
	public double GpuTempC { get; set; }
	public double GpuMemUsedPercent { get; set; }
}
