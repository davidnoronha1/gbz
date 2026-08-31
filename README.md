# gbz

A Game Boy emulator written in C#, built from scratch as a learning project.

## Status

- **CPU** — done. Cycle-accurate SM83 core (`src/cpu.cs`), driven off a
  generated opcode table (`src/opcodes.json`). Passes Blargg's `cpu_instrs`
  tests 1, 3–9 (see `notes.md` for the checklist; 2, 10, 11 still open).
- **Memory** — done. `src/mem.cs` implements the address space (ROM, WRAM,
  HRAM, VRAM, OAM, IO) plus interrupt registers (IE/IF) and the
  hardware-restricted semantics for LY/STAT/DMA that the PPU will drive.
- **PPU** — not started. See `PPU_GUIDE.md` for the implementation plan.
- **Graphics window** — stub only (`src/gfx/main.cs`), a Raylib window that
  draws "Hello world". Not yet wired into the emulator loop.
- **Cartridge/MBC, timer, joypad, APU** — not started.

## Layout

```
Program.cs          entry point, picks a ROM and runs the emulator
src/gb.cs           GBEmulator - owns STATE/CPU/Debugger, runs the main loop
src/cpu.cs          SM83 CPU core, decode + execute, cycle table
src/mem.cs          STATE - the address space, interrupts, register plumbing
src/dbg.cs          Debugger - opcode logging / test-ROM harness support
src/opcodes.json    Opcode metadata (mnemonic, operands, length, cycles, flags)
src/gfx/main.cs     Raylib window stub (not yet connected to the emulator)
gb-test-roms/       Blargg's test ROMs, used to validate CPU behavior
```

## Running

```
dotnet run -- <path-to-rom.gb>
```

If no ROM path is given, it defaults to one of the `cpu_instrs` test ROMs
under `gb-test-roms/`.

## Requirements

- .NET 7 SDK
- Raylib-cs (restored automatically via NuGet, see `gbz.csproj`)

## References

See `notes.md` for links used while building the CPU, and `PPU_GUIDE.md` for
the plan and references for the PPU.
