using System.Collections.Immutable;

namespace Phase1A.World;

public readonly record struct TilePosition(int X, int Y);
public sealed record FieldEncounterDefinition(string Id, TilePosition Position);
public sealed record FieldEncounterState(string Id, TilePosition Position, bool Defeated);
public readonly record struct FieldMoveResult(bool Moved, string? EncounterId = null);

public sealed record FieldMapDefinition(
    string Id, ImmutableArray<string> Rows, TilePosition Spawn,
    ImmutableArray<FieldEncounterDefinition> Encounters)
{
    public const int TileSize = 16;
    public int Width => Rows[0].Length;
    public int Height => Rows.Length;
    public bool IsWalkable(TilePosition position) => IsWalkable(position.X, position.Y);
    public bool IsWalkable(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height && Rows[y][x] == '.';
}

public static class PrototypeField
{
    public static FieldMapDefinition StartingMap { get; } = new(
        "prototype:field.starting_area",
        [
            "###################",
            "#.................#",
            "#...###...........#",
            "#...#.........##..#",
            "#...#.............#",
            "#.................#",
            "#..........###....#",
            "#..##.............#",
            "#..##.....#.......#",
            "#.................#",
            "###################"
        ], new(2, 5), [new("prototype:encounter.goblin_wolf", new(8, 5))]);
}

// Map data and field state are independent of combat, equipment, input devices and rendering.
public sealed class FieldState
{
    private readonly bool[] defeated;
    private TilePosition contactOrigin;
    public FieldMapDefinition Map { get; }
    public TilePosition PlayerPosition { get; private set; }
    public string? PendingEncounterId { get; private set; }
    public ImmutableArray<FieldEncounterState> Encounters =>
        [.. Map.Encounters.Select((encounter, index) => new FieldEncounterState(encounter.Id, encounter.Position, defeated[index]))];

    public FieldState(FieldMapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Rows.IsDefaultOrEmpty || map.Rows[0].Length == 0 || map.Rows.Any(row => row.Length != map.Rows[0].Length))
            throw new ArgumentException("Field rows must form a nonempty rectangle.", nameof(map));
        if (!map.IsWalkable(map.Spawn) || map.Encounters.IsDefault ||
            map.Encounters.Any(encounter => !map.IsWalkable(encounter.Position)) ||
            map.Encounters.Select(encounter => encounter.Id).Distinct(StringComparer.Ordinal).Count() != map.Encounters.Length)
            throw new ArgumentException("Field spawn and encounters must be valid map positions with unique IDs.", nameof(map));
        Map = map;
        PlayerPosition = map.Spawn;
        defeated = new bool[map.Encounters.Length];
    }

    public FieldMoveResult TryMove(int dx, int dy)
    {
        if (PendingEncounterId is not null || !((dx == -1 || dx == 1) && dy == 0 || dx == 0 && (dy == -1 || dy == 1)))
            return new(false);
        var next = new TilePosition(PlayerPosition.X + dx, PlayerPosition.Y + dy);
        if (!Map.IsWalkable(next)) return new(false);
        var previous = PlayerPosition;
        PlayerPosition = next;
        for (var i = 0; i < Map.Encounters.Length; i++)
        {
            var encounter = Map.Encounters[i];
            if (!defeated[i] && encounter.Position == next)
            {
                contactOrigin = previous;
                PendingEncounterId = encounter.Id;
                return new(true, encounter.Id);
            }
        }
        return new(true);
    }

    internal void CompleteEncounter(string id, bool wasDefeated, bool retreat)
    {
        if (PendingEncounterId != id) throw new InvalidOperationException("No matching pending field encounter.");
        var index = 0;
        while (Map.Encounters[index].Id != id) index++;
        defeated[index] = wasDefeated;
        if (retreat) PlayerPosition = contactOrigin;
        PendingEncounterId = null;
    }
}
