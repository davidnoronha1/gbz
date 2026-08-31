# PPU Implementation Guide

A step-by-step plan for building the Game Boy PPU on top of the existing CPU
and memory (`src/cpu.cs`, `src/mem.cs`). Written so each step produces
something you can actually see or test before moving to the next.

Do the steps in order. Don't try to build sprites + window + scrolling +
timing all at once — get a static background rendering first, then layer
timing accuracy, then sprites, then window, then the fine details.

## References

- [Pan Docs - Rendering](https://gbdev.io/pandocs/Rendering.html) — the
  authoritative reference for all of this
- [Pan Docs - LCDC](https://gbdev.io/pandocs/LCDC.html), [STAT](https://gbdev.io/pandocs/STAT.html), [Palettes](https://gbdev.io/pandocs/Palettes.html)
- [Pan Docs - Tile Data](https://gbdev.io/pandocs/Tile_Data.html), [Tile Maps](https://gbdev.io/pandocs/Tile_Maps.html)
- [Pan Docs - OAM](https://gbdev.io/pandocs/OAM.html)
- [gbdev PPU timing writeup (ultimate reference)](https://gbdev.io/pandocs/pixel_fifo.html) — read this once you have a naive scanline renderer working and want to go cycle-accurate
- [Gameboy Emulator Development Guide - PPU chapter](http://gbdev.gg8.se/wiki/articles/Video_Display) (mirror of an older but very clear writeup)
- Blargg's `dmg-acid2` test ROM — the standard visual correctness test for
  background/window/sprite rendering once you're far enough along
  (https://github.com/mattcurrie/dmg-acid2)
- [gekkio's mealybug-tearoom-tests](https://github.com/mattcurrie/mealybug-tearoom-tests) — for PPU timing edge cases, much later

## 0. What already exists

Don't rebuild these — read them first so you know what to hook into:

- `STATE.LY`, `STATE.SetLY()` and `STATE.SetSTATHardwareBits()` in
  `src/mem.cs` — LY (current scanline) and the low 3 bits of STAT (mode +
  coincidence flag) are already carved out as PPU-owned. The CPU's
  `read8`/`write8` already treat LY as read-only and mask STAT so the CPU
  can only touch the upper 5 bits (interrupt-enable bits). Your PPU should
  call these setters instead of writing STATE's arrays directly.
- `STATE.VRAM` (0x8000-0x9fff) and `STATE.OAM` (0xfe00-0xfe9f) — already
  backed by byte arrays, readable via `addrNoHook`.
- `STATE.interrupts.Request(bit)` — call this with `Interrupts.VBlankBit` or
  `Interrupts.LCDStatBit` when the PPU needs to fire an interrupt. The
  actual dispatch (checking IME/IE and jumping to the vector) is the CPU's
  job — you just need to raise the request bit.
- `CPU.Tick()` returns the number of T-cycles (4 per M-cycle) the last
  instruction took. `GBEmulator.run()` in `src/gb.cs` already has the
  comment `// TODO: ppu.Step(cycles) once the PPU exists` marking exactly
  where to plug in.
- IO registers not yet special-cased (LCDC 0xff40, SCY/SCX 0xff42/43,
  BGP/OBP0/OBP1 0xff47-49, WY/WX 0xff4a/4b, etc.) currently live as plain
  bytes in `STATE.IO` and are read/written with no special behavior. That's
  fine — the PPU can read them directly with `addrNoHook`. You generally
  don't need CPU-side special-casing for these (unlike LY/STAT), since
  they're just "settings" the CPU pokes and the PPU reads.

## 1. New file: `src/ppu.cs`

Create a `PPU` class, constructed with a `STATE` reference, the same way
`CPU` is. Give it:

```csharp
class PPU {
    STATE state;
    int dot; // cycle counter within the current line (0..455)
    public byte[] framebuffer = new byte[160 * 144]; // one color index (0-3) per pixel

    public PPU(STATE state) { this.state = state; }

    public void Step(int cycles) { /* section 2 */ }
}
```

Wire it into `GBEmulator`:

```csharp
class GBEmulator {
    STATE state;
    CPU cpu;
    PPU ppu;
    Debugger dbg;
    public GBEmulator(string rom) {
        state = new STATE(rom);
        cpu = new CPU(state);
        ppu = new PPU(state);
        dbg = new Debugger(state, cpu, rom);
    }

    public void run() {
        while (true) {
            dbg.DebugTick();
            int cycles = cpu.Tick();
            dbg.IncrementPC();
            ppu.Step(cycles);
        }
    }
}
```

## 2. Mode timing state machine (no pixels yet)

Before drawing anything, get the mode/LY timing right, since STAT and the
VBlank/STAT interrupts depend on it and test ROMs check it early.

The PPU cycles through 4 modes per frame, driven by a dot counter:

- **Mode 2 (OAM scan)** — 80 dots, at the start of each visible line (LY 0-143)
- **Mode 3 (Drawing)** — ~172-289 dots (start simple: fix it at 172), right after mode 2
- **Mode 0 (HBlank)** — remainder of the 456-dot line after mode 3 ends
- **Mode 1 (VBlank)** — 10 full lines (LY 144-153), 456 dots each, after LY 143's HBlank

One full frame = 154 lines x 456 dots = 70224 dots.

Implementation:

```csharp
public void Step(int cycles) {
    dot += cycles;

    switch (mode) {
        case 2: // OAM scan
            if (dot >= 80) { dot -= 80; mode = 3; }
            break;
        case 3: // Drawing
            if (dot >= 172) { dot -= 172; mode = 0; RenderScanline(); EnterHBlank(); }
            break;
        case 0: // HBlank
            if (dot >= 204) {
                dot -= 204;
                state.SetLY((byte)(state.LY + 1));
                if (state.LY == 144) { mode = 1; EnterVBlank(); }
                else { mode = 2; }
                UpdateCoincidence();
            }
            break;
        case 1: // VBlank
            if (dot >= 456) {
                dot -= 456;
                state.SetLY((byte)(state.LY + 1));
                if (state.LY > 153) { state.SetLY(0); mode = 2; }
                UpdateCoincidence();
            }
            break;
    }

    state.SetSTATHardwareBits(mode, coincidence);
}
```

This is a simplified fixed-length version (real hardware's mode 3 length
varies with sprites/scroll — see step 8). It's good enough to pass basic
timing tests and get picture output.

Details to get right here:

- **LYC=LY coincidence**: compare `state.LY` against IO register 0xff45
  (LYC) after every LY change; set the coincidence flag STAT expects.
- **STAT interrupt**: STAT's upper bits (already CPU-writable via
  `write8`) select which conditions (mode 0/1/2 entry, or LYC match) should
  raise `Interrupts.LCDStatBit`. Read those enable bits from IO 0xff41
  (`addrNoHook`, since the low 3 bits are masked out for the CPU but the
  PPU should read/write the real underlying byte — or keep your own copy)
  when you enter a new mode or get a coincidence match, and call
  `state.interrupts.Request(Interrupts.LCDStatBit)` if enabled.
- **VBlank interrupt**: on entering mode 1 (LY 144), always call
  `state.interrupts.Request(Interrupts.VBlankBit)` — this one isn't gated
  by STAT config, only by IE/IME (the CPU's job).
- **LCD off (LCDC bit 7 = 0)**: while off, the PPU should stay in mode 0
  with LY forced to 0 and shouldn't tick. Handle this before anything else
  in `Step()` for now (just `return` early); you can refine later.

At this point you can sanity check via `dbg.cs` or a print in `Step()` that
LY advances at the right rate and mode transitions happen, without drawing
a single pixel yet.

## 3. Background rendering (scanline-at-a-time, no scrolling yet)

Now make `RenderScanline()` (called once per line at end of mode 3) fill
one row of `framebuffer` from tile data. Start with SCX = SCY = 0 to keep
it simple — plain background, no scrolling.

Registers involved (all in `STATE.IO`, read via `addrNoHook`):

- **LCDC (0xff40)** bit 3: BG tile map select (0 = 0x9800, 1 = 0x9c00).
  Bit 4: BG/window tile data select (1 = 0x8000 unsigned, 0 = 0x8800
  signed — this one's a classic gotcha, see Pan Docs "Tile Data").
- **BGP (0xff47)**: 4x 2-bit entries mapping color index (0-3, from tile
  data) to actual shade. `shade = (BGP >> (colorIndex * 2)) & 0b11`.

For a given scanline `LY`:

1. The background tile map is a 32x32 grid of tile indices. The row you
   need is `LY / 8` (tile row), and within that tile, pixel row `LY % 8`.
2. For each of the 160 output pixels `x` in 0..159, the background pixel
   is at column `x / 8`, and `x % 8` within the tile.
3. Look up the tile index at `tileMapBase + tileRow*32 + tileCol`.
4. Fetch that tile's 2 bytes of pixel-row data. Tile data is 16 bytes per
   tile (2 bytes per row of 8 pixels, bitplane-encoded): byte0 = low bit of
   each pixel's color, byte1 = high bit. For pixel column `px` (0-7,
   left-to-right = bit 7 down to bit 0):
   ```
   colorIndex = ((byte1 >> (7 - px)) & 1) << 1 | ((byte0 >> (7 - px)) & 1)
   ```
5. Map `colorIndex` through BGP to get the shade, store it in
   `framebuffer[LY * 160 + x]`.

Get this rendering a static, non-scrolled background correctly (test with
a homebrew ROM or just hardcode some tile data) before adding SCX/SCY.

## 4. Hook up scrolling (SCX/SCY)

- SCY (0xff42), SCX (0xff43): add these as offsets before the tile-map
  lookup. The background map wraps (32x32 tiles = 256x256 pixels), so:
  ```
  bgY = (LY + SCY) & 0xff;
  bgX = (x + SCX) & 0xff;
  tileRow = bgY / 8; tileCol = bgX / 8;
  pixelRow = bgY % 8; pixelCol = bgX % 8;
  ```

## 5. Display the framebuffer

Wire `src/gfx/main.cs` (currently a "Hello world" stub) up to actually
draw `ppu.framebuffer`. Simplest approach:

- Map the 4 shade values (0-3) to grayscale colors (e.g. white, light
  gray, dark gray, black).
- Each frame (once per VBlank — i.e. when `LY` transitions to 144), copy
  `framebuffer` into a Raylib `Texture2D` (or just draw 160x144
  `DrawPixel` calls scaled up) and present it.
- You'll need to restructure `GBEmulator.run()` and `GfxEntry.main()` so
  the emulation loop and the Raylib window loop coexist — either run the
  emulator on a background thread and have the render loop poll the
  framebuffer, or step the emulator once per Raylib frame in bursts of
  ~70224 dots (one GB frame ≈ 1/59.7 sec).

This is a good checkpoint: run a real ROM (even without sprites/window)
and see *something* recognizable on screen.

## 6. Window layer

- LCDC bit 5 enables the window; bit 6 selects its tile map (0x9800 vs
  0x9c00, same encoding as BG but a separate bit).
- WY (0xff4a) / WX (0xff4b): the window is drawn starting at screen
  position `(WX - 7, WY)`. Once `LY >= WY` and window is enabled, pixels
  with `x >= WX - 7` come from the window tile map instead of the
  background, using their own internal line counter (the window has its
  own Y counter that only increments on lines where it was actually drawn
  — don't just reuse `LY - WY`, that breaks when WY/WX change mid-frame).
- Window always uses the same tile *data* area as BG (LCDC bit 4), just a
  potentially different tile *map* (bit 6).

## 7. Sprites (OAM)

This is the biggest chunk of remaining work.

- **OAM entries**: 40 entries x 4 bytes each in `STATE.OAM` (0xfe00-0xfe9f):
  Y position (+16 offset), X position (+8 offset), tile index, attributes
  (priority, Y-flip, X-flip, palette).
- **Per-scanline OAM scan (mode 2)**: during the 80-dot OAM scan, collect
  up to 10 sprites whose Y range covers the current `LY` (accounting for
  8x8 vs 8x16 mode from LCDC bit 2). Real hardware does this incrementally
  over the 80 dots; for a first pass it's fine to just do it all at once
  when mode 2 starts.
- **Drawing**: for each of the 160 columns, check the collected sprites
  (in priority order — lower X wins ties, then OAM index) for one that
  covers this column, look up its tile data the same bitplane way as BG
  tiles (sprites always use the 0x8000 unsigned addressing, ignoring LCDC
  bit 4), apply X/Y flip, map through OBP0/OBP1 (0xff48/49) instead of
  BGP, and respect the priority bit (behind-BG-color-1-3 vs always in
  front) and color index 0 = transparent.
- Get plain sprites working first (no flip, no priority, 8x8 only), then
  add flip, 8x16 mode, and priority.

## 8. Timing accuracy pass (optional, do last)

Once background + window + sprites are visually correct, if you want
cycle-accurate timing (needed for some test ROMs and demos that race the
raster):

- Mode 3's length isn't fixed at 172 — it's `172 + scroll penalty (SCX %
  8) + sprite fetch penalties (roughly 6-11 dots per sprite drawn on that
  line) + window fetch penalty`. See the [pixel FIFO doc](https://gbdev.io/pandocs/pixel_fifo.html).
- Implementing an actual pixel FIFO (background FIFO + sprite FIFO,
  pushing one pixel per dot) is the "correct" way to get this exactly
  right, and is what the reference doc above describes. This is a
  significant rewrite of step 3/7's scanline-at-once approach — treat it
  as a distinct project once the simple version works and you want to
  chase remaining test-ROM failures or visual glitches.
- Validate against `dmg-acid2` (pixel-level correctness) and the
  mealybug-tearoom-tests (timing/glitch behavior) once you get here.

## Suggested order of work / milestones

1. Mode timing + STAT/VBlank interrupts firing at the right times, no
   pixels (section 2).
2. Static, unscrolled background renders correctly to `framebuffer`
   (section 3).
3. Scrolling works (section 4).
4. Something is visible in a real window via Raylib (section 5) — first
   time you can *look* at a real ROM running.
5. Window layer (section 6).
6. Sprites, basic then full-featured (section 7).
7. `dmg-acid2` passes.
8. Cycle-accurate mode-3 timing / pixel FIFO, if you want to chase it
   further (section 8).

Update `notes.md`'s progress checklist as you go, the same way it's used
for CPU test ROMs.
