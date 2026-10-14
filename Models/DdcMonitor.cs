namespace Lumina.Models;

/// <summary>DDC/CI üzerinden yönetilebilen fiziksel bir monitör.</summary>
internal sealed class DdcMonitor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IntPtr Handle { get; init; }
    public uint Min { get; init; }
    public uint Max { get; init; }

    public uint ToRaw(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        return Min + (uint)Math.Round((Max - Min) * percent / 100.0);
    }

    public int ToPercent(uint raw)
    {
        if (Max <= Min) return 0;
        raw = Math.Clamp(raw, Min, Max);
        return (int)Math.Round((raw - Min) * 100.0 / (Max - Min));
    }
}

/// <summary>UI tarafına verilen salt-okunur monitör anlık görüntüsü.</summary>
public sealed record MonitorSnapshot(string Id, string Name, int Percent);
