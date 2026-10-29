namespace SSnip.Core.Tests;

/// <summary>Hand-written yt-dlp output samples. No real URLs: ids are fake and hosts are example.com.</summary>
internal static class Fixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), System.Text.Encoding.UTF8);
}
