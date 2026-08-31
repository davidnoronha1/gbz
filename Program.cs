static class PP
{
    public static void Main(string[] args)
    {
        var romPath = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "gb-test-roms/cpu_instrs/individual/10-bit ops.gb";
        bool useGfx = args.Any(a => a=="--gfx" || a=="--window" || a=="-w");
        var GB = new GBEmulator(romPath);
        if(useGfx){
            // PPU_GUIDE 5: coexist emulation + Raylib window via burst per frame
            GB.RunWithGfx();
        } else {
            GB.run();
        }
        Console.WriteLine("dafaq");
    }
}