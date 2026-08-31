using Raylib_cs;
using System.Numerics;

class GfxEntry {
    static readonly Color[] Palette = new Color[] {
        new Color(255,255,255,255), // 0 white
        new Color(170,170,170,255), // 1 light gray
        new Color( 85, 85, 85,255), // 2 dark gray
        new Color(  0,  0,  0,255), // 3 black
    };
    const int Scale = 3; // 160*3=480, 144*3=432

    public static void Run(GBEmulator emu){
        emu.DisableDebug();
        var thread = new System.Threading.Thread(()=>{
            while(true) emu.Tick();
        });
        thread.IsBackground = true;
        thread.Start();

        Raylib.InitWindow(160 * Scale, 144 * Scale, "gbz");
        Raylib.SetTargetFPS(60);

        Image img = Raylib.GenImageColor(160, 144, Color.BLACK);
        Texture2D tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);

        Color[] pixels = new Color[160*144];

        while (!Raylib.WindowShouldClose()) {
            byte[] fb = emu.ppu.fb;
            for(int i=0;i<pixels.Length;i++) pixels[i] = Palette[fb[i]&3];

            unsafe {
                fixed (Color* ptr = pixels) {
                    Raylib.UpdateTexture(tex, ptr);
                }
            }

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.BLACK);
            Raylib.DrawTextureEx(tex, new Vector2(0,0), 0, Scale, Color.WHITE);
            Raylib.EndDrawing();
        }
        Raylib.CloseWindow();
    }
}
