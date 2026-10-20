namespace Looppaper.Core.Media;

/// <summary>
/// Endless sequence over a list of media files, either in list order (wrapping around) or shuffled.
/// Shuffle uses Fisher-Yates and reshuffles after every full pass, never repeating the item that
/// was just returned at the start of the next pass. Not thread-safe.
/// </summary>
public sealed class MediaSequence
{
    private readonly Random _random;
    private List<string> _items = [];
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

    /// <summary>The item most recently returned by <see cref="Next"/>.</summary>
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

        Current = _order[_position++];
        return Current;
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
