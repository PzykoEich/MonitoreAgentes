using System.IO;
using System.Runtime.InteropServices;
using MonitoringAgent.Models;

namespace MonitoringAgent.Collectors;

public static class DiskCollector
{
    // Estructura POSIX statvfs para Linux (64 bits)
    [StructLayout(LayoutKind.Sequential)]
    private struct Statvfs
    {
        public ulong f_bsize;    // Tamaño del bloque del sistema de archivos
        public ulong f_frsize;   // Tamaño del fragmento fundamental
        public ulong f_blocks;   // Total de bloques
        public ulong f_bfree;    // Bloques libres
        public ulong f_bavail;   // Bloques libres para no-root
        public ulong f_files;    // Total de inodos
        public ulong f_ffree;    // Inodos libres
        public ulong f_favail;   // Inodos libres para no-root
        public ulong f_fsid;     // ID del sistema de archivos
        public ulong f_flag;     // Flags de montaje
        public ulong f_namemax;  // Longitud máxima de nombre de archivo
    }

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int statvfs(string path, out Statvfs buf);

    public static List<DiskMetrics> Collect()
    {
        var disks = new List<DiskMetrics>();

        try
        {
            DriveInfo[] drives = DriveInfo.GetDrives();

            foreach (var drive in drives)
            {
                try
                {
                    if (!drive.IsReady)
                        continue;

                    string rootPath = drive.RootDirectory.FullName;

                    // Formato del filesystem (ext4, xfs, etc.)
                    string rawFormat = string.Empty;
                    try { rawFormat = drive.DriveFormat; } catch { }

                    string formatLower = rawFormat.ToLowerInvariant();

                    // Omitir sistemas de archivos virtuales o temporales
                    if (formatLower.Contains("tmpfs") ||
                        formatLower.Contains("overlay") ||
                        formatLower.Contains("squashfs") ||
                        formatLower.Contains("devtmpfs") ||
                        rootPath.StartsWith("/proc") ||
                        rootPath.StartsWith("/sys") ||
                        rootPath.StartsWith("/dev"))
                    {
                        continue;
                    }

                    long totalBytes = drive.TotalSize;
                    if (totalBytes <= 0)
                        continue;

                    long freeBytes = drive.AvailableFreeSpace;
                    long usedBytes = totalBytes - freeBytes;

                    double totalGb = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 2);
                    double freeGb = Math.Round(freeBytes / (1024.0 * 1024.0 * 1024.0), 2);
                    double spaceUsedPct = Math.Round(((double)usedBytes / totalBytes) * 100.0, 2);

                    // Cálculo de inodos vía statvfs
                    double inodesUsedPct = 0;
                    long totalInodes = 0;
                    long freeInodes = 0;

                    if (OperatingSystem.IsLinux())
                    {
                        try
                        {
                            if (statvfs(rootPath, out Statvfs stat) == 0 && stat.f_files > 0)
                            {
                                totalInodes = (long)stat.f_files;
                                freeInodes = (long)stat.f_ffree;
                                long usedInodes = totalInodes - freeInodes;
                                inodesUsedPct = Math.Round(((double)usedInodes / totalInodes) * 100.0, 2);
                            }
                        }
                        catch
                        {
                            // Si falla statvfs por permisos en montajes puntuales, continúa sin abortar
                        }
                    }

                    // Firma exacta: (MountPoint, DriveFormat, TotalGigabytes, FreeGigabytes, SpaceUsedPercent, TotalInodes, FreeInodes, InodesUsedPercent)
                    disks.Add(new DiskMetrics(
                        rootPath,
                        string.IsNullOrWhiteSpace(rawFormat) ? "unknown" : rawFormat,
                        totalGb,
                        freeGb,
                        spaceUsedPct,
                        totalInodes,
                        freeInodes,
                        inodesUsedPct
                    ));
                }
                catch
                {
                    // Saltar montajes individuales inaccesibles
                    continue;
                }
            }
        }
        catch
        {
            // Protección contra excepciones no controladas
        }

        return disks;
    }
}