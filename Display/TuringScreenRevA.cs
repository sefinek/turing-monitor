using System.Drawing;
using System.IO.Ports;

namespace TuringMonitor.Display;

public sealed class TuringScreenRevA : IDisposable
{
	private readonly object _ioLock = new();
	private SerialPort? _serial;

	public TuringScreenRevA(string comPort = "AUTO", int displayWidth = 320, int displayHeight = 480)
	{
		ComPort = comPort;
		DisplayWidth = displayWidth;
		DisplayHeight = displayHeight;
	}

	public string ComPort { get; private set; }
	public int DisplayWidth { get; private set; }
	public int DisplayHeight { get; private set; }
	public string Model { get; private set; } = "unknown";
	public Orientation Orientation { get; private set; } = Orientation.Portrait;

	public int Width => Orientation is Orientation.Portrait or Orientation.ReversePortrait ? DisplayWidth : DisplayHeight;
	public int Height => Orientation is Orientation.Portrait or Orientation.ReversePortrait ? DisplayHeight : DisplayWidth;

	public void Dispose()
	{
		Close();
	}

	public void Open()
	{
		var port = ComPort;
		if (string.Equals(port, "AUTO", StringComparison.OrdinalIgnoreCase))
			port = SerialPortLocator.AutoDetect()
			       ?? throw new InvalidOperationException(
				       "Display not found automatically. Select a COM port manually in the settings.");

		var serial = new SerialPort(port, 115200)
		{
			Handshake = Handshake.RequestToSend,
			ReadTimeout = 1000,
			WriteTimeout = 5000
		};
		serial.Open();

		lock (_ioLock)
		{
			_serial = serial;
			ComPort = port;
		}
	}

	private void Close()
	{
		lock (_ioLock)
		{
			try
			{
				_serial?.Close();
			}
			catch
			{
			}

			_serial?.Dispose();
			_serial = null;
		}
	}

	private void WriteData(byte[] data)
	{
		lock (_ioLock)
		{
			if (_serial is not { IsOpen: true })
				throw new InvalidOperationException("Serial port is not open.");
			_serial.Write(data, 0, data.Length);
		}
	}

	private static byte[] BuildRectCommand(Command cmd, int x, int y, int ex, int ey)
	{
		var b = new byte[6];
		b[0] = (byte)(x >> 2);
		b[1] = (byte)(((x & 3) << 6) + (y >> 4));
		b[2] = (byte)(((y & 15) << 4) + (ex >> 6));
		b[3] = (byte)(((ex & 63) << 2) + (ey >> 8));
		b[4] = (byte)(ey & 255);
		b[5] = (byte)cmd;
		return b;
	}

	private void SendCommand(Command cmd, int x, int y, int ex, int ey)
	{
		WriteData(BuildRectCommand(cmd, x, y, ex, ey));
	}

	public void InitializeComm()
	{
		var hello = new byte[6];
		for (var i = 0; i < hello.Length; i++) hello[i] = (byte)Command.Hello;

		byte[] response;
		lock (_ioLock)
		{
			if (_serial is not { IsOpen: true })
				throw new InvalidOperationException("Serial port is not open.");
			_serial.Write(hello, 0, hello.Length);
			response = ReadExactly(_serial, 6);
			_serial.DiscardInBuffer();
		}

		if (IsAll(response, 0x01))
		{
			DisplayWidth = 320;
			DisplayHeight = 480;
			Model = "3.5\" (native panel 320x480)";
		}
		else if (IsAll(response, 0x02))
		{
			DisplayWidth = 480;
			DisplayHeight = 800;
			Model = "5\" (native panel 480x800)";
		}
		else if (IsAll(response, 0x03))
		{
			DisplayWidth = 600;
			DisplayHeight = 1024;
			Model = "8.8\" (native panel 600x1024)";
		}
		else if (response.Length == 0)
		{
			Model = $"no reply to identification handshake, using configured native panel {DisplayWidth}x{DisplayHeight}";
		}
		else
		{
			Model = $"unrecognized handshake response 0x{Convert.ToHexString(response)}, using configured native panel {DisplayWidth}x{DisplayHeight}";
		}
	}

	public void Reset()
	{
		SendCommand(Command.Reset, 0, 0, 0, 0);
		Close();
		Thread.Sleep(5000);
		Open();
	}

	public void Clear()
	{
		Orientation previous = Orientation;
		SetOrientation(Orientation.Portrait);
		SendCommand(Command.Clear, 0, 0, 0, 0);
		SetOrientation(previous);
	}

	public void ScreenOff()
	{
		SendCommand(Command.ScreenOff, 0, 0, 0, 0);
	}

	public void ScreenOn()
	{
		SendCommand(Command.ScreenOn, 0, 0, 0, 0);
	}

	public void SetBrightness(int level)
	{
		level = Math.Clamp(level, 0, 100);
		var absolute = (int)(255 - level / 100.0 * 255);
		SendCommand(Command.SetBrightness, absolute, 0, 0, 0);
	}

	public void SetOrientation(Orientation orientation)
	{
		Orientation = orientation;
		var width = Width;
		var height = Height;

		var b = new byte[16];
		b[5] = (byte)Command.SetOrientation;
		b[6] = (byte)((int)orientation + 100);
		b[7] = (byte)(width >> 8);
		b[8] = (byte)(width & 255);
		b[9] = (byte)(height >> 8);
		b[10] = (byte)(height & 255);
		WriteData(b);
	}

	public void DisplayBitmap(Bitmap image, int x = 0, int y = 0)
	{
		var screenW = Width;
		var screenH = Height;

		var imgW = image.Width;
		var imgH = image.Height;
		if (x + imgW > screenW) imgW = screenW - x;
		if (y + imgH > screenH) imgH = screenH - y;
		if (imgW <= 0 || imgH <= 0) return;

		Bitmap toSend = image;
		var ownsCrop = false;
		if (imgW != image.Width || imgH != image.Height)
		{
			toSend = image.Clone(new Rectangle(0, 0, imgW, imgH), image.PixelFormat);
			ownsCrop = true;
		}

		try
		{
			var rgb565 = ImageSerializer.ToRgb565LittleEndian(toSend);
			DisplayRegionRaw(x, y, imgW, imgH, rgb565, 0, rgb565.Length);
		}
		finally
		{
			if (ownsCrop) toSend.Dispose();
		}
	}

	public void DisplayRegionRaw(int x, int y, int w, int h, byte[] data, int offset, int length)
	{
		var command = BuildRectCommand(Command.DisplayBitmap, x, y, x + w - 1, y + h - 1);
		var chunkSize = Width * 8;
		var end = offset + length;

		lock (_ioLock)
		{
			if (_serial is not { IsOpen: true })
				throw new InvalidOperationException("Serial port is not open.");

			_serial.Write(command, 0, command.Length);

			for (var pos = offset; pos < end; pos += chunkSize)
			{
				var len = Math.Min(chunkSize, end - pos);
				_serial.Write(data, pos, len);
			}
		}
	}

	private static bool IsAll(byte[] data, byte value)
	{
		if (data.Length != 6) return false;
		foreach (var b in data)
			if (b != value)
				return false;
		return true;
	}

	private static byte[] ReadExactly(SerialPort serial, int count)
	{
		var buffer = new byte[count];
		var read = 0;
		try
		{
			while (read < count)
			{
				var n = serial.Read(buffer, read, count - read);
				if (n <= 0) break;
				read += n;
			}
		}
		catch (TimeoutException)
		{
		}

		return read == count ? buffer : Array.Empty<byte>();
	}
}
