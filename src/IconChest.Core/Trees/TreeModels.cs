namespace IconChest.Core.Trees;

public sealed class FolderTreeNode
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public int TotalCount { get; set; }
    public List<FolderTreeNode> Children { get; set; } = new();
}

public sealed class MonthNode
{
    public int Month { get; set; }
    public int Count { get; set; }
}

public sealed class YearNode
{
    public int Year { get; set; }
    public int Count { get; set; }
    public List<MonthNode> Months { get; set; } = new();
}

public sealed class YearTreeResult
{
    public List<YearNode> Years { get; set; } = new();
    public int UnknownCount { get; set; }
}

