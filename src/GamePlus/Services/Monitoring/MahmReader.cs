using System.IO.MemoryMappedFiles;

namespace GamePlus.Services.Monitoring;

/// <summary>One reading of the MSI Afterburner sensors.</summary>
public sealed class MahmSnapshot
{
    /// <summary>Valid sensor values by Afterburner's own source name (e.g. "GPU2 temperature").</summary>
    public Dictionary<string, float> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>Device names by GPU index (index 0 is named "GPU1" in the sensor names).</summary>
    public List<string> Gpus { get; } = new();
}

/// <summary>
/// Reads Afterburner's hardware monitoring shared memory ("MAHMSharedMemory"). It opens read-only without
/// administrator rights, so the app never has to touch Afterburner's config files in Program Files.
/// </summary>
public sealed unsafe class MahmReader : IDisposable
{
    private const string MapName = "MAHMSharedMemory";
    private const uint Signature = 0x4D41484D; // 'MAHM' as Afterburner writes it
    private const int NameLength = 260;
    private const int ValueOffset = 1300;   // float
    private const int GpuNameOffset = 520;  // device name inside a GPU entry
    private const float Invalid = 3.0e38f;  // Afterburner reports FLT_MAX for sensors without data

    private const int GpuIndexOffset = 1316, SourceIdOffset = 1320;

    // Afterburner source ids of the per-GPU sensors.
    private static readonly Dictionary<uint, string> GpuSuffixById = new()
    {
        [0] = "temperature",
        [48] = "usage",
        [32] = "core clock",
        [49] = "memory usage",
        [97] = "power",
        [64] = "voltage",
    };

    private bool _dumped;
    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private byte* _base;

    /// <summary>Reads the sensors, or returns null when Afterburner is closed or the layout is unknown.</summary>
    public MahmSnapshot? TryRead()
    {
        try
        {
            if (_view is null && !Open()) return null;

            var header = (uint*)_base;
            if (header[0] != Signature)
            {
                LogOnce($"MAHM bad signature {header[0]:X8}");
                Close();
                return null;
            }

            uint headerSize = header[2], count = header[3], entrySize = header[4], gpuCount = header[6], gpuEntrySize = header[7];
            // A newer Afterburner could move fields around; treat anything smaller than we know as unreadable.
            if (entrySize < 1324 || count > 4096)
            {
                LogOnce($"MAHM unexpected layout: entrySize={entrySize} count={count}");
                return null;
            }

            var snapshot = new MahmSnapshot();
            var gpuSensors = new List<(uint Gpu, uint Id, float Value)>();
            var dump = _dumped ? null : new System.Text.StringBuilder();
            for (uint i = 0; i < count; i++)
            {
                var entry = _base + headerSize + (long)i * entrySize;
                var name = ReadAnsi(entry, NameLength);
                var value = *(float*)(entry + ValueOffset);
                uint gpu = *(uint*)(entry + GpuIndexOffset), id = *(uint*)(entry + SourceIdOffset);
                dump?.Append($"{name}|{ReadAnsi(entry + NameLength, 32)}|gpu={gpu}|id={id}|{value}; ");
                if (name.Length == 0 || !(value < Invalid) || float.IsNaN(value)) continue;
                snapshot.Values[name] = value;
                if (GpuSuffixById.ContainsKey(id) && gpu < 16 && !name.StartsWith("CPU", StringComparison.Ordinal)) gpuSensors.Add((gpu, id, value));
            }
            if (dump is not null)
            {
                _dumped = true;
                Log.Info("MAHM sensors: " + dump);
            }

            var gpuBase = _base + headerSize + (long)count * entrySize;
            for (uint g = 0; g < gpuCount && gpuEntrySize >= GpuNameOffset + NameLength; g++)
            {
                var device = ReadAnsi(gpuBase + (long)g * gpuEntrySize + GpuNameOffset, NameLength);
                snapshot.Gpus.Add(device.Length > 0 ? device : $"GPU {g + 1}");
            }
            // Sensor names differ between machines (a lone GPU is "GPU usage", the Ally's APU may name clocks/power otherwise),
            // but the source ids are fixed: expose each GPU sensor under the numbered name the rest of the app looks for.
            foreach (var (gpu, id, value) in gpuSensors)
            {
                snapshot.Values.TryAdd($"GPU{gpu + 1} {GpuSuffixById[id]}", value);
            }
            return snapshot;
        }
        catch (Exception ex)
        {
            LogOnce($"MAHM read failed: {ex.GetType().Name} {ex.Message}");
            Close();
            return null;
        }
    }

    private bool Open()
    {
        try
        {
            _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
            return true;
        }
        catch (Exception ex)
        {
            // Afterburner not running (or not installed).
            LogOnce($"MAHM open failed: {ex.GetType().Name} {ex.Message}");
            Close();
            return false;
        }
    }

    private string _lastLog = "";

    // Diagnostic breadcrumb that doesn't flood the log while the loop retries every second.
    private void LogOnce(string message)
    {
        if (message == _lastLog) return;
        _lastLog = message;
        Log.Info(message);
    }

    private void Close()
    {
        try
        {
            if (_view is not null && _base != null) _view.SafeMemoryMappedViewHandle.ReleasePointer();
        }
        catch (Exception)
        {
            // Nothing to release.
        }
        _base = null;
        _view?.Dispose();
        _map?.Dispose();
        _view = null;
        _map = null;
    }

    private static string ReadAnsi(byte* p, int max)
    {
        var length = 0;
        while (length < max && p[length] != 0) length++;
        return System.Text.Encoding.Latin1.GetString(p, length);
    }

    public void Dispose() => Close();
}
