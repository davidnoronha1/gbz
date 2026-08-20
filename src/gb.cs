class GBEmulator {
    STATE state;
    CPU cpu;
    Debugger dbg;
    public GBEmulator(string rom) {
        state = new STATE(rom);
        cpu = new CPU(state);
        // state.setDebugger(dbg);
        dbg = new Debugger(state, cpu, rom);
    }

    public void run() {
        while (true) {
            dbg.DebugTick(); // here opcode is set
            int cycles = cpu.Tick();
            dbg.IncrementPC();
            // TODO: ppu.Step(cycles) once the PPU exists (see IMPL.md)
        }
    }
}