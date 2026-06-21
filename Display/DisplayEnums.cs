namespace TuringMonitor.Display;

public enum Orientation
{
	Portrait = 0,
	ReversePortrait = 1,
	Landscape = 2,
	ReverseLandscape = 3
}

internal enum Command : byte
{
	Reset = 101,
	Clear = 102,
	ToBlack = 103,
	ScreenOff = 108,
	ScreenOn = 109,
	SetBrightness = 110,
	SetOrientation = 121,
	DisplayBitmap = 197,
	Hello = 69
}
