using System.ComponentModel;

namespace EastwardModTool.Core.Documents;

/// <summary>Open tabs and navigation share the workspace's documents without owning their drafts.</summary>
public sealed class DocumentSession : IDisposable
{
    private readonly List<AssetDocument> _open = [];
    private readonly Dictionary<string, AssetDocument> _known = new(StringComparer.Ordinal);
    private readonly List<string> _history = [];
    private readonly List<string> _closed = [];
    private readonly List<string> _pinned = [];
    private int _position = -1;
    public IReadOnlyList<AssetDocument> Documents => _open.AsReadOnly();
    public IReadOnlyList<string> PinnedNames => _pinned.AsReadOnly();
    public AssetDocument? SelectedDocument
    {
        get; private set;
    }
    public bool CanGoBack => _position > 0;
    public bool CanGoForward => _position + 1 < _history.Count;
    public bool CanReopen => _closed.Count > 0;
    public event EventHandler? Changed;
    public event EventHandler? SelectionChanged;

    public void Open(AssetDocument document)
    {
        if (_known.TryGetValue(document.Name, out var existing) && !ReferenceEquals(existing, document))
        {
            throw new ArgumentException("A different document already uses this name", nameof(document));
        }
        if (_known.TryAdd(document.Name, document))
        {
            document.PropertyChanged += DocumentChanged;
        }

        Select(document, true);
    }

    public void Select(string name)
    {
        Select(_known[name], true);
    }

    private void Select(AssetDocument document, bool record)
    {
        if (!_open.Contains(document))
        {
            _open.Add(document);
        }

        _closed.Remove(document.Name);
        if (ReferenceEquals(SelectedDocument, document))
        {
            return;
        }

        SelectedDocument = document;
        if (record)
        {
            _history.RemoveRange(_position + 1, _history.Count - _position - 1);
            _history.Add(document.Name);
            if (_history.Count > 256)
            {
                _history.RemoveAt(0);
            }

            _position = _history.Count - 1;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Close(string name)
    {
        var index = _open.FindIndex(document => document.Name == name);
        if (index < 0)
        {
            return;
        }

        var document = _open[index];
        _open.RemoveAt(index);
        _closed.Remove(name);
        _closed.Add(name);
        if (ReferenceEquals(document, SelectedDocument))
        {
            SelectedDocument = null;
            if (_open.Count > 0)
            {
                Select(_open[Math.Min(index, _open.Count - 1)], true);
            }
            else
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reopen()
    {
        if (CanReopen)
        {
            Select(_known[_closed[^1]], true);
        }
    }

    public void GoBack()
    {
        if (CanGoBack)
        {
            Select(_known[_history[--_position]], false);
        }
    }

    public void GoForward()
    {
        if (CanGoForward)
        {
            Select(_known[_history[++_position]], false);
        }
    }

    public void TogglePin(string name)
    {
        if (!_known.ContainsKey(name))
        {
            throw new ArgumentException("Open the document before pinning it", nameof(name));
        }

        if (!_pinned.Remove(name))
        {
            _pinned.Add(name);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void DocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AssetDocument.IsDirty))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        foreach (var document in _known.Values)
        {
            document.PropertyChanged -= DocumentChanged;
        }

        _known.Clear();
        _open.Clear();
        _history.Clear();
        _closed.Clear();
        _pinned.Clear();
        _position = -1;
        SelectedDocument = null;
    }
}
