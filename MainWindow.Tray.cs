using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TuringMonitor.Platform;

namespace TuringMonitor;

public partial class MainWindow
{
	private TrayIcon? _tray;
	private ContextMenu? _trayMenu;
	private bool _trayReady;

	private void RestoreFromTray()
	{
		Show();
		ShowInTaskbar = true;
		WindowState = WindowState.Normal;
		Activate();
	}

	private void HideToTray()
	{
		ShowInTaskbar = false;
		Hide();
	}

	private void ShowTrayMenu()
	{
		_trayMenu ??= BuildTrayMenu();
		SetForegroundWindow(_hwnd);
		_trayMenu.IsOpen = true;
	}

	private ContextMenu BuildTrayMenu()
	{
		var menu = new ContextMenu
		{
			Placement = PlacementMode.MousePoint,
			Style = (Style)FindResource("TrayMenu")
		};

		var open = new MenuItem { Header = "Open" };
		open.Click += (_, _) => RestoreFromTray();

		var exit = new MenuItem { Header = "Exit" };
		exit.Click += (_, _) => ExitApp();

		menu.Items.Add(open);
		menu.Items.Add(exit);
		return menu;
	}

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);
}
