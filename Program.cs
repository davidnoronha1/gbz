static class PP
{

    public static void Main(string[] args)
    {

        var romPath = args.Length > 0 ? args[0] : "gb-test-roms/cpu_instrs/individual/10-bit ops.gb";
        var GB = new GBEmulator(romPath);
        GB.run();
        Console.WriteLine("dafaq");
    }
}