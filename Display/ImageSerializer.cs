using System.Drawing;
using System.Drawing.Imaging;

namespace TuringMonitor.Display;

internal static class ImageSerializer
{
	public static byte[] ToRgb565LittleEndian(Bitmap image)
	{
		var output = new byte[image.Width * image.Height * 2];
		WriteRgb565LittleEndian(image, output);
		return output;
	}

	public static void WriteRgb565LittleEndian(Bitmap image, byte[] output)
	{
		var width = image.Width;
		var height = image.Height;
		if (output.Length < width * height * 2)
			throw new ArgumentException("Destination buffer is too small.", nameof(output));

		var rect = new Rectangle(0, 0, width, height);
		BitmapData data = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			var stride = data.Stride;
			unsafe
			{
				var basePtr = (byte*)data.Scan0;
				var o = 0;
				for (var y = 0; y < height; y++)
				{
					var row = basePtr + y * stride;
					for (var x = 0; x < width; x++)
					{
						var b = row[x * 4 + 0];
						var g = row[x * 4 + 1];
						var r = row[x * 4 + 2];

						var rgb565 = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
						output[o++] = (byte)(rgb565 & 0xFF);
						output[o++] = (byte)((rgb565 >> 8) & 0xFF);
					}
				}
			}
		}
		finally
		{
			image.UnlockBits(data);
		}
	}
}
