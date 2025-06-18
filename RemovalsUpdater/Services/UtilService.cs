namespace RemovalsUpdater.Services;

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
}