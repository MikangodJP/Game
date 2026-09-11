using Godot;
using Phase1A.Visual.Presentation;
using Phase1A.World;

namespace Phase1A.Visual;

// Read-only field rendering. Movement/collision and encounter flags live in world state.
public partial class FieldScreen : Node2D
{
    public GameController Game { get; set; } = null!;
    private static readonly Color Ink = new("070a12"), Ground = new("182725"),
        Grass = new("304337"), Rock = new("536b71"), RockLight = new("84958d"),
        Paper = new("d6d2ae"), Gold = new("dfb963"), Red = new("c86959"),
        Green = new("9eaf74"), Blue = new("819eb5");

    public override void _Draw()
    {
        Fill(0, 0, 320, 240, Ink);
        var field = Game.State.Field;
        var player = Game.State.Player;
        var stats = player.EffectiveStats;
        Text("STARTING GLADE", 8, 6, Gold);
        Text($"HP {player.Hp}/{stats.MaxHp}", 8, 19, player.Hp < 20 ? Red : Green);
        Text($"MP {player.Mp}/{stats.MaxMp}", 104, 19, Blue);
        Text(field.Encounters.All(e => e.Defeated) ? "AREA CLEAR" : "ENEMY NEARBY", 236, 19, Paper);

        for (var y = 0; y < field.Map.Height; y++)
            for (var x = 0; x < field.Map.Width; x++)
            {
                var px = 8 + x * FieldMapDefinition.TileSize;
                var py = 32 + y * FieldMapDefinition.TileSize;
                Fill(px, py, 16, 16, Ground);
                if (!field.Map.IsWalkable(x, y))
                {
                    Fill(px + 1, py + 3, 14, 12, Ink);
                    Fill(px + 2, py + 2, 12, 11, Rock);
                    Fill(px + 4, py + 1, 8, 2, RockLight);
                    Fill(px + 3, py + 4, 3, 1, RockLight);
                    Fill(px + 9, py + 9, 4, 2, Grass);
                }
                else
                {
                    // Fixed integer decoration; never consumes simulation randomness.
                    var offset = (x * 3 + y * 5) % 9;
                    Fill(px + 2 + offset, py + 10, 1, 2, Grass);
                    Fill(px + 3 + offset, py + 9, 1, 1, Grass);
                }
            }
        foreach (var encounter in field.Encounters.Where(e => !e.Defeated))
            DrawToken(encounter.Position, enemy: true);
        DrawToken(field.PlayerPosition, enemy: false);
        if (Game.Mode != GameMode.Menu)
        {
            Text("WASD/ARROWS MOVE   E/ENTER EQUIPMENT", 8, 215, Paper);
            Text("TOUCH ENEMY TO BATTLE", 8, 228, RockLight);
        }

        if (Game.Mode == GameMode.GameOver)
        {
            Fill(37, 81, 246, 78, Rock);
            Fill(38, 82, 244, 76, Ink);
            PixelArt.Text(this, "DEFEAT", 124, 94, Red, 2);
            Text("YOUR JOURNEY ENDS HERE.", 94, 120, Paper);
            Text("R / START: NEW GAME", 106, 141, Gold);
        }
    }

    private void DrawToken(TilePosition position, bool enemy)
    {
        var x = 8 + position.X * FieldMapDefinition.TileSize;
        var y = 32 + position.Y * FieldMapDefinition.TileSize;
        // Two original tiny silhouettes: a blue-cloaked adventurer and an ear-pronged goblin.
        Fill(x + 3, y + 14, 11, 1, Ink);
        if (enemy)
        {
            Fill(x + 1, y + 3, 3, 4, Green); Fill(x + 12, y + 3, 3, 4, Green);
            Fill(x + 4, y + 2, 8, 7, Grass); Fill(x + 4, y + 3, 8, 4, Green);
            Fill(x + 5, y + 5, 2, 1, Ink); Fill(x + 9, y + 5, 2, 1, Ink);
            Fill(x + 6, y + 8, 6, 5, Rock); Fill(x + 3, y + 9, 3, 3, Green);
            Fill(x + 4, y + 13, 3, 2, Grass); Fill(x + 10, y + 13, 3, 2, Grass);
            Fill(x + 13, y + 8, 1, 6, Gold);
        }
        else
        {
            Fill(x + 5, y + 1, 7, 3, Gold); Fill(x + 4, y + 3, 9, 2, Gold);
            Fill(x + 6, y + 5, 5, 3, Paper); Fill(x + 10, y + 5, 1, 1, Ink);
            Fill(x + 5, y + 8, 7, 4, Blue); Fill(x + 3, y + 10, 11, 3, Blue);
            Fill(x + 5, y + 13, 3, 2, Rock); Fill(x + 10, y + 13, 3, 2, Rock);
        }
    }

    private void Fill(int x, int y, int width, int height, Color color) =>
        DrawRect(new Rect2(x, y, width, height), color);
    private void Text(string text, int x, int y, Color color) => PixelArt.Text(this, text, x, y, color);
}
