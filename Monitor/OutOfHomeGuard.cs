using System.Net.NetworkInformation;
using System.Net.Sockets;
using TuringMonitor.Display;
using TuringMonitor.Logging;

namespace TuringMonitor.Monitor;

public static class OutOfHomeGuard
{
	private static readonly string[] VirtualAdapterMarkers =
	{
		"virtual", "vmware", "hyper-v", "vethernet", "virtualbox", "tap-windows", "tap adapter",
		"loopback", "pseudo", "vpn", "wan miniport", "bluetooth", "npcap", "wireguard",
		"tailscale", "zerotier", "docker", "wsl", "tunnel"
	};

	public static bool ShouldExit(MonitorSettings settings)
	{
		if (settings.ExitWhenAway)
		{
			if (DisplayPresent())
			{
				settings.ExitWhenAway = false;
				SettingsStore.Save(settings);
				AppLog.Info("Away mode: display detected - staying open and turning the toggle off");
				return false;
			}

			AppLog.Info("Away mode: display not connected - closing");
			return true;
		}

		if (settings.ExitWhenNoEthernet && !HasWiredConnection())
		{
			AppLog.Info("Away mode: no wired Ethernet connection - closing");
			return true;
		}

		return false;
	}

	private static bool DisplayPresent()
	{
		try
		{
			return SerialPortLocator.AutoDetect() is not null;
		}
		catch
		{
			return false;
		}
	}

	private static bool HasWiredConnection()
	{
		try
		{
			foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (ni.OperationalStatus != OperationalStatus.Up)
					continue;
				if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
				    or NetworkInterfaceType.Tunnel
				    or NetworkInterfaceType.Wireless80211
				    or NetworkInterfaceType.Ppp)
					continue;
				if (IsVirtual(ni.Description) || IsVirtual(ni.Name))
					continue;

				foreach (UnicastIPAddressInformation addr in ni.GetIPProperties().UnicastAddresses)
				{
					if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
						continue;

					var bytes = addr.Address.GetAddressBytes();
					if (bytes[0] == 169 && bytes[1] == 254)
						continue;

					return true;
				}
			}
		}
		catch
		{
		}

		return false;
	}

	private static bool IsVirtual(string text)
	{
		foreach (var marker in VirtualAdapterMarkers)
			if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
				return true;
		return false;
	}
}
