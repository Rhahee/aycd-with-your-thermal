using System.Drawing;
using System.IO.Ports;

namespace PrintToDo;

internal static class EscPosPrinter
{
    public static void Print(Bitmap bitmap, PrinterSettings settings)
    {
        AppStore.ValidatePrinter(settings);
        if (string.IsNullOrWhiteSpace(settings.PortName))
            throw new IOException("Pilih port COM milik printer pada halaman Printer sebelum mencetak.");
        if (!SerialPort.GetPortNames().Contains(settings.PortName, StringComparer.OrdinalIgnoreCase))
            throw new IOException($"Port {settings.PortName} tidak ditemukan. Periksa sambungan Bluetooth Windows.");
        using var port = new SerialPort(settings.PortName, settings.BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 3000,
            WriteTimeout = 5000
        };
        try
        {
            port.Open();
            WriteReceipt(port, bitmap);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"Port {settings.PortName} sedang digunakan oleh aplikasi lain.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new IOException("Pengiriman ke printer terlalu lama. Periksa daya dan jarak Bluetooth.", ex);
        }
    }

    private static void WriteReceipt(SerialPort port, Bitmap bitmap)
    {
        port.Write(new byte[] { 0x1B, 0x40 }, 0, 2); // ESC @: initialize

        var bytesPerRow = (bitmap.Width + 7) / 8;
        const int maxRows = 24;
        for (var top = 0; top < bitmap.Height; top += maxRows)
        {
            var rows = Math.Min(maxRows, bitmap.Height - top);
            var command = new byte[8 + bytesPerRow * rows];
            command[0] = 0x1D;
            command[1] = 0x76;
            command[2] = 0x30;
            command[3] = 0;
            command[4] = (byte)(bytesPerRow & 0xFF);
            command[5] = (byte)(bytesPerRow >> 8);
            command[6] = (byte)(rows & 0xFF);
            command[7] = (byte)(rows >> 8);
            for (var row = 0; row < rows; row++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, top + row);
                    var luminance = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                    if (luminance < 180)
                        command[8 + row * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }
            port.Write(command, 0, command.Length);
            Thread.Sleep(80); // Give small Bluetooth printer buffers time to drain.
        }
        port.Write(new byte[] { 0x0A, 0x0A, 0x0A }, 0, 3);
    }
}
