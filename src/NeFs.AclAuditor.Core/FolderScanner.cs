using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core;

public interface IFolderScanner
{
    Task<ScanResult> ScanAsync(
        string rootPath,
        int maxDepth,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class FolderScanner : IFolderScanner
{
    private readonly IAclReader _aclReader;

    public FolderScanner(IAclReader aclReader)
    {
        _aclReader = aclReader;
    }

    public Task<ScanResult> ScanAsync(
        string rootPath,
        int maxDepth,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Путь к корню не задан.", nameof(rootPath));
        if (maxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Глубина не может быть отрицательной.");

        return Task.Run(() => Scan(rootPath, maxDepth, progress, cancellationToken), cancellationToken);
    }

    private ScanResult Scan(
        string rootPath,
        int maxDepth,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fullRoot = Path.GetFullPath(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!Directory.Exists(fullRoot))
            throw new DirectoryNotFoundException($"Каталог не найден: {fullRoot}");

        var folderCount = 0;
        var errorCount = 0;

        var root = Visit(fullRoot, Path.GetFileName(fullRoot.TrimEnd('\\', '/')) is { Length: > 0 } name
                ? name
                : fullRoot,
            depth: 0,
            maxDepth,
            progress,
            cancellationToken,
            ref folderCount,
            ref errorCount);

        return new ScanResult
        {
            Root = root,
            FolderCount = folderCount,
            ErrorCount = errorCount
        };
    }

    private FolderNode Visit(
        string path,
        string name,
        int depth,
        int maxDepth,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken,
        ref int folderCount,
        ref int errorCount)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = new FolderNode
        {
            FullPath = path,
            Name = name,
            Depth = depth
        };

        try
        {
            node.Aces = _aclReader.ReadAces(path).ToList();
        }
        catch (Exception ex)
        {
            node.Error = $"ACL: {ex.Message}";
            errorCount++;
        }

        folderCount++;
        progress?.Report(new ScanProgress
        {
            FoldersProcessed = folderCount,
            ErrorCount = errorCount,
            CurrentPath = path
        });

        if (depth >= maxDepth)
            return node;

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(path);
        }
        catch (Exception ex)
        {
            node.Error = string.IsNullOrEmpty(node.Error)
                ? $"Листинг: {ex.Message}"
                : $"{node.Error}; Листинг: {ex.Message}";
            errorCount++;
            progress?.Report(new ScanProgress
            {
                FoldersProcessed = folderCount,
                ErrorCount = errorCount,
                CurrentPath = path
            });
            return node;
        }

        foreach (var childPath in children.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childName = Path.GetFileName(childPath);
            var child = Visit(childPath, childName, depth + 1, maxDepth, progress, cancellationToken, ref folderCount, ref errorCount);
            node.Children.Add(child);
        }

        return node;
    }
}
