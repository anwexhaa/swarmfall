namespace Swarmfall.Core;

/// <summary>
/// A uniform grid for finding nearby entities without checking every pair.
///
/// Checking every enemy against every other enemy is O(n^2): 4,000 enemies is
/// 16 million distance checks per step. The grid buckets entities by cell, so a
/// query only looks at the 3x3 block of cells around a point, which is close to
/// O(1) per query when the cell size is about the query radius.
///
/// It is rebuilt every step with a counting sort into flat arrays: count per
/// cell, prefix-sum into start offsets, then scatter. No lists, no per-cell
/// allocations, and the entities of one cell sit next to each other in memory.
/// </summary>
public sealed class SpatialGrid
{
    private readonly float _cellSize;
    private readonly float _invCell;
    private readonly int _cols, _rows;
    private readonly int[] _cellStart; // cell -> first index into _items, length cells + 1
    private readonly int[] _items;     // entity slots, grouped by cell
    private readonly int[] _cellOf;    // per build entry: which cell it landed in
    private int _count;

    public SpatialGrid(float worldWidth, float worldHeight, float cellSize, int capacity)
    {
        _cellSize = cellSize;
        _invCell = 1f / cellSize;
        _cols = Math.Max(1, (int)MathF.Ceiling(worldWidth / cellSize));
        _rows = Math.Max(1, (int)MathF.Ceiling(worldHeight / cellSize));
        _cellStart = new int[_cols * _rows + 1];
        _items = new int[capacity];
        _cellOf = new int[capacity];
    }

    public float CellSize => _cellSize;
    public int Columns => _cols;
    public int Rows => _rows;

    private int CellX(float x) => Math.Clamp((int)(x * _invCell), 0, _cols - 1);
    private int CellY(float y) => Math.Clamp((int)(y * _invCell), 0, _rows - 1);

    /// <summary>
    /// Rebuilds the grid. positions[slot] is read for every slot in slots[0..count).
    /// </summary>
    public void Build(ReadOnlySpan<int> slots, ReadOnlySpan<Vec2> positions)
    {
        _count = slots.Length;
        Array.Clear(_cellStart);

        // 1. count entities per cell (stored shifted by one for the prefix sum)
        for (int i = 0; i < _count; i++)
        {
            Vec2 p = positions[slots[i]];
            int cell = CellY(p.Y) * _cols + CellX(p.X);
            _cellOf[i] = cell;
            _cellStart[cell + 1]++;
        }

        // 2. prefix sum: _cellStart[c] becomes the first index of cell c
        for (int c = 1; c < _cellStart.Length; c++)
            _cellStart[c] += _cellStart[c - 1];

        // 3. scatter, using _cellStart as a moving cursor, then shift it back
        for (int i = 0; i < _count; i++)
            _items[_cellStart[_cellOf[i]]++] = slots[i];
        for (int c = _cellStart.Length - 1; c > 0; c--)
            _cellStart[c] = _cellStart[c - 1];
        _cellStart[0] = 0;
    }

    /// <summary>
    /// Writes into results every slot within radius of center, and returns how
    /// many were found. If results fills up, the search stops there.
    /// The radius must be no larger than the cell size: only the 3x3 block of
    /// cells around the center is searched.
    /// </summary>
    public int Query(Vec2 center, float radius, ReadOnlySpan<Vec2> positions, Span<int> results)
    {
        float r2 = radius * radius;
        int cx = CellX(center.X), cy = CellY(center.Y);
        int found = 0;
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(_rows - 1, cy + 1); y++)
        {
            for (int x = Math.Max(0, cx - 1); x <= Math.Min(_cols - 1, cx + 1); x++)
            {
                int cell = y * _cols + x;
                for (int i = _cellStart[cell]; i < _cellStart[cell + 1]; i++)
                {
                    int slot = _items[i];
                    if (Vec2.DistanceSquared(positions[slot], center) <= r2)
                    {
                        if (found == results.Length) return found;
                        results[found++] = slot;
                    }
                }
            }
        }
        return found;
    }

    /// <summary>The slot nearest to center within maxRadius, or -1. Searches outward ring by ring.</summary>
    public int Nearest(Vec2 center, float maxRadius, ReadOnlySpan<Vec2> positions)
    {
        int cx = CellX(center.X), cy = CellY(center.Y);
        int maxRing = (int)MathF.Ceiling(maxRadius * _invCell);
        int best = -1;
        float bestD2 = maxRadius * maxRadius;
        for (int ring = 0; ring <= maxRing; ring++)
        {
            // Every point in ring r is at least (r - 1) cells away, so once that
            // lower bound exceeds the best distance found, nothing further can win.
            float minDist = (ring - 1) * _cellSize;
            if (best >= 0 && minDist > 0 && minDist * minDist > bestD2) break;

            for (int y = cy - ring; y <= cy + ring; y++)
            {
                if (y < 0 || y >= _rows) continue;
                bool edgeRow = y == cy - ring || y == cy + ring;
                for (int x = cx - ring; x <= cx + ring; x += edgeRow ? 1 : 2 * ring)
                {
                    if (x >= 0 && x < _cols)
                    {
                        int cell = y * _cols + x;
                        for (int i = _cellStart[cell]; i < _cellStart[cell + 1]; i++)
                        {
                            int slot = _items[i];
                            float d2 = Vec2.DistanceSquared(positions[slot], center);
                            if (d2 < bestD2) { bestD2 = d2; best = slot; }
                        }
                    }
                    if (ring == 0) break;
                }
            }
        }
        return best;
    }
}
