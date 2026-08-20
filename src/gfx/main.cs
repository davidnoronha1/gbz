using Raylib_cs;

class GfxEntry {
    static public void main() {
        Raylib.InitWindow(160 * 2, 144 * 2, "Game Boy EMU");
        
        while (!Raylib.WindowShouldClose()) {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.BLACK);
            Raylib.DrawText("Hello world", 0, 0, 20, Color.WHITE);
            Raylib.EndDrawing();
        }
    }
}