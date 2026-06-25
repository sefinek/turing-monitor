using System.Management;
using System.Text.RegularExpressions;

namespace TuringMonitor.Display;

public static class SerialPortLocator
{
	private const string KnownSerialNumber = "USB35INCHIPSV2";
	private const string KnownVid = "1A86";
	private const string KnownPid = "5722";

	private static readonly Regex ComPortRegex = new(@"\((COM\d+)\)", RegexOptions.IgnoreCase);

	public static string? AutoDetect()
	{
		foreach (SerialDevice device in EnumerateSerialDevices())
		{
			var id = device.PnpDeviceId.ToUpperInvariant();
			if (id.Contains(KnownSerialNumber) || (id.Contains("VID_" + KnownVid) && id.Contains("PID_" + KnownPid)))
				return device.PortName;
		}

		return null;
	}

	public static IEnumerable<SerialDevice> EnumerateSerialDevices()
	{
		using var searcher = new ManagementObjectSearcher(
			"SELECT Name, DeviceID, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

		foreach (ManagementBaseObject obj in searcher.Get())
		{
			var name = obj["Name"] as string;
			if (string.IsNullOrEmpty(name))
				continue;

			Match m = ComPortRegex.Match(name);
			if (!m.Success)
				continue;

			var port = m.Groups[1].Value.ToUpperInvariant();
			var pnpId = obj["PNPDeviceID"] as string ?? obj["DeviceID"] as string ?? string.Empty;
			yield return new SerialDevice(port, name, pnpId);
		}
	}

	public sealed record SerialDevice(string PortName, string Description, string PnpDeviceId);
}
