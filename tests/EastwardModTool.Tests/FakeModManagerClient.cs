using EastwardModTool.Editing;

public sealed class FakeModManagerClient : IModManagerClient
{
    public bool Installed
    {
        get; private set;
    }
    public int InstallCalls
    {
        get; private set;
    }
    public int ApplyCalls
    {
        get; private set;
    }
    public int UninstallCalls
    {
        get; private set;
    }
    public string? RemovedId
    {
        get; private set;
    }
    public string? AddedSource
    {
        get; private set;
    }
    public string? NormalizedDirectory
    {
        get; set;
    }
    public string? LastDirectory
    {
        get; private set;
    }
    public IReadOnlyList<string> FileConflicts { get; set; } = [];
    public bool FailApply
    {
        get; set;
    }
    private List<ManagedMod> _mods = [];
    public ModManagerSnapshot Inspect(string gameDirectory)
    {
        LastDirectory = gameDirectory;
        return new ModManagerSnapshot(Installed, Installed ? "fixture-v1" : null, _mods.ToArray(), FileConflicts, NormalizedDirectory);
    }
    public void Install(string gameDirectory)
    {
        LastDirectory = gameDirectory;
        Installed = true;
        InstallCalls++;
        if (_mods.Count == 0)
        {
            _mods.Add(new ManagedMod("first", "Fixture resource mod", "mods/first.ewmod", "ewmod", true, []));
            _mods.Add(new ManagedMod("conflicting", "Conflicting resource mod", "mods/second.ewmod", "ewmod", false, ["fixture/shared/resource"]));
        }
    }
    public void Uninstall(string gameDirectory)
    {
        LastDirectory = gameDirectory;
        if (FileConflicts.Count > 0)
        {
            throw new InvalidDataException("Fixture bootstrap checksum mismatch");
        }

        Installed = false;
        UninstallCalls++;
    }
    public void Add(string gameDirectory, string source)
    {
        LastDirectory = gameDirectory;
        AddedSource = source;
        _mods.Add(new ManagedMod("lua", "Fixture Lua mod", source, "lua", false, []));
    }
    public void Remove(string gameDirectory, string id)
    {
        LastDirectory = gameDirectory;
        RemovedId = id;
        _mods = _mods.Where(mod => mod.Id != id).ToList();
    }
    public void Apply(string gameDirectory, IReadOnlyDictionary<string, bool> enabled)
    {
        LastDirectory = gameDirectory;
        if (FileConflicts.Count > 0)
        {
            throw new InvalidDataException("Fixture managed-file validation failed");
        }

        if (FailApply)
        {
            throw new InvalidDataException("Fixture managed-file validation failed");
        }

        if (enabled["conflicting"])
        {
            throw new InvalidDataException("Fixture overlap conflict");
        }

        ApplyCalls++;
        _mods = _mods.Select(mod => mod with { Enabled = enabled[mod.Id] }).ToList();
    }
}
