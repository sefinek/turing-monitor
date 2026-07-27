using Orientation = TuringMonitor.Display.Orientation;

namespace TuringMonitor;

public partial class MainWindow
{
	private sealed record OrientationOption(string Name, Orientation Value)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record IntervalOption(string Name, int Ms)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record LocaleOption(string Name, string Culture)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record LinkSpeedOption(string Name, int Mbps)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record UnitsOption(string Name, string Value)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record NetInterfaceOption(string Name, string Id)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record DiskOption(string Name, string Root)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record ThemeItem(string Name, bool Compatible, bool IsDashboard)
	{
		public override string ToString()
		{
			return Compatible || IsDashboard ? Name : "⚠  " + Name;
		}
	}
}
