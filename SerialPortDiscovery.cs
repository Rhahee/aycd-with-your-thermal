using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace PrintToDo;

internal static class SerialPortDiscovery
{
    internal sealed record PortInfo(string Name, string Label);

    public static PortInfo[] List()
    {
        var ports = SerialPort.GetPortNames()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var addresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, PNPDeviceID FROM Win32_SerialPort");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var port = item["DeviceID"]?.ToString();
                    var pnp = item["PNPDeviceID"]?.ToString() ?? "";
                    if (port is null) continue;
                    var match = Regex.Match(pnp, @"&0&([0-9A-F]{12})_", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var hex = match.Groups[1].Value.ToUpperInvariant();
                        addresses[port] = string.Join(":",
                            Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
                    }
                }
            }
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        return ports.Select(port => new PortInfo(port,
            addresses.TryGetValue(port, out var address)
                ? $"{port} · Bluetooth {address}" : port)).ToArray();
    }
}
