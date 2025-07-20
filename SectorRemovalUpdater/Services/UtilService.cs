using System.Diagnostics;

namespace SectorRemovalUpdater.Services;

public static class UtilService
{
    public static IEnumerable<int> ClosestSteps(int start, int range)
    {
        var visited = new HashSet<int> { start };
        yield return start;

        for (int i = 1; i <= range; i++)
        {
            int up = start + i;
            int down = start - i;

            if (!visited.Contains(up))
            {
                visited.Add(up);
                yield return up;
            }

            if (!visited.Contains(down))
            {
                visited.Add(down);
                yield return down;
            }
        }
    }
    
    private static readonly Random _random = new Random();
    public static void Shuffle<T>(T[] array)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));
            
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = _random.Next(0, i + 1);
            
            // Swap elements at positions i and j
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    private const string _sectorPathPrefix = @"base\worlds\03_night_city\_compiled\";
    private const string _sectorPathSuffix = ".streamingsector";
    public static string GetAbbreviatedSectorPath(string sectorPath)
    {
        return sectorPath.Replace(_sectorPathPrefix, "").Replace(_sectorPathSuffix, "");
    }
    
    public static string GetSectorPath(string sectorPath)
    {
        return _sectorPathPrefix + sectorPath.Trim() + _sectorPathSuffix;
    }

    public static string? GetGameVersion(string gameDirPath)
    {
        var filePath = Path.Combine(gameDirPath, "bin", "x64", "Cyberpunk2077.exe");
        return !File.Exists(filePath) ? null : FileVersionInfo.GetVersionInfo(filePath).ProductVersion;
    }
}