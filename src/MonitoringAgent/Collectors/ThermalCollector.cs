using System.IO;
using MonitoringAgent.Models;

namespace MonitoringAgent.Collectors;

public static class ThermalCollector
{
    public static List<ThermalSensor> Collect(ThermalMonitoringOptions options)
    {
        var sensors = new List<ThermalSensor>();

        if (!OperatingSystem.IsLinux() || !options.Enable)
            return sensors;

        string hwmonPath = options.HwmonPath;
        string thermalZonePath = options.ThermalZonePath;

        // 1. Intentar leer desde hwmon (hardware físico dedicado)
        if (Directory.Exists(hwmonPath))
        {
            try
            {
                var hwmonDirs = Directory.GetDirectories(hwmonPath);
                foreach (var dir in hwmonDirs)
                {
                    string deviceName = ReadFileSafe(Path.Combine(dir, "name")) ?? Path.GetFileName(dir);

                    var inputFiles = Directory.GetFiles(dir, "temp*_input");
                    foreach (var inputFile in inputFiles)
                    {
                        string basePrefix = inputFile.Substring(0, inputFile.Length - "_input".Length);
                        string? rawValue = ReadFileSafe(inputFile);

                        if (long.TryParse(rawValue, out long milliCelsius))
                        {
                            double tempCelsius = Math.Round(milliCelsius / 1000.0, 1);

                            string label = ReadFileSafe($"{basePrefix}_label") 
                                           ?? Path.GetFileName(basePrefix);

                            double? critTemp = null;
                            string? rawCrit = ReadFileSafe($"{basePrefix}_crit");
                            if (long.TryParse(rawCrit, out long critMilli))
                            {
                                critTemp = Math.Round(critMilli / 1000.0, 1);
                            }

                            sensors.Add(new ThermalSensor(
                                DeviceName: deviceName,
                                SensorLabel: label,
                                TemperatureCelsius: tempCelsius,
                                CriticalThresholdCelsius: critTemp
                            ));
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Fallback a thermal_zone si hwmon no arrojó sensores
        if (sensors.Count == 0 && Directory.Exists(thermalZonePath))
        {
            try
            {
                var zoneDirs = Directory.GetDirectories(thermalZonePath, "thermal_zone*");
                foreach (var dir in zoneDirs)
                {
                    string? rawTemp = ReadFileSafe(Path.Combine(dir, "temp"));
                    string zoneType = ReadFileSafe(Path.Combine(dir, "type")) ?? Path.GetFileName(dir);

                    if (long.TryParse(rawTemp, out long milliCelsius))
                    {
                        sensors.Add(new ThermalSensor(
                            DeviceName: "thermal_zone",
                            SensorLabel: zoneType,
                            TemperatureCelsius: Math.Round(milliCelsius / 1000.0, 1),
                            CriticalThresholdCelsius: null
                        ));
                    }
                }
            }
            catch { }
        }

        return sensors;
    }

    private static string? ReadFileSafe(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Trim();
            }
        }
        catch { }

        return null;
    }
}