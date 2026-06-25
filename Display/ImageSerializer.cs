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
				fixed (byte* outBase = output)
				{
					var op = outBase;
					for (var y = 0; y < height; y++)
					{
						var row = basePtr + y * stride;
						for (var x = 0; x < width; x++)
						{
							var b = row[x * 4 + 0];
							var g = row[x * 4 + 1];
							var r = row[x * 4 + 2];

							var rgb565 = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
							*op++ = (byte)(rgb565 & 0xFF);
							*op++ = (byte)(rgb565 >> 8);
						}
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
