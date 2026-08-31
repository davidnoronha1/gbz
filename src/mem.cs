using Raylib_cs;

class Interrupts
{
    public bool enabled = false; // IME - gates whether any dispatch happens at all
    public byte mask = 0;  // IE (0xffff) - which sources are allowed to fire
    public byte flags = 0; // IF (0xff0f) - which sources currently have a pending request

    public const int VBlankBit = 0;
    public const int LCDStatBit = 1;
    public const int TimerBit = 2;
    public const int SerialBit = 3;
    public const int JoypadBit = 4;

    public bool vblank_enabled() { return (mask & (1 << VBlankBit)) != 0; }
    public bool lcd_stat_enabled() { return (mask & (1 << LCDStatBit)) != 0; }
    public bool timer_enabled() { return (mask & (1 << TimerBit)) != 0; }
    public bool serial_enabled() { return (mask & (1 << SerialBit)) != 0; }
    public bool joypad_enabled() { return (mask & (1 << JoypadBit)) != 0; }

    // For the PPU/timer/etc to call when a source wants to fire. Whether it
    // actually gets dispatched is still gated by `enabled` and `mask` - that's
    // the CPU's job (not yet implemented).
    public void Request(int bit) { flags |= (byte)(1 << bit); }
}

class STATE
{
    byte[] ROM;
    byte[] WRAM1 = new byte[4096];
    byte[] WRAM2 = new byte[4096];
    byte[] HRAM = new byte[0xfffe - 0xff80 + 1];
    public byte[] VRAM = new byte[0x9fff - 0x8000 + 1];
    byte[] OAM = new byte[0xfe9f - 0xfe00 + 1];
    byte[] IO = new byte[0xff7f - 0xff00 + 1];
    public Interrupts interrupts = new Interrupts();
    public Debugger? debug_hook = null;
    public bool had_invalid_access = false;
    string rom_path_ = "";

    // PPU-owned register state. The CPU can't write these directly - a
    // future PPU will update them as it steps through modes/scanlines.
    private byte _LY;
    public const ushort _STAT = 0xff41;
    public const ushort _LCDC = 0xff40;
    public const ushort _LYC = 0xff45;

    private void onLYorLYCupdate(byte value)
    {
        
        if (value == this.addrNoHook(0xff45))
        {
            this.addrNoHook(_STAT) |= (byte)(1 << 2);
            if ((this.addrNoHook(0xff41) & (1 << 6)) != 0 && (addrNoHook(_LCDC) & (1 << 7)) != 0)
            {
                interrupts.Request(Interrupts.LCDStatBit);
            }
        }
        else
        {
            this.addrNoHook(_STAT) &= unchecked((byte)~(1 << 2));
        }
    }

    public byte LY
    {
        get => _LY;
        set
        {
            onLYorLYCupdate(value);
            _LY = value;
        }
    }



    public STATE(string rom_path)
    {
        rom_path_ = rom_path;
        Console.WriteLine("READING ROM: {0}", rom_path);
        ROM = System.IO.File.ReadAllBytes(rom_path);
    }

    byte garbage = 0;

    public void reset()
    {
        Array.Clear(WRAM1);
        Array.Clear(WRAM2);
        Array.Clear(HRAM);
        Array.Clear(VRAM);
        Array.Clear(OAM);
        Array.Clear(IO);
        LY = 0;
        ROM = System.IO.File.ReadAllBytes(rom_path_);
    }

    // Raw storage access with no special-register semantics - used by read8/
    // write8 for the plumbing, and by the debugger to peek/poke memory
    // (including registers) without hardware restrictions getting in the way.
    public ref byte addrNoHook(ushort idx)
    {
        if (idx <= 0x7fff)
        {
            return ref ROM[idx];
        }

        if (idx >= 0xc000 && idx <= 0xcfff)
        {
            if (idx - 0xc000 >= WRAM1.Length)
            {
                Console.WriteLine("{0:X4} is in bounds for WRAM1 but does not fit", idx);
                return ref garbage;
            }
            return ref WRAM1[idx - 0xc000];
        }

        if (idx >= 0xd000 && idx <= 0xdfff)
        {
            if (idx - 0xd000 >= WRAM2.Length)
            {
                Console.WriteLine("{0:X4} is in bounds for WRAM1 but does not fit", idx);
                return ref garbage;
            }
            return ref WRAM2[idx - 0xd000];
        }

        if (idx >= 0xff80 && idx <= 0xfffe)
        {
            return ref HRAM[idx - 0xff80];
        }

        if (idx >= 0x8000 && idx <= 0x9fff)
        {
            return ref VRAM[idx - 0x8000];
        }

        if (idx >= 0xfe00 && idx <= 0xfe9f)
        {
            return ref OAM[idx - 0xfe00];
        }

        // -- IO --
        if (idx == 0xffff)
        {
            return ref interrupts.mask;
        }
        else if (idx == 0xff0f)
        {
            return ref interrupts.flags;
        }

        if (idx >= 0xff00 && idx <= 0xff7f)
        {
            return ref IO[idx - 0xff00];
        }

        Console.WriteLine("\x1b[1maccess to {0:x4} not implemented\x1b[0m", idx);
        had_invalid_access = true;
        return ref garbage;
    }

    public ref byte addr(ushort idx)
    {
        if (debug_hook != null)
            debug_hook.memAccessHook(idx);

        return ref addrNoHook(idx);
    }

    // CPU-facing accessors. Unlike addr()/addrNoHook(), these enforce the
    // hardware semantics that a plain array reference can't express: LY is
    // read-only, STAT's low 3 bits are PPU-owned, and writing DMA kicks off
    // an OAM copy.
    public byte read8(ushort idx)
    {
        if (debug_hook != null)
            debug_hook.memAccessHook(idx);

        if (idx == 0xff44) // LY
        {
            return LY;
        }

        if (idx == 0xff41) // STAT
        {
            return (byte)(IO[idx - 0xff00] | 0x80);
        }

        return addrNoHook(idx);
    }

    public void write8(ushort idx, byte value)
    {
        if (debug_hook != null)
            debug_hook.memAccessHook(idx);

        if (idx == 0xff44) // LY is read-only to the CPU
        {
            Console.WriteLine("--- write to LY! ---");
            return;
        }

        if (idx == STATE._LYC)
        {
            IO[idx - 0xff00] = value;
            onLYorLYCupdate(LY);
            return;
        }


        if (idx == 0xff41) // STAT - only the upper 5 bits (interrupt sources) are CPU-writable
        {
            IO[idx - 0xff00] = (byte)((IO[idx - 0xff00] & 0x07) | (value & 0xf8) | 0x80);
            return;
        }

        if (idx == 0xff46) // DMA - write triggers a 160-byte copy into OAM
        {
            IO[idx - 0xff00] = value;
            ushort src = (ushort)(value << 8);
            for (int i = 0; i < 0xa0; i++)
            {
                OAM[i] = addrNoHook((ushort)(src + i));
            }
            return;
        }

        addrNoHook(idx) = value;
    }
}
