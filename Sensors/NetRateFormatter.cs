namespace TuringMonitor.Sensors;

public static class NetRateFormatter
{
	public static string Format(double kbps, string units)
	{
		if (units == "bits")
		{
			var kbit = kbps * 8.0;
			return kbit >= 1000 ? $"{kbit / 1000.0:0.0} Mbit/s" : $"{kbit:0} Kbit/s";
		}

		return kbps >= 1024 ? $"{kbps / 1024.0:0.0} MB/s" : $"{kbps:0} KB/s";
	}
}
