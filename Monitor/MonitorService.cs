using System.Drawing;
using System.IO;
using TuringMonitor.Configuration;
using TuringMonitor.Display;
using TuringMonitor.Logging;
using TuringMonitor.Rendering;
using TuringMonitor.Sensors;
using TuringMonitor.Theme;

namespace TuringMonitor.Monitor;

public sealed class MonitorService : IDisposable
{
	private const int BandHeight = 16;
	private const int ReconnectDelayMs = 5000;
	private readonly object _gate = new();

	private readonly DashboardRenderer _renderer = new();
	private CancellationTokenSource? _cts;
	private Task? _loop;

	private TuringScreenRevA? _screen;

	public bool IsRunning { get; private set; }

	public void Dispose()
	{
		Stop();
		_cts?.Dispose();
		_renderer.Dispose();
	}

	public event Action<bool>? RunningChanged;
	public event Action<Bitmap>? FrameRendered;
	public event Action<SystemStats>? StatsUpdated;

	public void Start(MonitorSettings settings)
	{
		lock (_gate)
		{
			if (IsRunning)
				return;

			_cts?.Dispose();
			_cts = new CancellationTokenSource();
			CancellationToken token = _cts.Token;
			IsRunning = true;
			_loop = Task.Run(() => RunLoop(settings, token), token);
		}

		AppLog.Info($"Starting monitor (port={settings.ComPort}, orientation={settings.Orientation}, brightness={settings.Brightness}%)");
	}

	public void Stop()
	{
		CancellationTokenSource? cts;
		Task? loop;
		lock (_gate)
		{
			if (!IsRunning)
				return;
			cts = _cts;
			loop = _loop;
		}

		cts?.Cancel();
		try
		{
			loop?.Wait(6000);
		}
		catch
		{
		}
	}

	public void SetBrightness(int level)
	{
		lock (_gate)
		{
			try
			{
				_screen?.SetBrightness(level);
			}
			catch
			{
			}
		}
	}

	private void RunLoop(MonitorSettings settings, CancellationToken token)
	{
		try
		{
			while (true)
			{
				RunSession(settings, token);

				if (token.IsCancellationRequested || !settings.AutoReconnect)
					break;

				RunningChanged?.Invoke(false);
				AppLog.Info($"Reconnecting in {ReconnectDelayMs / 1000} seconds...");
				if (token.WaitHandle.WaitOne(ReconnectDelayMs))
					break;
			}
		}
		finally
		{
			lock (_gate)
			{
				IsRunning = false;
			}

			AppLog.Info("Monitor stopped");
			RunningChanged?.Invoke(false);
		}
	}

	private void RunSession(MonitorSettings settings, CancellationToken token)
	{
		TuringScreenRevA? screen = null;
		SensorHub? sensors = null;
		try
		{
			sensors = new SensorHub
			{
				NetInterfaceId = settings.NetInterface,
				DiskRoot = settings.DiskDrive
			};

			screen = new TuringScreenRevA(settings.ComPort);
			screen.Open();
			AppLog.Info($"Connected to display on {screen.ComPort}");

			if (settings.ResetOnStartup)
			{
				AppLog.Info("Resetting display...");
				screen.Reset();
			}

			AppLog.Info("Initializing communication...");
			screen.InitializeComm();
			AppLog.Info($"Setting brightness to {settings.Brightness}%");
			screen.SetBrightness(settings.Brightness);
			screen.ScreenOn();

			lock (_gate)
			{
				_screen = screen;
			}

			RunningChanged?.Invoke(true);

			if (string.IsNullOrEmpty(settings.ThemeName))
				RunDashboardMode(screen, sensors, settings, token);
			else
				RunThemeMode(screen, sensors, settings, token);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex) when (IsConnectionError(ex))
		{
			AppLog.Warn(ex.Message);
		}
		catch (Exception ex)
		{
			AppLog.Error(ex, "Monitor session failed");
		}
		finally
		{
			lock (_gate)
			{
				_screen = null;
			}

			try
			{
				screen?.ScreenOff();
			}
			catch
			{
			}

			screen?.Dispose();
			sensors?.Dispose();
		}
	}

	private static bool IsConnectionError(Exception ex)
	{
		return ex is IOException or InvalidOperationException or UnauthorizedAccessException or TimeoutException;
	}

	private void RunDashboardMode(TuringScreenRevA screen, SensorHub sensors, MonitorSettings settings, CancellationToken token)
	{
		_renderer.TimeFormat = settings.TimeFormat;
		_renderer.DateFormat = settings.DateFormat;
		_renderer.Culture = settings.DateCulture;
		_renderer.NetLinkMbps = settings.NetLinkMbps;
		_renderer.NetUnits = settings.NetUnits;

		screen.SetOrientation(settings.Orientation);

		var w = screen.Width;
		var h = screen.Height;
		AppLog.Info($"Working resolution: {w}x{h}");
		var linkLabel = settings.NetLinkMbps > 0 ? $"{settings.NetLinkMbps} Mb/s (manual)" : "auto-detect";
		AppLog.Info($"Dashboard mode | orientation={settings.Orientation} | interval={settings.IntervalMs} ms | net units={settings.NetUnits} | link={linkLabel}");

		var frameBytes = w * h * 2;
		var bufferA = new byte[frameBytes];
		var bufferB = new byte[frameBytes];
		byte[]? prev = null;
		var useA = true;
		long frameNo = 0;

		while (!token.IsCancellationRequested)
		{
			frameNo++;
			SystemStats stats = sensors.Read();
			StatsUpdated?.Invoke(stats);
			Bitmap frame = _renderer.Render(stats, w, h);

			var current = useA ? bufferA : bufferB;
			ImageSerializer.WriteRgb565LittleEndian(frame, current);

			FrameRendered?.Invoke(frame);
			PushDirtyBands(screen, w, h, current, prev, token);

#if DEBUG
			if (frameNo == 1 || frameNo % 5 == 0)
				AppLog.Info(StatsSummary(stats));
#endif

			prev = current;
			useA = !useA;

			if (token.WaitHandle.WaitOne(settings.IntervalMs))
				break;
		}
	}

#if DEBUG
	private static string StatsSummary(SystemStats s)
	{
		var gpu = s.GpuAvailable ? $" | GPU {s.GpuLoadPercent:0}% {s.GpuTempC:0}°C" : "";
		var cpuTemp = s.CpuTempAvailable ? $" {s.CpuTempC:0}°C" : "";
		var link = s.NetLinkMbps > 0 ? $" @ {s.NetLinkMbps:0} Mb/s" : "";
		return $"CPU {s.CpuLoadPercent:0}%{cpuTemp} {s.CpuClockMhz / 1000.0:0.00} GHz | RAM {s.RamUsedPercent:0}%"
		       + $" | DISK {s.DiskUsedPercent:0}% | NET ↓{s.NetDownKbps:0} ↑{s.NetUpKbps:0} KB/s{link}{gpu}";
	}
#endif

	private void RunThemeMode(TuringScreenRevA screen, SensorHub sensors, MonitorSettings settings, CancellationToken token)
	{
		AppLog.Info($"Loading theme '{settings.ThemeName}'...");

		ThemeConfig config;
		try
		{
			config = ThemeConfig.Load(settings.ThemeName);
		}
		catch (Exception ex)
		{
			AppLog.Error(ex, $"Failed to load theme '{settings.ThemeName}', falling back to dashboard");
			RunDashboardMode(screen, sensors, settings, token);
			return;
		}

		screen.SetOrientation(ThemeEngine.GetOrientation(config));

		var (w, h) = ThemeEngine.GetCanvasSize(config);

		if (w != screen.Width || h != screen.Height)
			AppLog.Warn($"Theme size {w}x{h} differs from screen {screen.Width}x{screen.Height} - it may be clipped, pick a theme that matches your display");

		using var painter = new ThemePainter(screen, w, h);
		var engine = new ThemeEngine(config, painter) { NetUnits = settings.NetUnits };
		engine.RenderStatic();
		AppLog.Info($"Theme '{settings.ThemeName}' loaded successfully");

		long frameNo = 0;
		while (!token.IsCancellationRequested)
		{
			frameNo++;
			SystemStats stats = sensors.Read();
			StatsUpdated?.Invoke(stats);
			engine.Update(stats);
			FrameRendered?.Invoke(painter.Canvas);

#if DEBUG
			if (frameNo == 1 || frameNo % 5 == 0)
				AppLog.Info(StatsSummary(stats));
#endif

			if (token.WaitHandle.WaitOne(settings.IntervalMs))
				break;
		}
	}

	private static void PushDirtyBands(TuringScreenRevA screen, int w, int h, byte[] frame, byte[]? prev, CancellationToken token)
	{
		var rowBytes = w * 2;
		var full = prev is null;

		for (var y = 0; y < h; y += BandHeight)
		{
			if (token.IsCancellationRequested) return;

			var bandH = Math.Min(BandHeight, h - y);
			var start = y * rowBytes;
			var len = bandH * rowBytes;

			if (!full && frame.AsSpan(start, len).SequenceEqual(prev.AsSpan(start, len)))
				continue;

			screen.DisplayRegionRaw(0, y, w, bandH, frame, start, len);
		}

	}
}
