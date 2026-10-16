namespace EastwardModTool.Editing;

public sealed record ManagedMod(string Id, string Name, string File, string Kind, bool Enabled, IReadOnlyList<string> Conflicts);
public sealed record ModManagerSnapshot(bool Installed, string? Version, IReadOnlyList<ManagedMod> Mods, IReadOnlyList<string> Conflicts, string? GameDirectory = null);

public interface IModManagerClient
{
    ModManagerSnapshot Inspect(string gameDirectory);
    void Install(string gameDirectory);
    void Uninstall(string gameDirectory);
    void Add(string gameDirectory, string source);
    void Apply(string gameDirectory, IReadOnlyDictionary<string, bool> enabled);
    void Remove(string gameDirectory, string id);
}
