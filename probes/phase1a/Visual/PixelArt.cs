using Godot;

namespace Phase1A.Visual;

// Original hand-drawn grids for this disposable harness. Each occupied cell is
// one solid pixel at the internal resolution; there are no sampled textures.
public static class PixelArt
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    public const int GlyphAdvance = 6;

    public static void Text(CanvasItem canvas, string text, int x, int y, Color color, int scale = 1)
    {
        scale = Math.Max(1, scale);
        var originX = x;
        foreach (var character in text.ToUpperInvariant())
        {
            if (character == '\n')
            {
                x = originX;
                y += 8 * scale;
                continue;
            }
            var glyph = Glyphs.GetValueOrDefault(character, Glyphs['?']);
            for (var row = 0; row < GlyphHeight; row++)
                for (var column = 0; column < GlyphWidth; column++)
                    if (glyph[row * 6 + column] == '1')
                        canvas.DrawRect(new Rect2(x + column * scale, y + row * scale, scale, scale), color);
            x += GlyphAdvance * scale;
        }
    }

    public static void Goblin(CanvasItem canvas, int centerX, int baselineY, bool defeated = false) =>
        Creature(canvas, GoblinPixels, GoblinColors, centerX, baselineY, defeated);

    public static void Wolf(CanvasItem canvas, int centerX, int baselineY, bool defeated = false) =>
        Creature(canvas, WolfPixels, WolfColors, centerX, baselineY, defeated);

    private static void Creature(CanvasItem canvas, string[] pixels, Dictionary<char, Color> colors,
        int centerX, int baselineY, bool defeated)
    {
        if (defeated)
        {
            var dim = new Color("343b47");
            canvas.DrawRect(new Rect2(centerX - 6, baselineY - 1, 12, 1), dim);
            canvas.DrawRect(new Rect2(centerX - 3, baselineY - 2, 5, 1), dim);
            return;
        }
        var left = centerX - pixels.Max(row => row.Length) / 2;
        var top = baselineY - pixels.Length;
        for (var row = 0; row < pixels.Length; row++)
            for (var column = 0; column < pixels[row].Length; column++)
                if (colors.TryGetValue(pixels[row][column], out var color))
                    canvas.DrawRect(new Rect2(left + column, top + row, 1, 1), color);
    }

    private static readonly Dictionary<char, Color> GoblinColors = new()
    {
        ['o'] = new("242c29"), ['d'] = new("3d513e"), ['g'] = new("74815a"),
        ['h'] = new("a2ad73"), ['c'] = new("a58b54"), ['b'] = new("5e503b"),
        ['s'] = new("756344"), ['m'] = new("9da99c"), ['e'] = new("ecce84"),
        ['t'] = new("d5ccb0")
    };

    // Hunched hooded creature, long uneven ears, little tusk, short spear.
    // A narrow head and lopsided cape deliberately avoid a familiar mascot shape.
    private static readonly string[] GoblinPixels =
    [
        "..............oo....................",
        ".............obco..............o....",
        "............obccco............omo...",
        "...........obcccbo...........ommo...",
        "..........obcccbbbo..........ommo...",
        ".........obcccbbbboo..........oo....",
        "........obcccbbbddggo.........so....",
        "...oo..obcccbbddgghgo.........so....",
        "...ogooobccbbdgggghgoo.....oo..so....",
        "....ohgobcbbdgggghhhgoooooggo..so....",
        "....ogggobbddggghhhhggghgggo...so....",
        ".....ogggoddggghhhghggggooo....so....",
        "......oggodgggeoehgggooo......so....",
        ".......ooodgggooogggghgo......so....",
        "........obdggggggggggghgo.....so....",
        ".......obbdggggggoooooggo.....so....",
        "......obbbbdggggotogggooo.....so....",
        ".....obcccbbddgggooo.........so....",
        ".....obcccccbbdddgo..........so....",
        "....obcccccccbbdggo..........so....",
        "....obccccccccbogggo.........so....",
        "....obccccccbbboddggo........so....",
        "...obccccccbbbo.oddggo......ogggo..",
        "...obcccccbbbo..oddgggoooooggghgo..",
        "...obcccbbbbo....oddggggggghgoggo..",
        "...obccbbbbo......ooooggggooo.so....",
        "...obbbbbbo..........oooo....so....",
        "...obbbbboooggo..............so....",
        "....obbbooggggo..............so....",
        ".....oo.ogghgo.ogggo.........so....",
        ".......odgggo..ogghgo........so....",
        ".......odggo....ogghgo.......so....",
        "......odggo......ogggo.......so....",
        ".....odggo........oggo.......so....",
        "....odgggo........ogggo......oo....",
        "...odgggggo......odggggo...........",
        "...ogggggoo......oggggggo..........",
        "....oooooo........oooooo.........."
    ];

    private static readonly Dictionary<char, Color> WolfColors = new()
    {
        ['o'] = new("252d3b"), ['d'] = new("434e64"), ['g'] = new("7f8eaa"),
        ['h'] = new("b6c2cd"), ['e'] = new("ddb675"), ['t'] = new("e5d9b4")
    };

    // Low, alert quadruped facing left: tall ears, long snout, raised bushy tail.
    private static readonly string[] WolfPixels =
    [
        ".........o.......o..............................",
        "........ogo.....ogo.............................",
        "........ohgo...oggo.............................",
        ".......ogdhgo.ogdgo.............................",
        ".......ogddhgogddgo.............................",
        ".......ogdghhggdggo.......................oo....",
        "......ogghhhggggggo......................ogho...",
        ".....ogghhhhggggggo.....................oggho...",
        "....ogghhggggggggggoooooo...............ogghho...",
        "...ogghgeoggggggggggggggooooo.........ogghhgo...",
        "..ogghhgooogggghhhhhggggggggggooooo..ogghhhgo...",
        ".ogghhhggggggghhhhhhhhhhhggggggggggoogghhhgo....",
        "ooghhhhggggggggggghhhhhhhhhggggggggggghhhgo.....",
        "oooggggggggggggggggggggggggggggggggggghgo......",
        ".ooogggggggdddgggggggggggggggggggggggggo.......",
        "...ootgoogddddddggggggggggggggggggggddo........",
        ".....ooooodddddddddgggggggggggggdddddo.........",
        ".........oodddddddddddddddddddddddddo..........",
        "..........oddgoddddddddooooodddddddgo...........",
        "..........oddgo.oddddgo....odddooddggo..........",
        "..........oddgo..oddgo......oddo.oddgo..........",
        "..........odggo..odgo.......odgo..odgo..........",
        "..........odggo..odgo.......odgo..odgo..........",
        ".........odggo..odggo......odggo..odgo..........",
        "........odgggo..odggo.....odgggo..odgo..........",
        ".......oghhggo.oggggo....oghhggo.ogggo..........",
        ".......oooooo..ooooo....oooooo..ooooo..........",
        "................................................"
    ];

    // Independently drawn 5x7 forms. Slash characters only separate rows.
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        [' '] = "00000/00000/00000/00000/00000/00000/00000",
        ['A'] = "01110/10001/10001/11111/10001/10001/10001",
        ['B'] = "11110/10001/10001/11110/10001/10001/11110",
        ['C'] = "01111/10000/10000/10000/10000/10000/01111",
        ['D'] = "11100/10010/10001/10001/10001/10010/11100",
        ['E'] = "11111/10000/10000/11110/10000/10000/11111",
        ['F'] = "11111/10000/10000/11110/10000/10000/10000",
        ['G'] = "01111/10000/10000/10111/10001/10001/01110",
        ['H'] = "10001/10001/10001/11111/10001/10001/10001",
        ['I'] = "11111/00100/00100/00100/00100/00100/11111",
        ['J'] = "00111/00010/00010/00010/10010/10010/01100",
        ['K'] = "10001/10010/10100/11000/10100/10010/10001",
        ['L'] = "10000/10000/10000/10000/10000/10000/11111",
        ['M'] = "10001/11011/10101/10101/10001/10001/10001",
        ['N'] = "10001/11001/11001/10101/10011/10011/10001",
        ['O'] = "01110/10001/10001/10001/10001/10001/01110",
        ['P'] = "11110/10001/10001/11110/10000/10000/10000",
        ['Q'] = "01110/10001/10001/10001/10101/10010/01101",
        ['R'] = "11110/10001/10001/11110/10100/10010/10001",
        ['S'] = "01111/10000/10000/01110/00001/00001/11110",
        ['T'] = "11111/00100/00100/00100/00100/00100/00100",
        ['U'] = "10001/10001/10001/10001/10001/10001/01110",
        ['V'] = "10001/10001/10001/10001/10001/01010/00100",
        ['W'] = "10001/10001/10001/10101/10101/11011/10001",
        ['X'] = "10001/10001/01010/00100/01010/10001/10001",
        ['Y'] = "10001/10001/01010/00100/00100/00100/00100",
        ['Z'] = "11111/00001/00010/00100/01000/10000/11111",
        ['0'] = "01110/10001/10011/10101/11001/10001/01110",
        ['1'] = "00100/01100/00100/00100/00100/00100/01110",
        ['2'] = "01110/10001/00001/00010/00100/01000/11111",
        ['3'] = "11110/00001/00001/01110/00001/00001/11110",
        ['4'] = "00010/00110/01010/10010/11111/00010/00010",
        ['5'] = "11111/10000/10000/11110/00001/00001/11110",
        ['6'] = "01110/10000/10000/11110/10001/10001/01110",
        ['7'] = "11111/00001/00010/00010/00100/00100/00100",
        ['8'] = "01110/10001/10001/01110/10001/10001/01110",
        ['9'] = "01110/10001/10001/01111/00001/00001/01110",
        ['>'] = "00000/10000/01000/00100/01000/10000/00000",
        ['^'] = "00100/01010/10001/00000/00000/00000/00000",
        ['<'] = "00000/00001/00010/00100/00010/00001/00000",
        ['/'] = "00001/00001/00010/00100/01000/10000/10000",
        ['-'] = "00000/00000/00000/11111/00000/00000/00000",
        [':'] = "00000/00100/00100/00000/00100/00100/00000",
        ['!'] = "00100/00100/00100/00100/00100/00000/00100",
        ['?'] = "01110/10001/00001/00010/00100/00000/00100",
        ['.'] = "00000/00000/00000/00000/00000/00110/00110",
        [','] = "00000/00000/00000/00000/00110/00100/01000",
        ['['] = "01110/01000/01000/01000/01000/01000/01110",
        [']'] = "01110/00010/00010/00010/00010/00010/01110",
        ['('] = "00010/00100/01000/01000/01000/00100/00010",
        [')'] = "01000/00100/00010/00010/00010/00100/01000",
        ['+'] = "00000/00100/00100/11111/00100/00100/00000",
        ['%'] = "11001/11010/00010/00100/01000/01011/10011",
        ['='] = "00000/00000/11111/00000/11111/00000/00000",
        ['\''] = "00100/00100/00000/00000/00000/00000/00000"
    };
}
