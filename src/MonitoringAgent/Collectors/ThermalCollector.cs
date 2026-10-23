using System.Globalization;
using System.IO;
using System.Net;
using MonitoringAgent.Models;

namespace MonitoringAgent.Collectors;

public static class ThermalCollector
{
    private const string HwmonBasePath = "/sys/class/hwmon";
    private const string ThermalZoneBasePath = "/sys/class/thermal";

    public static List<ThermalSensor> Collect()
    {
        var sensors = new List<ThermalSensor>();

        if(!OperatingSystem.IsLinux) return sensors;

        //Leer /sys/class/hwmon => si es físico. 
        if (Directory.Exists(HwmonBasePath))
        {
            try
            {
                var hwmonDirs = Directory.GetDirectories(HwmonBasePath);

                foreach(var dir in hwmonDirs)
                {
                    string deviceName = ReadFileSafe(Path.Combine(dir, "name")) ?? Path.GetFileName(dir);

                    var inputFiles = Directory.GetFiles(dir,"temp*_input");
                    foreach(var inputFile in inputFiles)
                    {
                        string basePrefix = inputFile.Substring(0, inputFile.Length - "_input".Length);
                        string rawValue = ReadFileSafe(inputFile);

                        if(long.TryParse(rawValue, out long milliCelcius))
                        {
                            double tempCelcius = Math.Round(milliCelcius / 1000.0, 1);
                            //etiqueta del sensor
                            string label = ReadFileSafe($"{basePrefix}_label") ?? Path.GetFileName(basePrefix);

                            //Umbral crítico
                            double? critTemp = null;
                            string rawCrit = ReadFileSafe($"{basePrefix}_crit");
                            if(long.TryParse(rawCrit, out long critMilli))
                            {
                                critTemp = Math.Round(critMilli  / 1000.0, 1);
                            }
                            sensors.Add(new ThermalSensor(
                                DeviceName: deviceName,
                                SensorLabel: label,
                                TemperatureCelsius: tempCelcius,
                                CriticalThresholdCelsius: critTemp
                            ));
                        }
                    }
                }
            }catch{}
        }
        if(sensors.Count == 0 && Directory.Exists(ThermalZoneBasePath))
        {
            try
            {
                var zoneDirs = Directory.GetDirectories(ThermalZoneBasePath, "thermal_zone*");
                foreach(var dir in zoneDirs)
                {
                    string rawTemp = ReadFileSafe(Path.Combine(dir, "temp"));
                    string zoneType = ReadFileSafe(Path.Combine(dir, "type")) ?? Path.GetFileName(dir);

                    if(long.TryParse(rawTemp, out long milliCelsius))
                    {
                        sensors.Add( new ThermalSensor(
                            DeviceName: "thermal_zone",
                            SensorLable: zoneType,
                            TemperatureCelsius: Math.Round(milliCelsius / 1000.0, 1),
                            CriticalThresholdCelsius: null
                        ));
                    }
                }
            }catch{}
        }
        return sensors;
    }

    private static string? ReadFileSafe(string path)
    {
        try
        {
            if(File.Exists(path)) return File.ReadAllText(path).Trim();
        }catch{}
        return null;
    }
}