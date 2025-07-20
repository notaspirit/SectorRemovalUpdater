namespace SectorRemovalUpdater.Models.RemovalsUpdater;

public class HashDuplicate
{
    public ulong Hash { get; set; }
    public List<SourceAndDiff> HashOccurances { get; set; } = new();
}

public class SourceAndDiff
{
    public string JsonDiffNode { get; set; }
    public string JsonDiffNodeData { get; set; }
    public int Index { get; set; }
    public string SectorPath { get; set; }
    public string NodeType { get; set; }
}