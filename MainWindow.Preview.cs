using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TuringMonitor.Theme;
using Drawing = System.Drawing;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace TuringMonitor;

public partial class MainWindow
{
	private volatile bool _previewActive = true;
	private WriteableBitmap? _previewBitmap;
	private byte[]? _previewBuffer;
	private bool _previewFrozen;

	private void ShowThemePreview()
	{
		var theme = SelectedThemeName();
		var path = string.IsNullOrEmpty(theme) ? null : ResourceLocator.ThemePreview(theme);

		if (path is not null)
		{
			imgPreview.Source = LoadImageFile(path);
			previewHint.Visibility = Visibility.Collapsed;
			_previewFrozen = _service.IsRunning;
		}
		else
		{
			if (!_service.IsRunning)
			{
				imgPreview.Source = null;
				previewHint.Visibility = Visibility.Visible;
			}

			_previewFrozen = false;
		}
	}

	private void OnFrameRendered(Drawing.Bitmap bitmap)
	{
		if (!_previewActive || _previewFrozen)
			return;

		var width = bitmap.Width;
		var height = bitmap.Height;
		BitmapData data = bitmap.LockBits(new Drawing.Rectangle(0, 0, width, height),
			ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		var stride = data.Stride;
		var size = stride * height;

		var buffer = _previewBuffer?.Length == size ? _previewBuffer : new byte[size];
		try
		{
			Marshal.Copy(data.Scan0, buffer, 0, size);
		}
		finally
		{
			bitmap.UnlockBits(data);
		}

		_previewBuffer = buffer;
		Dispatcher.BeginInvoke(() => DrawPreview(buffer, width, height, stride));
	}

	private void DrawPreview(byte[] buffer, int width, int height, int stride)
	{
		if (_previewBitmap is null || _previewBitmap.PixelWidth != width || _previewBitmap.PixelHeight != height)
			_previewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

		if (!ReferenceEquals(imgPreview.Source, _previewBitmap))
			imgPreview.Source = _previewBitmap;

		_previewBitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride, 0);
		previewHint.Visibility = Visibility.Collapsed;
	}

	private static BitmapImage LoadImageFile(string path)
	{
		var image = new BitmapImage();
		image.BeginInit();
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.UriSource = new Uri(path);
		image.EndInit();
		image.Freeze();
		return image;
	}
}
