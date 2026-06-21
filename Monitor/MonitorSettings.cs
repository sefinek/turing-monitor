using TuringMonitor.Display;

namespace TuringMonitor.Monitor;

public sealed class MonitorSettings
{
	public string ComPort { get; set; } = "AUTO";
	public int Brightness { get; set; } = 50;
	public Orientation Orientation { get; set; } = Orientation.Landscape;
	public int IntervalMs { get; set; } = 1000;
	public bool ResetOnStartup { get; set; }
	public bool StartMinimized { get; set; }
	public bool Autostart { get; set; }
	public bool StartMonitoringOnLaunch { get; set; }
	public string ThemeName { get; set; } = "";
	public string TimeFormat { get; set; } = "HH:mm:ss";
	public string DateFormat { get; set; } = "ddd, dd MMM";
	public string DateCulture { get; set; } = "";
	public int NetLinkMbps { get; set; }
	public string NetUnits { get; set; } = "bytes";
	public string NetInterface { get; set; } = "";
	public string DiskDrive { get; set; } = "";
	public bool ExitWhenNoEthernet { get; set; }
	public bool ExitWhenAway { get; set; }
}
