using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Resources;
using Size = System.Drawing.Size;

namespace TuringMonitor.Monitor;

public sealed class TrayIcon : IDisposable
{
	private const int WmTrayCallback = 0x0400 + 1;
	private const int NimAdd = 0;
	private const int NimDelete = 2;
	private const int NifMessage = 0x01;
	private const int NifIcon = 0x02;
	private const int NifTip = 0x04;
	private const int WmLButtonUp = 0x0202;
	private const int WmLButtonDblClk = 0x0203;
	private const int WmRButtonUp = 0x0205;

	private readonly IntPtr _hwnd;
	private readonly HwndSource? _source;
	private bool _added;
	private IntPtr _hIcon;

	public TrayIcon(IntPtr hwnd, string tooltip)
	{
		_hwnd = hwnd;
		_hIcon = CreateIconHandle();

		NotifyIconData data = NewData();
		data.uFlags = NifMessage | NifIcon | NifTip;
		data.uCallbackMessage = WmTrayCallback;
		data.hIcon = _hIcon;
		data.szTip = tooltip;
		_added = Shell_NotifyIcon(NimAdd, ref data);

		_source = HwndSource.FromHwnd(hwnd);
		_source?.AddHook(WndProc);
	}

	public void Dispose()
	{
		if (_added)
		{
			NotifyIconData data = NewData();
			Shell_NotifyIcon(NimDelete, ref data);
			_added = false;
		}

		_source?.RemoveHook(WndProc);

		if (_hIcon != IntPtr.Zero)
		{
			DestroyIcon(_hIcon);
			_hIcon = IntPtr.Zero;
		}
	}

	public event Action? LeftClick;
	public event Action? RightClick;

	private NotifyIconData NewData()
	{
		return new NotifyIconData
		{
			cbSize = Marshal.SizeOf<NotifyIconData>(),
			hWnd = _hwnd,
			uID = 1,
			szTip = string.Empty,
			szInfo = string.Empty,
			szInfoTitle = string.Empty
		};
	}

	private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg != WmTrayCallback)
			return IntPtr.Zero;

		switch (lParam.ToInt32() & 0xFFFF)
		{
			case WmLButtonUp:
			case WmLButtonDblClk:
				LeftClick?.Invoke();
				handled = true;
				break;
			case WmRButtonUp:
				RightClick?.Invoke();
				handled = true;
				break;
		}

		return IntPtr.Zero;
	}

	private static IntPtr CreateIconHandle()
	{
		try
		{
			StreamResourceInfo? info = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
			if (info?.Stream is { } stream)
				using (stream)
				using (var icon = new Icon(stream, new Size(32, 32)))
				{
					return CopyIcon(icon.Handle);
				}
		}
		catch
		{
		}

		return DrawFallbackIcon();
	}

	private static IntPtr DrawFallbackIcon()
	{
		using var bitmap = new Bitmap(32, 32);
		using (Graphics g = Graphics.FromImage(bitmap))
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;

			var rect = new Rectangle(2, 2, 27, 27);
			const int d = 12;
			using var path = new GraphicsPath();
			path.AddArc(rect.X, rect.Y, d, d, 180, 90);
			path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
			path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
			path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
			path.CloseFigure();

			using var brush = new LinearGradientBrush(rect,
				Color.FromArgb(80, 140, 255), Color.FromArgb(150, 110, 255), 45f);
			g.FillPath(brush, path);
		}

		return bitmap.GetHicon();
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern bool Shell_NotifyIcon(int dwMessage, ref NotifyIconData data);

	[DllImport("user32.dll")]
	private static extern bool DestroyIcon(IntPtr hIcon);

	[DllImport("user32.dll")]
	private static extern IntPtr CopyIcon(IntPtr hIcon);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct NotifyIconData
	{
		public int cbSize;
		public IntPtr hWnd;
		public int uID;
		public int uFlags;
		public int uCallbackMessage;
		public IntPtr hIcon;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string szTip;
		public int dwState;
		public int dwStateMask;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string szInfo;
		public int uVersion;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string szInfoTitle;
		public int dwInfoFlags;
		public Guid guidItem;
		public IntPtr hBalloonIcon;
	}
}
