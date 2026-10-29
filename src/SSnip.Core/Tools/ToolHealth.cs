namespace SSnip.Core.Tools;

/// <summary>Summarises tool statuses for the status bar.</summary>
public static class ToolHealth
{
    public static (bool AllHealthy, string Summary) Summarise(IReadOnlyCollection<ToolStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        var broken = statuses.Where(s => !s.IsHealthy).Select(s => ToolDescriptor.For(s.Tool).DisplayName).ToList();
        return broken.Count == 0
            ? (true, "Tools OK")
            : (false, broken.Count == 1 ? $"{broken[0]} missing" : $"{broken.Count} tools missing");
    }
}
