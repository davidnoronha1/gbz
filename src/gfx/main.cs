using Raylib_cs;

class GfxEntry {
    // PPU shade 0-3 → grayscale (white → black) after BGP mapping
    static readonly Color[] Palette = new Color[] {
        new Color(255,255,255,255), // 0 white
        new Color(170,170,170,255), // 1 light gray
        new Color( 85, 85, 85,255), // 2 dark gray
        new Color(  0,  0,  0,255), // 3 black
    };
    const int Scale = 3; // 160*3=480, 144*3=432
    const int DotsPerFrame = 70224; // 154*456

    static public void main() {
        // default entry – launch demo ROM via Gfx
        var emu = new GBEmulator("gb-test-roms/cpu_instrs/individual/01-special.gb");
        Run(emu);
    }

    // PPU_GUIDE 5: bursts of ~70224 dots per Raylib frame (~59.7Hz), or background-thread poll
    static public void Run(GBEmulator emu){
        emu.DisableDebug();
        Raylib.InitWindow(160 * Scale, 144 * Scale, "gbz");
        Raylib.SetTargetFPS(60);

        while (!Raylib.WindowShouldClose()) {
            // burst: one GB frame per Raylib frame
            emu.RunForDots(DotsPerFrame);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.BLACK);
            // Option A: DrawRectangle per pixel scaled
            for(int y=0;y<144;y++){
                for(int x=0;x<160;x++){
                    byte shade = emu.ppu.fb[y*160+x];
                    if(shade>3) shade=0;
                    Raylib.DrawRectangle(x*Scale, y*Scale, Scale, Scale, Palette[shade]);
                }
            }
            // Alternative: Texture update would be faster, but DrawRectangle is simplest for Scanline PPU
            Raylib.EndDrawing();
        }
        Raylib.CloseWindow();
    }

    // Background-thread variant (guide alternative) – polls fb
    static public void RunWithThread(GBEmulator emu){
        emu.DisableDebug();
        var thread = new System.Threading.Thread(()=>{
            while(true) emu.Tick();
        });
        thread.IsBackground = true;
        thread.Start();
        Raylib.InitWindow(160*Scale,144*Scale,"gbz (threaded)");
        Raylib.SetTargetFPS(60);
        while(!Raylib.WindowShouldClose()){
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.BLACK);
            // copy fb to avoid tearing
            byte[] copy = (byte[])emu.ppu.fb.Clone();
            for(int y=0;y<144;y++) for(int x=0;x<160;x++) {
                Raylib.DrawRectangle(x*Scale,y*Scale,Scale,Scale,Palette[copy[y*160+x]&3]);
            }
            Raylib.EndDrawing();
        }
        Raylib.CloseWindow();
    }
}