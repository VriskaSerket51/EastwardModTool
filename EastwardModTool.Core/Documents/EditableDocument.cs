using EastwardLib.Assets;

namespace EastwardModTool.Core.Documents;

public abstract class EditableDocument<T> : AssetDocument where T : notnull
{
    private readonly T _initial;
    private readonly List<T> _history;
    private readonly List<long> _sizes = [0];
    private long _historySize;
    private int _position;
    protected T State => _history[_position];
    internal override object CurrentState => State;
    public override bool IsDirty => !EqualityComparer<T>.Default.Equals(_initial, State);
    public override bool CanUndo => CanEdit && _position > 0;
    public override bool CanRedo => CanEdit && _position < _history.Count - 1;

    protected EditableDocument(string name, Asset original, T initial, bool canEdit) : base(name, original, canEdit)
    {
        _initial = initial;
        _history = [initial];
    }

    protected void Change(T value)
    {
        RequireEditable();
        if (EqualityComparer<T>.Default.Equals(State, value))
        {
            return;
        }

        _historySize -= _sizes.Skip(_position + 1).Sum();
        _sizes.RemoveRange(_position + 1, _sizes.Count - _position - 1);
        _history.RemoveRange(_position + 1, _history.Count - _position - 1);
        _history.Add(value);
        var size = EqualityComparer<T>.Default.Equals(_initial, value) ? 0 : EstimateSize(value);
        _sizes.Add(size);
        _historySize += size;
        while (_history.Count > 101 || (_historySize > 64 * 1024 * 1024 && _history.Count > 2))
        {
            // Keep the baseline available even when older intermediate edits exceed the budget.
            _historySize -= _sizes[1];
            _history.RemoveAt(1);
            _sizes.RemoveAt(1);
        }

        _position = _history.Count - 1;
        StateChanged();
    }

    public override void Undo()
    {
        if (CanUndo)
        {
            _position--;
            StateChanged();
        }
    }

    public override void Redo()
    {
        if (CanRedo)
        {
            _position++;
            StateChanged();
        }
    }

    public override void Reset()
    {
        Change(_initial);
    }

    protected virtual void StateChanged()
    {
        Notify(nameof(IsDirty));
        Notify(nameof(CanUndo));
        Notify(nameof(CanRedo));
    }

    protected abstract long EstimateSize(T value);
}
