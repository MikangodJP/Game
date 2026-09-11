using Godot;
using Phase1A.Visual.Presentation;

namespace Phase1A.Visual;

// Opt-in acceptance run for the Field control menu through real keyboard and pad events.
public partial class BattleScreen
{
    private async void StartMenuQa(string outputDirectory)
    {
        System.IO.Directory.CreateDirectory(outputDirectory);
        var report = System.IO.Path.Combine(outputDirectory, "menu-qa.txt");
        try
        {
            await Frames();
            Check(standalone is null && game.Mode == GameMode.Field, "menu QA starts on the live Field");
            var fieldSample = await PixelAt(184, 100);
            var fieldHints = await NonInkPixels(new Rect2I(0, 211, 320, 29));
            Check(fieldHints > 0, "Field movement hints start visible");

            FieldKey(Key.D, true);
            await Frames();
            Check(game.State.Field.PlayerPosition.X == 3, "held movement begins before opening the menu");
            await Press(Key.Tab);
            Check(game.Mode == GameMode.Menu && fieldScreen.Visible && menuScreen.Visible,
                "Tab gives the visible menu sole ownership over the still-visible Field");
            var menuPosition = game.State.Field.PlayerPosition;
            await FieldDelay(0.34);
            Check(game.State.Field.PlayerPosition == menuPosition, "opening clears held Field movement");
            FieldKey(Key.D, false);
            await Frames();
            Check(game.State.Field.PlayerPosition == menuPosition, "movement release cannot leak through the menu");
            Check(await NonInkPixels(new Rect2I(0, 211, 320, 29)) == 0, "Field movement hints hide while the menu is open");
            Check(await PixelAt(184, 100) == fieldSample, "Field pixels outside the menu remain visible and unchanged");
            await MenuCapture(outputDirectory, "menu-00-root", 1, rootPalette: true);

            await Press(Key.Up); await Press(Key.Left);
            Check(game.FieldMenu.SelectedIndex == 0, "root cursor clamps at the upper-left edge");
            await Press(Key.Right); await Press(Key.Right); await Press(Key.Right);
            Check(game.FieldMenu.SelectedIndex == 2, "root cursor clamps at the right edge");
            await Press(Key.Down); await Press(Key.Down);
            Check(game.FieldMenu.SelectedIndex == 5, "root cursor clamps at the lower edge");
            await Press(Key.Left); await Press(Key.Up);
            Check(game.FieldMenu.SelectedIndex == 1, "root movement preserves the logical column");

            await Press(Key.Enter);
            Check(game.FieldMenu.Depth == 2 && game.FieldMenu.BuildView(game.State.Player).Breadcrumb == "MAGIC",
                "Magic opens its child command frame");
            Check(game.FieldMenu.CurrentEntries.Select(entry => entry.Label)
                .SequenceEqual(["SPELLS", "INFORMATION", "BACK"]),
                "Field Magic contains only Spells, Information, and Back");
            await MenuCapture(outputDirectory, "menu-01-magic", 2);
            await MoveFieldMenuTo("INFORMATION"); await Press(Key.Enter);
            Check(game.FieldMenu.BuildView(game.State.Player).ActivePanel is { Kind: FieldMenuPanelKind.Info },
                "Magic Information is a passive explanatory panel");
            await MenuCapture(outputDirectory, "menu-02-information", 3, menuPalette: true);
            await Press(Key.Enter);
            Check(game.Mode == GameMode.Menu && game.FieldMenu.Depth == 2 &&
                game.FieldMenu.BuildView(game.State.Player).ActivePanel is null,
                "Enter dismisses Information without closing Magic or the Field menu");
            await Press(Key.Escape);
            Check(game.FieldMenu.Depth == 1 && game.FieldMenu.CurrentEntries[game.FieldMenu.SelectedIndex].Label == "MAGIC",
                "Escape then pops the child and restores the Magic cursor");
            await Press(Key.Escape);
            Check(game.Mode == GameMode.Field, "Escape at root closes to Field");

            await Press(Key.Tab); await MoveFieldMenuTo("STATUS"); await Press(Key.Enter);
            var status = game.FieldMenu.BuildView(game.State.Player).ActivePanel!;
            var stats = game.State.Player.EffectiveStats;
            Check(status.Kind == FieldMenuPanelKind.Status, "Status opens the dedicated read-only sheet");
            Check(status.Rows.Single(row => row.Label == "HP").Value == $"{game.State.Player.Hp}/{stats.MaxHp}" &&
                status.Rows.Single(row => row.Label == "MP").Value == $"{game.State.Player.Mp}/{stats.MaxMp}",
                "Status shows live HP and MP from the persistent player");
            Check(status.Rows.Single(row => row.Label == "STR").Value == stats.Strength.ToString() &&
                status.Rows.Single(row => row.Label == "DEF").Value == stats.Defense.ToString() &&
                status.Rows.Single(row => row.Label == "MAG").Value == stats.Magic.ToString() &&
                status.Rows.Single(row => row.Label == "RES").Value == stats.Resistance.ToString() &&
                status.Rows.Single(row => row.Label == "AGI").Value == stats.Agility.ToString(),
                "Status shows all five named combat/WIP effective stats without duplication");
            await MenuCapture(outputDirectory, "menu-03-status", 2);
            await Pad(JoyButton.B);
            Check(game.Mode == GameMode.Menu && game.FieldMenu.BuildView(game.State.Player).ActivePanel is null,
                "controller B dismisses a root panel before closing the menu");
            await Pad(JoyButton.B);
            Check(game.Mode == GameMode.Field, "a second controller B closes from the root");

            await Press(Key.Tab); await MoveFieldMenuTo("SYSTEM");
            Check(await PixelAt(109, 44) == new Color("000000"),
                "the System cursor keeps a black pixel gap after Actions");
            await Press(Key.Enter);
            Check(game.FieldMenu.Depth == 2 && game.FieldMenu.CurrentEntries.Select(entry => entry.Label)
                .SequenceEqual(["SETTINGS", "FOR TESTING", "BACK"]), "System opens its WIP child commands");
            await MenuCapture(outputDirectory, "menu-04-system", 2);
            await MoveFieldMenuTo("FOR TESTING"); await Press(Key.Enter);
            Check(game.FieldMenu.BuildView(game.State.Player).ActivePanel?.Lines[0] == "FOR TESTING IS NOT IMPLEMENTED YET.",
                "For Testing opens a visible placeholder panel");
            await MenuCapture(outputDirectory, "menu-05-for-testing", 3);
            await Pad(JoyButton.Back);
            Check(game.Mode == GameMode.Field && !menuScreen.Visible, "controller Back/View closes the menu from any depth");
            await FieldDelay(0.28);
            Check(game.State.Field.PlayerPosition == menuPosition, "closing cannot resume stale held movement");
            Check(await NonInkPixels(new Rect2I(0, 211, 320, 29)) == fieldHints, "closing restores the Field movement hints");
            await Press(Key.A);
            Check(game.State.Field.PlayerPosition.X == 2, "a fresh movement press works after closing");
            await Capture(outputDirectory, "menu-06-returned-field");

            await EnterFieldEncounter();
            var battleSelection = ui.Menu.SelectedIndex;
            var battleLog = ui.Session.MachineText;
            await Press(Key.Tab);
            Check(game.Mode == GameMode.Battle && ui.Mode == ScreenMode.Menu &&
                ui.Menu.SelectedIndex == battleSelection && ui.Session.MachineText == battleLog,
                "Tab is inert in Battle and cannot drive the existing battle menu");

            GD.Print($"MENU QA PASS: {qaChecks.Count} checks");
            System.IO.File.WriteAllLines(report, qaChecks.Append("PASS ALL"));
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error.ToString());
            System.IO.File.WriteAllLines(report, qaChecks.Append("FAIL " + error));
            GetTree().Quit(1);
        }
    }

    private async Task MoveFieldMenuTo(string label)
    {
        var index = game.FieldMenu.CurrentEntries.ToList().FindIndex(entry => entry.Label == label);
        Check(index >= 0, "field menu entry present: " + label);
        var row = index / game.FieldMenu.Columns;
        var column = index % game.FieldMenu.Columns;
        for (var i = 0; i < 8 && game.FieldMenu.SelectedRow > row; i++) await Press(Key.Up);
        for (var i = 0; i < 8 && game.FieldMenu.SelectedRow < row; i++) await Press(Key.Down);
        for (var i = 0; i < 8 && game.FieldMenu.SelectedColumn > column; i++) await Press(Key.Left);
        for (var i = 0; i < 8 && game.FieldMenu.SelectedColumn < column; i++) await Press(Key.Right);
        Check(game.FieldMenu.SelectedIndex == index, "field cursor reaches: " + label);
    }

    private async Task MenuCapture(
        string directory, string name, int expectedWindows,
        bool rootPalette = false, bool menuPalette = false)
    {
        fieldScreen.QueueRedraw();
        menuScreen.QueueRedraw();
        QueueRedraw();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        Check(frame.GetWidth() == 320 && frame.GetHeight() == 240, name + " native 320x240 viewport");
        Check(menuScreen.LastWindowBounds.Count == expectedWindows, name + " records every visible menu window");
        Check(menuScreen.LastWindowBounds[0] == new Rect2I(8, 8, 160, 54), name + " keeps the exact root geometry");
        foreach (var bounds in menuScreen.LastWindowBounds)
            Check(bounds.Position.X >= 0 && bounds.Position.Y >= 0 && bounds.End.X <= 320 && bounds.End.Y <= 240,
                name + " keeps a window inside the native viewport");
        if (rootPalette)
        {
            var black = new Color("000000");
            var white = new Color("ffffff");
            var root = menuScreen.LastWindowBounds[0];
            var cursorPixel = false;
            var rootPaletteOnly = true;
            for (var y = root.Position.Y; y < root.End.Y; y++)
                for (var x = root.Position.X; x < root.End.X; x++)
                {
                    var color = frame.GetPixel(x, y);
                    if (color != black && color != white) rootPaletteOnly = false;
                    if (x is >= 12 and < 20 && y is >= 30 and < 40 && color == white) cursorPixel = true;
                }
            Check(rootPaletteOnly, "root menu uses literal black and white only");
            Check(cursorPixel, "root selection is represented by a white cursor glyph");
        }
        if (menuPalette)
        {
            var black = new Color("000000");
            var white = new Color("ffffff");
            var disabled = new Color("7f7f7f");
            var paletteOnly = true;
            foreach (var bounds in menuScreen.LastWindowBounds)
                for (var y = bounds.Position.Y; y < bounds.End.Y; y++)
                    for (var x = bounds.Position.X; x < bounds.End.X; x++)
                    {
                        var color = frame.GetPixel(x, y);
                        if (color != black && color != white && color != disabled) paletteOnly = false;
                    }
            Check(paletteOnly, name + " keeps the documented menu palette");
        }
        var error = frame.SavePng(System.IO.Path.Combine(directory, name + ".png"));
        Check(error == Error.Ok, name + " captured");
    }

    private async Task<Color> PixelAt(int x, int y)
    {
        fieldScreen.QueueRedraw();
        menuScreen.QueueRedraw();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        return frame.GetPixel(x, y);
    }

    private async Task<int> NonInkPixels(Rect2I bounds)
    {
        fieldScreen.QueueRedraw();
        menuScreen.QueueRedraw();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        var ink = new Color("070a12");
        var count = 0;
        for (var y = bounds.Position.Y; y < bounds.End.Y; y++)
            for (var x = bounds.Position.X; x < bounds.End.X; x++)
                if (frame.GetPixel(x, y) != ink) count++;
        return count;
    }
}
