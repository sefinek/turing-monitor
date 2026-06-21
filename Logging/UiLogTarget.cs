using NLog;
using NLog.Targets;

namespace TuringMonitor.Logging;

[Target("Ui")]
public sealed class UiLogTarget : TargetWithLayout
{
	public static event Action<string>? Logged;

	protected override void Write(LogEventInfo logEvent)
	{
		Logged?.Invoke(RenderLogEvent(Layout, logEvent));
	}
}
