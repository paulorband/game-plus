using System.IO.MemoryMappedFiles;

namespace GamePlus.Services.Monitoring;

/// <summary>FPS data of the game RTSS is hooked into.</summary>
public readonly record struct GameFrameInfo(int ProcessId, string ProcessName, double Fps, double FrameTimeMs);

/// <summary>
/// Talks to RivaTuner Statistics Server through its shared memory ("RTSSSharedMemoryV2"): reads the game's frame stats and
/// writes text into an OSD slot of its own. Both work without administrator rights.
/// </summary>
public sealed unsafe class RtssClient : IDisposable
{
    private const string MapName = "RTSSSharedMemoryV2";
    private const uint Signature = 0x52545353; // 'RTSS' multi-char constant, as the header defines it
    private const string Owner = "GamePlus";

    // Shared memory header (all DWORDs).
    private const int OffVersion = 4, OffAppEntrySize = 8, OffAppArrOffset = 12, OffAppArrSize = 16;
    private const int OffOsdEntrySize = 20, OffOsdArrOffset = 24, OffOsdArrSize = 28, OffOsdFrame = 32, OffBusy = 36;

    // OSD slot layout.
    private const int SlotOwner = 256, SlotTextEx = 512, SlotTextExSize = 4096, SlotBuffer = SlotTextEx + SlotTextExSize, SlotBufferSize = 262144;

    // Application entry layout (up to the fields we need).
    private const int AppPid = 0, AppName = 4, AppTime0 = 268, AppTime1 = 272, AppFrames = 276, AppFrameTime = 280;

    private const uint MinVersionTags = 0x0002000B;
    private const uint MinVersionObjects = 0x0002000C;
    private const uint MinVersionBusy = 0x0002000E;
    private const uint MinVersionTextEx = 0x00020007;

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private byte* _base;
    private int _slot = -1;
    private int _lastPid;

    public bool IsConnected => _view is not null;

    /// <summary>Whether the connected RTSS understands text tags and embedded graphs (v2.12+).</summary>
    public bool SupportsGraphs => IsConnected && Version >= MinVersionObjects;

    private uint Version => *(uint*)(_base + OffVersion);

    /// <summary>Connects (or reconnects). False when RTSS is closed or too old for extended OSD text.</summary>
    public bool Connect()
    {
        try
        {
            if (_view is not null)
            {
                if (*(uint*)_base == Signature) return true;
                Close();
            }

            _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.ReadWrite);
            _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
            if (*(uint*)_base != Signature || Version < MinVersionTags || Version < MinVersionTextEx)
            {
                LogOnce($"RTSS unsupported: signature={*(uint*)_base:X8} version={Version:X8}");
                Close();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LogOnce($"RTSS connect failed: {ex.GetType().Name} {ex.Message}");
            Close();
            return false;
        }
    }

    private string _lastLog = "";

    private void LogOnce(string message)
    {
        if (message == _lastLog) return;
        _lastLog = message;
        Log.Info(message);
    }

    /// <summary>Frame stats of the game that is rendering, ignoring the given process names (this app, Lossless Scaling).</summary>
    public GameFrameInfo? ReadGame(int foregroundPid, ICollection<string> ignoredNames)
    {
        if (!IsConnected) return null;
        try
        {
            var appOffset = *(uint*)(_base + OffAppArrOffset);
            var appSize = *(uint*)(_base + OffAppEntrySize);
            var appCount = Math.Min(*(uint*)(_base + OffAppArrSize), 256u);
            var now = (uint)Environment.TickCount;

            GameFrameInfo? best = null;
            for (uint i = 0; i < appCount; i++)
            {
                var entry = _base + appOffset + (long)i * appSize;
                var pid = *(int*)(entry + AppPid);
                if (pid == 0) continue;

                uint t0 = *(uint*)(entry + AppTime0), t1 = *(uint*)(entry + AppTime1), frames = *(uint*)(entry + AppFrames);
                // Entries of games that stopped rendering linger for a while; only recently updated ones count.
                if (t1 == 0 || now - t1 > 2000 || t1 <= t0 || frames == 0) continue;

                var name = System.IO.Path.GetFileName(ReadAnsi(entry + AppName, 260));
                if (ignoredNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                var fps = 1000.0 * frames / (t1 - t0);
                var frameTime = *(uint*)(entry + AppFrameTime);
                var info = new GameFrameInfo(pid, name, fps, frameTime > 0 ? frameTime / 1000.0 : 1000.0 / fps);

                // Prefer the game that was already being tracked, then the one in the foreground, then the busiest.
                if (pid == _lastPid) { best = info; break; }
                if (best is null || pid == foregroundPid || (best.Value.ProcessId != foregroundPid && info.Fps > best.Value.Fps)) best = info;
            }
            _lastPid = best?.ProcessId ?? 0;
            return best;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Writes the OSD text (and the embedded-object buffer, if any) into this app's slot.</summary>
    public bool WriteOsd(string text, byte[]? objects)
    {
        if (!IsConnected) return false;
        try
        {
            var slot = FindSlot(claim: true);
            if (slot is null) return false;

            var busy = (int*)(_base + OffBusy);
            var locking = Version >= MinVersionBusy;
            // Bit 0 tells us the renderer is reading the OSD right now; skip this refresh rather than tear the text.
            if (locking && (Interlocked.Or(ref *busy, 1) & 1) != 0) return false;
            try
            {
                var bytes = System.Text.Encoding.Latin1.GetBytes(text);
                var length = Math.Min(bytes.Length, SlotTextExSize - 1);
                for (var i = 0; i < length; i++) slot[SlotTextEx + i] = bytes[i];
                slot[SlotTextEx + length] = 0;

                if (objects is { Length: > 0 } && Version >= MinVersionObjects && objects.Length <= SlotBufferSize)
                {
                    for (var i = 0; i < objects.Length; i++) slot[SlotBuffer + i] = objects[i];
                }
            }
            finally
            {
                if (locking) Interlocked.And(ref *busy, ~1);
            }

            Interlocked.Increment(ref *(int*)(_base + OffOsdFrame));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Clears this app's slot so the text disappears from the game.</summary>
    public void ReleaseOsd()
    {
        if (!IsConnected) return;
        try
        {
            var slot = FindSlot(claim: false);
            if (slot is null) return;
            var size = *(uint*)(_base + OffOsdEntrySize);
            for (uint i = 0; i < size; i++) slot[i] = 0;
            Interlocked.Increment(ref *(int*)(_base + OffOsdFrame));
            _slot = -1;
        }
        catch (Exception)
        {
            // RTSS went away; nothing left to clear.
        }
    }

    // Slot 0 belongs to Afterburner itself, so third parties start at 1. Reuse ours, otherwise claim the first free one.
    private byte* FindSlot(bool claim)
    {
        var offset = *(uint*)(_base + OffOsdArrOffset);
        var size = *(uint*)(_base + OffOsdEntrySize);
        var count = Math.Min(*(uint*)(_base + OffOsdArrSize), 8u);

        if (_slot >= 1 && _slot < count && OwnerIs(_base + offset + (long)_slot * size, Owner)) return _base + offset + (long)_slot * size;

        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1 && !claim) break;
            for (var i = 1; i < count; i++)
            {
                var slot = _base + offset + (long)i * size;
                if (pass == 0 ? OwnerIs(slot, Owner) : slot[SlotOwner] == 0)
                {
                    if (pass == 1)
                    {
                        var owner = System.Text.Encoding.ASCII.GetBytes(Owner);
                        for (var b = 0; b < owner.Length; b++) slot[SlotOwner + b] = owner[b];
                        slot[SlotOwner + owner.Length] = 0;
                    }
                    _slot = i;
                    return slot;
                }
            }
        }
        return null;
    }

    private static bool OwnerIs(byte* slot, string owner)
    {
        for (var i = 0; i < owner.Length; i++) if (slot[SlotOwner + i] != owner[i]) return false;
        return slot[SlotOwner + owner.Length] == 0;
    }

    private static string ReadAnsi(byte* p, int max)
    {
        var length = 0;
        while (length < max && p[length] != 0) length++;
        return System.Text.Encoding.Latin1.GetString(p, length);
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
        _slot = -1;
    }

    public void Dispose()
    {
        ReleaseOsd();
        Close();
    }
}
