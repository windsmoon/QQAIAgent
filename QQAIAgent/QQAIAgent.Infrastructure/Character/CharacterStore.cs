using QQAIAgent.Abstractions.Character;

namespace QQAIAgent.Infrastructure.Character;

public sealed class CharacterStore(string directoryPath) : ICharacterStore
{
    #region methods
    public Task<string> LoadAsync(long userId, long groupId, CancellationToken cancellationToken)
    {
        var path = GetPath(userId, groupId);
        if (File.Exists(path) == false)
        {
            path = GetPath(userId, null);
        }

        if (File.Exists(path) == false)
        {
            path = Path.Combine(directoryPath, "Default.txt");
        }

        return File.ReadAllTextAsync(path, cancellationToken);
    }

    public Task SaveAsync(long userId, long? groupId, string content, CancellationToken cancellationToken)
    {
        var path = GetPath(userId, groupId);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        return File.WriteAllTextAsync(path, content, cancellationToken);
    }

    public void Delete(long userId, long? groupId)
    {
        var path = GetPath(userId, groupId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetPath(long userId, long? groupId)
    {
        return groupId.HasValue
            ? Path.Combine(directoryPath, groupId.Value.ToString(), $"{userId}.txt")
            : Path.Combine(directoryPath, $"{userId}.txt");
    }
    #endregion
}
