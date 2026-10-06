namespace Swarmfall.Core;

/// <summary>
/// A fixed-capacity object pool for structs. Every slot is allocated once, up
/// front, so spawning and killing entities during play never allocates and never
/// wakes the garbage collector mid-frame.
///
/// Live entities are also kept in a dense list, so iterating them touches only
/// live slots, in order, with no gaps. Spawn and despawn are both O(1): despawn
/// swaps the last live index into the hole.
/// </summary>
public sealed class Pool<T> where T : struct
{
    private readonly T[] _items;
    private readonly int[] _free;      // stack of unused slots
    private int _freeCount;
    private readonly int[] _active;    // dense list of live slots
    private readonly int[] _activePos; // slot -> position in _active, or -1
    private int _activeCount;

    public Pool(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new T[capacity];
        _free = new int[capacity];
        _active = new int[capacity];
        _activePos = new int[capacity];
        Clear();
    }

    public int Capacity => _items.Length;
    public int Count => _activeCount;

    /// <summary>The slot at position i of the dense live list, 0 &lt;= i &lt; Count.</summary>
    public int ActiveAt(int i) => _active[i];

    public ref T this[int slot] => ref _items[slot];

    public bool IsActive(int slot) => _activePos[slot] >= 0;

    /// <summary>Takes a free slot and returns it, or -1 if the pool is full.</summary>
    public int Spawn(in T value)
    {
        if (_freeCount == 0) return -1;
        int slot = _free[--_freeCount];
        _items[slot] = value;
        _activePos[slot] = _activeCount;
        _active[_activeCount++] = slot;
        return slot;
    }

    public void Despawn(int slot)
    {
        int pos = _activePos[slot];
        if (pos < 0) return; // already free; despawning twice is a no-op
        int last = _active[--_activeCount];
        _active[pos] = last;
        _activePos[last] = pos;
        _activePos[slot] = -1;
        _free[_freeCount++] = slot;
    }

    public void Clear()
    {
        _activeCount = 0;
        _freeCount = _items.Length;
        for (int i = 0; i < _items.Length; i++)
        {
            _free[i] = _items.Length - 1 - i; // hand out slot 0 first
            _activePos[i] = -1;
        }
    }
}
