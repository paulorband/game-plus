using System.Runtime.InteropServices;

namespace GamePlus.Services.Monitoring;

/// <summary>
/// Memory speed from the SMBIOS tables (Afterburner has no RAM frequency sensor). Reading the firmware table needs no
/// administrator rights and no WMI, so the app stays free of extra dependencies.
/// </summary>
public static partial class RamInfoProvider
{
    private const uint RsmbSignature = 0x52534D42; // 'RSMB'
    private static int? _cached;
    private static bool _read;

    [LibraryImport("kernel32.dll")]
    private static partial uint GetSystemFirmwareTable(uint provider, uint table, [Out] byte[]? buffer, uint size);

    /// <summary>Configured memory speed in MT/s (e.g. 6000 for DDR5-6000), or null when unknown. Cached: it doesn't change while running.</summary>
    public static int? ConfiguredSpeed
    {
        get
        {
            if (_read) return _cached;
            _read = true;
            try { _cached = Read(); }
            catch (Exception) { _cached = null; }
            return _cached;
        }
    }

    private static int? Read()
    {
        var size = GetSystemFirmwareTable(RsmbSignature, 0, null, 0);
        if (size < 8) return null;
        var data = new byte[size];
        if (GetSystemFirmwareTable(RsmbSignature, 0, data, size) == 0) return null;

        // RawSMBIOSData: 8-byte header (calling method, major, minor, revision, length) then the structure table.
        var end = (int)Math.Min(size, 8 + BitConverter.ToUInt32(data, 4));
        var pos = 8;
        int best = 0;
        while (pos + 4 <= end)
        {
            var type = data[pos];
            var length = data[pos + 1];
            if (length < 4) break;

            // Type 17 = memory device. Size at 0x0C (0 = empty slot), speed at 0x15, configured speed at 0x20.
            if (type == 17 && length >= 0x22 && BitConverter.ToUInt16(data, pos + 0x0C) != 0)
            {
                int speed = BitConverter.ToUInt16(data, pos + 0x20);
                if (speed == 0) speed = BitConverter.ToUInt16(data, pos + 0x15);
                best = Math.Max(best, speed);
            }

            // Skip the formatted area, then the string set that ends with a double NUL.
            pos += length;
            while (pos + 1 < end && !(data[pos] == 0 && data[pos + 1] == 0)) pos++;
            pos += 2;
        }
        return best > 0 ? best : null;
    }
}
