using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OrielFolder.Helpers;

public sealed record InlineTreeRow<T>(T Item, int Depth, bool IsExpanded);

public sealed class InlineTreeState<T> where T : class
{
    private readonly Func<T, string> _key;
    private readonly Func<T, bool> _canExpand;
    private readonly Func<T, IEnumerable<T>> _children;
    private readonly Func<T, Task> _load;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _pending = new(StringComparer.OrdinalIgnoreCase);
    private string? _root;
    private int _revision;

    public InlineTreeState(Func<T, string> key, Func<T, bool> canExpand,
        Func<T, IEnumerable<T>> children, Func<T, Task> load)
    {
        _key = key; _canExpand = canExpand; _children = children; _load = load;
    }
    public event Action? Changed;

    public void SetRoot(string path)
    {
        if (string.Equals(_root, path, StringComparison.OrdinalIgnoreCase)) return;
        _root = path;
        _revision++;
        _expanded.Clear();
        _pending.Clear();
    }

    public bool IsExpanded(T item) => _canExpand(item) && _expanded.Contains(_key(item));

    public async Task ToggleAsync(T item)
    {
        if (!_canExpand(item)) return;
        var key = _key(item);
        if (_expanded.Remove(key)) { Changed?.Invoke(); return; }
        _expanded.Add(key);
        var revision = _revision;
        Task? task = null;
        try
        {
            if (!_pending.TryGetValue(key, out task)) _pending[key] = task = _load(item);
            if (!task.IsCompleted) Changed?.Invoke();
            await task;
        }
        catch
        {
            if (revision == _revision) _expanded.Remove(key);
            throw;
        }
        finally
        {
            if (revision == _revision)
            {
                if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, task))
                    _pending.Remove(key);
                Changed?.Invoke();
            }
        }
    }

    public IReadOnlyList<InlineTreeRow<T>> Project(IEnumerable<T> roots,
        Func<IEnumerable<T>, IEnumerable<T>>? arrange = null)
    {
        var rows = new List<InlineTreeRow<T>>();
        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Append(IEnumerable<T> items, int depth)
        {
            foreach (var item in arrange == null ? items : arrange(items))
            {
                var key = _key(item);
                if (!ancestors.Add(key)) continue;
                bool expanded = IsExpanded(item);
                rows.Add(new(item, depth, expanded));
                if (expanded) Append(_children(item), depth + 1);
                ancestors.Remove(key);
            }
        }
        Append(roots, 0);
        return rows;
    }
}
