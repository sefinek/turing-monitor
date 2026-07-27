using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace TuringMonitor;

public partial class MainWindow
{
	private static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

	private void InitializeVersion()
	{
		lblVersion.Text = $"v{AppVersion}";
	}

	private void lblVersion_Click(object sender, MouseButtonEventArgs e)
	{
		MessageBox.Show(this,
			$"Turing Monitor\nVersion {AppVersion}\n\n© 2026 Sefinek",
			"About", MessageBoxButton.OK, MessageBoxImage.Information);
	}
}
