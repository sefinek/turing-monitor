using NLog;

namespace TuringMonitor.Logging;

public static class AppLog
{
	private static readonly Logger Logger = LogManager.GetLogger("App");

	public static void Debug(string message, bool toUi = false)
	{
		Write(LogLevel.Debug, message, toUi, null);
	}

	public static void Info(string message, bool toUi = true)
	{
		Write(LogLevel.Info, message, toUi, null);
	}

	public static void Warn(string message, bool toUi = true)
	{
		Write(LogLevel.Warn, message, toUi, null);
	}

	public static void Error(string message, bool toUi = true)
	{
		Write(LogLevel.Error, message, toUi, null);
	}

	public static void Error(Exception exception, string message, bool toUi = true)
	{
		Write(LogLevel.Error, message, toUi, exception);
	}

	private static void Write(LogLevel level, string message, bool toUi, Exception? exception)
	{
		if (!Logger.IsEnabled(level))
			return;

		var logEvent = new LogEventInfo(level, Logger.Name, message)
		{
			Exception = exception,
			Properties =
			{
				["ui"] = toUi
			}
		};
		Logger.Log(logEvent);
	}
}
