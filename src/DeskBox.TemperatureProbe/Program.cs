using System.Globalization;
using LibreHardwareMonitor.Hardware;

// The monitor owns this process via redirected anonymous stdout. No listener,
// port, global pipe, hardware identity, or persistent telemetry is created.
try
{
    var computer = new Computer
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsStorageEnabled = true
    };
    computer.Open();
    while (true)
    {
        double? cpu = null, gpu = null, disk = null;
        try
        {
            foreach (var hardware in computer.Hardware)
            {
                hardware.Update();
                foreach (var subhardware in hardware.SubHardware) subhardware.Update();
                var temperatures = hardware.Sensors.Concat(hardware.SubHardware.SelectMany(part => part.Sensors))
                    .Where(sensor => sensor.SensorType == SensorType.Temperature &&
                        sensor.Value is > 0 and < 130)
                    .ToArray();
                double? representative = temperatures
                    .OrderByDescending(sensor => sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(sensor => sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    .Select(sensor => (double?)sensor.Value)
                    .FirstOrDefault();
                if (!representative.HasValue) continue;
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu: cpu ??= representative; break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel: gpu ??= representative; break;
                    case HardwareType.Storage: disk ??= representative; break;
                }
            }
        }
        catch { /* A transient sensor error only blanks this sample. */ }
        Console.WriteLine(string.Join('|', Format(cpu), Format(gpu), Format(disk)));
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}
catch
{
    // With no hardware access the parent shows unavailable, never crashes.
}

static string Format(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
