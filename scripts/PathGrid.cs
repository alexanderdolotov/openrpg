using Godot;
using System;
using System.Collections.Generic;

// Grid-based A* over the known village area — built once from whatever
// currently implements IObstacle (Home's wall footprint, each tree's
// trunk), so it scales automatically as more obstacles get added later
// without touching this file. Static/global on purpose: there's one
// shared world and one grid, not one per NPC.
//
// Deliberately NOT used for "travel" (flagpole) destinations — that's
// not this grid failing to cover them, it's the correct behavior for
// "the path is unknown": those always walk straight at the target
// (Euclidean heuristic) instead. See NPCActor.AssignAction.
public static class PathGrid
{
    private const float CellSize = 30f;
    private const float NpcClearance = 25f; // roughly NPCActor's own collision radius, plus a little buffer

    private static Vector2 _origin;
    private static int _cols;
    private static int _rows;
    private static bool[,] _blocked;
    private static bool _built;

    // For each obstacle, only checks the small local box of cells its
    // OWN inflated radius could possibly reach — not, as this used to,
    // every single cell in the whole grid checked against every
    // obstacle (O(cols×rows×obstacles)). That was fine while area was
    // a small, fixed village rect; once it started growing to actually
    // cover wherever content gets generated (see Main.
    // ComputePathGridArea), the same handful of obstacles started
    // paying for a full sweep of a grid tens of thousands of cells
    // larger — a real, measured tens-of-milliseconds hitch per rebuild
    // (Main's own BuildPathGrid runs this after every new object
    // exploration generates), not a hypothetical cost. This is
    // O(obstacles × (radius/CellSize)²) instead — a football-sized
    // obstacle circle costs the same to rasterize whether the grid
    // around it is a small village or the full map, which is what
    // actually varies here, not the obstacle count.
    public static void Build(Rect2 area, IEnumerable<(Vector2 Center, float Radius)> obstacles)
    {
        _origin = area.Position;
        _cols = Mathf.CeilToInt(area.Size.X / CellSize);
        _rows = Mathf.CeilToInt(area.Size.Y / CellSize);
        _blocked = new bool[_cols, _rows];

        foreach ((Vector2 center, float radius) in obstacles)
        {
            float inflated = radius + NpcClearance;
            Vector2 local = center - _origin;
            int minX = Mathf.Max(0, Mathf.FloorToInt((local.X - inflated) / CellSize));
            int maxX = Mathf.Min(_cols - 1, Mathf.CeilToInt((local.X + inflated) / CellSize));
            int minY = Mathf.Max(0, Mathf.FloorToInt((local.Y - inflated) / CellSize));
            int maxY = Mathf.Min(_rows - 1, Mathf.CeilToInt((local.Y + inflated) / CellSize));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (!_blocked[x, y] && CellToWorld(x, y).DistanceTo(center) <= inflated)
                        _blocked[x, y] = true;
                }
            }
        }
        _built = true;
    }

    // A simplified list of waypoints from start to goal (world space),
    // or null if no path exists — either the grid isn't built, or start
    // /goal are nowhere near a walkable cell. Callers should fall back
    // to direct movement on null, same as any other "can't do that
    // right now" case in this codebase.
    public static List<Vector2> FindPath(Vector2 start, Vector2 goal)
    {
        if (!_built)
            return null;

        (int sx, int sy) = SnapToWalkable(WorldToCell(start));
        (int gx, int gy) = SnapToWalkable(WorldToCell(goal));
        if (sx < 0 || gx < 0)
            return null;

        List<(int X, int Y)> raw = RunAStar(sx, sy, gx, gy);
        return raw == null ? null : Simplify(raw);
    }

    private static Vector2 CellToWorld(int x, int y) =>
        _origin + new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

    private static (int X, int Y) WorldToCell(Vector2 pos)
    {
        Vector2 local = pos - _origin;
        int x = Mathf.Clamp((int)(local.X / CellSize), 0, _cols - 1);
        int y = Mathf.Clamp((int)(local.Y / CellSize), 0, _rows - 1);
        return (x, y);
    }

    private static bool InBounds(int x, int y) => x >= 0 && x < _cols && y >= 0 && y < _rows;
    private static bool IsWalkable(int x, int y) => InBounds(x, y) && !_blocked[x, y];

    // Spirals outward from a (possibly blocked) cell to find the
    // nearest walkable one — start/goal positions can land inside an
    // obstacle's inflated margin even though the NPC itself is standing
    // just outside it.
    private static (int X, int Y) SnapToWalkable((int X, int Y) cell)
    {
        if (IsWalkable(cell.X, cell.Y))
            return cell;

        for (int r = 1; r <= 6; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    int nx = cell.X + dx, ny = cell.Y + dy;
                    if (IsWalkable(nx, ny))
                        return (nx, ny);
                }
            }
        }
        return (-1, -1);
    }

    private static readonly (int Dx, int Dy)[] Directions =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    private static List<(int X, int Y)> RunAStar(int sx, int sy, int gx, int gy)
    {
        var open = new PriorityQueue<(int X, int Y), float>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), float> { [(sx, sy)] = 0f };
        var closed = new HashSet<(int, int)>();

        open.Enqueue((sx, sy), Heuristic(sx, sy, gx, gy));

        while (open.Count > 0)
        {
            (int X, int Y) current = open.Dequeue();
            if (current == (gx, gy))
                return ReconstructPath(cameFrom, current);
            if (!closed.Add(current))
                continue;

            foreach ((int dx, int dy) in Directions)
            {
                int nx = current.X + dx, ny = current.Y + dy;
                if (!IsWalkable(nx, ny))
                    continue;
                // don't let the path cut across a diagonal gap between
                // two blocked cells — keeps corners honest
                if (dx != 0 && dy != 0 && (!IsWalkable(current.X + dx, current.Y) || !IsWalkable(current.X, current.Y + dy)))
                    continue;

                float stepCost = (dx != 0 && dy != 0) ? 1.41421356f : 1f;
                float tentativeG = gScore[current] + stepCost;
                var neighbor = (nx, ny);
                if (!gScore.TryGetValue(neighbor, out float existing) || tentativeG < existing)
                {
                    gScore[neighbor] = tentativeG;
                    cameFrom[neighbor] = current;
                    open.Enqueue(neighbor, tentativeG + Heuristic(nx, ny, gx, gy));
                }
            }
        }
        return null; // no path exists
    }

    // Octile distance — the admissible heuristic for 8-directional grid
    // movement (straight cost 1, diagonal cost sqrt(2)).
    private static float Heuristic(int x, int y, int gx, int gy)
    {
        float dx = Mathf.Abs(x - gx), dy = Mathf.Abs(y - gy);
        return Mathf.Max(dx, dy) + 0.41421356f * Mathf.Min(dx, dy);
    }

    private static List<(int X, int Y)> ReconstructPath(Dictionary<(int, int), (int, int)> cameFrom, (int X, int Y) current)
    {
        var path = new List<(int, int)> { current };
        while (cameFrom.TryGetValue(current, out (int, int) prev))
        {
            current = prev;
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

    // Greedy string-pulling: from each point, jump as far ahead as the
    // path allows while a straight line to that point stays clear of
    // obstacles. Turns "one waypoint per grid cell" into a small
    // handful of natural waypoints.
    private static List<Vector2> Simplify(List<(int X, int Y)> cellPath)
    {
        var worldPath = new List<Vector2>(cellPath.Count);
        foreach ((int x, int y) in cellPath)
            worldPath.Add(CellToWorld(x, y));

        var simplified = new List<Vector2> { worldPath[0] };
        int i = 0;
        while (i < worldPath.Count - 1)
        {
            int farthest = i + 1;
            for (int j = i + 2; j < worldPath.Count; j++)
            {
                if (HasLineOfSight(worldPath[i], worldPath[j]))
                    farthest = j;
            }
            simplified.Add(worldPath[farthest]);
            i = farthest;
        }
        return simplified;
    }

    private static bool HasLineOfSight(Vector2 a, Vector2 b)
    {
        float dist = a.DistanceTo(b);
        int steps = Math.Max(1, (int)(dist / (CellSize * 0.5f)));
        for (int s = 0; s <= steps; s++)
        {
            Vector2 p = a.Lerp(b, (float)s / steps);
            (int cx, int cy) = WorldToCell(p);
            if (!IsWalkable(cx, cy))
                return false;
        }
        return true;
    }
}
