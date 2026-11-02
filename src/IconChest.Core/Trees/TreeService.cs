using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Scanning;

namespace IconChest.Core.Trees;

public interface ITreeService
{
    IReadOnlyList<FolderTreeNode> GetFolderTree();
    YearTreeResult GetYearTree(string dateField = "effective");
}

public sealed class TreeService : ITreeService
{
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public TreeService(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public IReadOnlyList<FolderTreeNode> GetFolderTree()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();

        // 1. Get all library folders
        var folders = conn.Query<(long Id, string Path)>(
            "SELECT id, path FROM library_folder ORDER BY path ASC;").ToList();

        // 2. Get directory counts excluding hidden and missing
        const string dirCountsSql = @"
            SELECT folder_id AS FolderId,
                   directory_path AS DirectoryPath,
                   directory_key AS DirectoryKey,
                   COUNT(*) AS ImageCount
            FROM image
            WHERE is_hidden = 0 AND is_missing = 0
            GROUP BY folder_id, directory_key, directory_path;";

        var dirCounts = conn.Query<(long FolderId, string DirectoryPath, string DirectoryKey, int ImageCount)>(dirCountsSql)
            .ToLookup(x => x.FolderId);

        var roots = new List<FolderTreeNode>();

        foreach (var folder in folders)
        {
            var folderRootKey = PathKeyProvider.Default.GetDirectoryKey(folder.Path);
            var folderName = Path.GetFileName(folder.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(folderName))
            {
                folderName = folder.Path;
            }

            var rootNode = new FolderTreeNode
            {
                Path = folder.Path,
                Name = folderName,
                Count = 0
            };

            // Intermediate map for building subfolder hierarchy
            var nodeMap = new Dictionary<string, FolderTreeNode>(StringComparer.OrdinalIgnoreCase)
            {
                [folderRootKey] = rootNode
            };

            var subDirs = dirCounts[folder.Id].ToList();

            // Set root folder's direct count
            var rootDirRecord = subDirs.FirstOrDefault(d => string.Equals(d.DirectoryKey, folderRootKey, StringComparison.OrdinalIgnoreCase));
            if (rootDirRecord != default)
            {
                rootNode.Count = rootDirRecord.ImageCount;
            }

            // Build hierarchy for subdirectories
            foreach (var d in subDirs)
            {
                if (string.Equals(d.DirectoryKey, folderRootKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Relative path key from root
                string relKey;
                if (d.DirectoryKey.StartsWith(folderRootKey + "/", StringComparison.OrdinalIgnoreCase))
                {
                    relKey = d.DirectoryKey[(folderRootKey.Length + 1)..];
                }
                else
                {
                    continue;
                }

                var segments = relKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var currentParent = rootNode;
                var currentKey = folderRootKey;

                for (int i = 0; i < segments.Length; i++)
                {
                    var seg = segments[i];
                    currentKey += "/" + seg;

                    if (!nodeMap.TryGetValue(currentKey, out var childNode))
                    {
                        var segPath = Path.Combine(currentParent.Path, seg);
                        childNode = new FolderTreeNode
                        {
                            Path = segPath,
                            Name = seg,
                            Count = 0
                        };
                        currentParent.Children.Add(childNode);
                        nodeMap[currentKey] = childNode;
                    }

                    if (i == segments.Length - 1)
                    {
                        childNode.Count = d.ImageCount;
                    }

                    currentParent = childNode;
                }
            }

            // Compute TotalCount recursively
            CalculateTotalCounts(rootNode);
            roots.Add(rootNode);
        }

        return roots;
    }

    public YearTreeResult GetYearTree(string dateField = "effective")
    {
        string dateCol = dateField.ToLowerInvariant() switch
        {
            "created" => "created_utc",
            "modified" => "modified_utc",
            "taken" => "COALESCE(taken_override_utc, exif_date_utc)",
            "added" => "added_utc",
            _ => "effective_date_utc"
        };

        using var conn = _connectionFactory.CreateAndOpenConnection();
        string sql = $@"
            SELECT SUBSTR({dateCol}, 1, 4) AS YearStr,
                   SUBSTR({dateCol}, 6, 2) AS MonthStr,
                   COUNT(*) AS ImageCount
            FROM image
            WHERE is_hidden = 0 AND is_missing = 0
            GROUP BY SUBSTR({dateCol}, 1, 4), SUBSTR({dateCol}, 6, 2);";

        var rows = conn.Query<(string? YearStr, string? MonthStr, int ImageCount)>(sql).ToList();

        var result = new YearTreeResult();
        var yearMap = new Dictionary<int, YearNode>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.YearStr) || !int.TryParse(row.YearStr, out int year))
            {
                result.UnknownCount += row.ImageCount;
                continue;
            }

            if (!yearMap.TryGetValue(year, out var yearNode))
            {
                yearNode = new YearNode { Year = year };
                yearMap[year] = yearNode;
            }

            yearNode.Count += row.ImageCount;

            if (!string.IsNullOrWhiteSpace(row.MonthStr) && int.TryParse(row.MonthStr, out int month))
            {
                yearNode.Months.Add(new MonthNode { Month = month, Count = row.ImageCount });
            }
        }

        result.Years = yearMap.Values
            .OrderByDescending(y => y.Year)
            .ToList();

        foreach (var yr in result.Years)
        {
            yr.Months = yr.Months.OrderBy(m => m.Month).ToList();
        }

        return result;
    }

    private static int CalculateTotalCounts(FolderTreeNode node)
    {
        int total = node.Count;
        foreach (var child in node.Children)
        {
            total += CalculateTotalCounts(child);
        }

        node.TotalCount = total;
        return total;
    }
}
