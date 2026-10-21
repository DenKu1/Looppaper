namespace Looppaper.Core.Media;

/// <summary>
/// Endless sequence over a list of media files, either in list order (wrapping around) or shuffled.
/// Shuffle uses Fisher-Yates and reshuffles after every full pass, never repeating the item that
/// was just returned at the start of the next pass. <see cref="Previous"/> steps back through the items already
/// returned (and <see cref="Next"/> then replays them forward), so it also works when shuffled. Not thread-safe.
/// </summary>
public sealed class MediaSequence
{
    /// <summary>How many returned items <see cref="Previous"/> can go back through.</summary>
    public const int HistoryLimit = 100;

    private readonly Random _random;
    private readonly List<string> _history = [];
    private readonly Stack<string> _forward = new();
    private List<string> _items = [];
    private HashSet<string> _present = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _order = [];
    private int _position;

    public MediaSequence(bool shuffle, Random? random = null)
    {
        Shuffle = shuffle;
        _random = random ?? new Random();
    }

    public bool Shuffle { get; }

    public IReadOnlyList<string> Items => _items;

    public int Count => _items.Count;

    /// <summary>The item most recently returned by <see cref="Next"/> or <see cref="Previous"/>.</summary>
    public string? Current { get; private set; }

    /// <summary>
    /// Replaces the items (e.g. after a rescan). Unchanged lists keep their position; otherwise ordered
    /// sequences continue after <see cref="Current"/> and shuffled ones start a fresh pass.
    /// </summary>
    public void Update(IReadOnlyList<string> items)
    {
        if (items.Count == _items.Count && items.SequenceEqual(_items, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _items = [.. items];
        _present = new HashSet<string>(_items, StringComparer.OrdinalIgnoreCase);
        if (Shuffle)
        {
            Reshuffle();
        }
        else
        {
            _order = _items;
            _position = Current is null ? 0 : FirstAfter(Current);
        }
    }

    /// <summary>Returns the next item, or null when the list is empty.</summary>
    public string? Next()
    {
        if (_items.Count == 0)
        {
            return null;
        }

        // After Previous: replay the items that were stepped back over first.
        while (_forward.TryPop(out var replay))
        {
            if (_present.Contains(replay))
            {
                return Record(replay);
            }
        }

        if (_position >= _order.Count)
        {
            if (Shuffle)
            {
                Reshuffle();
            }
            else
            {
                _position = 0;
            }
        }

        return Record(_order[_position++]);
    }

    /// <summary>
    /// Makes <paramref name="item"/> current, as if <see cref="Next"/> had returned it (e.g. the configured video to
    /// start with). In list order the sequence continues after it; shuffled, a fresh pass starts that avoids it first.
    /// </summary>
    public void JumpTo(string item)
    {
        _forward.Clear();
        Record(item);
        if (Shuffle)
        {
            Reshuffle();
            return;
        }

        var index = _order.FindIndex(i => string.Equals(i, item, StringComparison.OrdinalIgnoreCase));
        _position = index >= 0 ? index + 1 : FirstAfter(item);
    }

    /// <summary>
    /// Steps back to the item returned before <see cref="Current"/>. Null when there is none: an empty or one-item
    /// list, nothing returned yet, or a shuffled sequence back at the oldest item it remembers. In list order it keeps
    /// going back past the start of the history.
    /// </summary>
    public string? Previous()
    {
        if (Current is null || _items.Count < 2)
        {
            return null;
        }

        for (var i = _history.Count - 2; i >= 0; i--)
        {
            if (_present.Contains(_history[i]))
            {
                StepBackTo(i);
                return Current;
            }
        }

        if (Shuffle)
        {
            return null;
        }

        var index = _items.FindIndex(item => string.Equals(item, Current, StringComparison.OrdinalIgnoreCase));
        var previous = index < 0 ? _items[^1] : _items[(index - 1 + _items.Count) % _items.Count];
        _forward.Push(Current);
        _history.Clear();
        _history.Add(previous);
        Current = previous;
        return previous;
    }

    /// <summary>Makes history entry <paramref name="index"/> current; the later ones wait in <see cref="_forward"/>.</summary>
    private void StepBackTo(int index)
    {
        for (var j = _history.Count - 1; j > index; j--)
        {
            _forward.Push(_history[j]);
        }

        _history.RemoveRange(index + 1, _history.Count - index - 1);
        Current = _history[index];
    }

    private string Record(string item)
    {
        Current = item;
        _history.Add(item);
        if (_history.Count > HistoryLimit)
        {
            _history.RemoveAt(0);
        }

        return item;
    }

    private void Reshuffle()
    {
        _order = [.. _items];
        for (var i = _order.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }

        if (_order.Count > 1 && Current is not null && string.Equals(_order[0], Current, StringComparison.OrdinalIgnoreCase))
        {
            var swap = 1 + _random.Next(_order.Count - 1);
            (_order[0], _order[swap]) = (_order[swap], _order[0]);
        }

        _position = 0;
    }

    /// <summary>Index of the first item that sorts after <paramref name="item"/> (wrapping to 0).</summary>
    private int FirstAfter(string item)
    {
        for (var i = 0; i < _order.Count; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Compare(_order[i], item) > 0)
            {
                return i;
            }
        }

        return 0;
    }
}
