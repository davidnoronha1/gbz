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

    // Step one CPU instruction + PPU (used by Gfx burst mode)
    public int Tick(){
        int cycles = cpu.Tick();
        dbg.IncrementPC();
        ppu.Step(cycles);
        return cycles;
    }

    // Run for ~one frame (70224 dots) – burst mode for Raylib loop (PPU_GUIDE 5)
    public void RunForDots(int dots){
        int acc=0;
        while(acc < dots){
            dbg.DebugTick();
            acc += Tick();
        }
    }

    public void run() {
        while (true) {
            dbg.DebugTick(); // here opcode is set
            Tick();
        }
    }

    // Non-blocking run for Gfx – steps one frame per Raylib tick
    public void RunWithGfx(){
        DisableDebug();
        GfxEntry.Run(this);
    }
}