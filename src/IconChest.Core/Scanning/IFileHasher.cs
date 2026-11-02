namespace IconChest.Core.Scanning;

public interface IFileHasher
{
    string AlgorithmName { get; }
    Task<string> HashFileAsync(string filePath, CancellationToken cancellationToken = default);
}

