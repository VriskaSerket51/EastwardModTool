using EastwardModLoader;

namespace EastwardModTool.Editing;

public sealed class LoaderModManagerClient : IModManagerClient
{
    public ModManagerSnapshot Inspect(string gameDirectory)
    {
        var manager = new LoaderModManager(gameDirectory);
        var snapshot = manager.Inspect();
        return new ModManagerSnapshot(snapshot.Installed, snapshot.Version, snapshot.Mods.Select(mod => new ManagedMod(mod.Id, mod.Name, mod.File, mod.Kind, mod.Enabled, [])).ToArray(), snapshot.Conflicts, manager.GameDirectory);
    }
    public void Install(string gameDirectory) => new LoaderModManager(gameDirectory).Install();
    public void Uninstall(string gameDirectory) => new LoaderModManager(gameDirectory).Uninstall();
    public void Add(string gameDirectory, string source) => new LoaderModManager(gameDirectory).Import(source);
    public void Remove(string gameDirectory, string id) => new LoaderModManager(gameDirectory).Remove(id);
    public void Apply(string gameDirectory, IReadOnlyDictionary<string, bool> enabled) => new LoaderModManager(gameDirectory).SetEnabled(enabled.Where(pair => pair.Value).Select(pair => pair.Key).ToArray());
}
