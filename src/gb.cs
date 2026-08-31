class GBEmulator {
    public readonly STATE state;
    public readonly CPU cpu;
    public readonly PPU ppu;
    public readonly Debugger dbg;
    public GBEmulator(string rom) {
        state = new STATE(rom);
        cpu = new CPU(state);
        ppu = new PPU(state);
        // state.setDebugger(dbg);
        dbg = new Debugger(state, cpu, rom);
    }

    public void DisableDebug(){ dbg.SetNonInteractive(); }

    public int Tick(){
        dbg.DebugTick();
        int cycles = cpu.Tick();
        ppu.Step(cycles);
        return cycles;
    }

    public void run() {
        while (true) {
            Tick();
        }
    }

    public void RunWithGfx(){
        GfxEntry.Run(this);
    }
}