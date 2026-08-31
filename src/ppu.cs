enum PPU_Mode
{
    DISABLED = 0b100,
    OAM_SCAN = 0b10,
    DRAW = 0b11,
    HBLANK = 0b00,
    VBLANK = 0b01
}

class PPU
{
    STATE S;
    int X; // each scan line is 456 dots
    PPU_Mode _mode = PPU_Mode.OAM_SCAN;
    PPU_Mode mode
    {
        get => _mode;
        set
        {
            // update MODE in STAT register
            S.addrNoHook(STATE._STAT) = (byte)((S.addrNoHook(STATE._STAT) & ~0x03) | ((byte)value & 0x03) | 0x80);

            // Don't fire STAT while LCD is off (DISABLED aliases HBLANK=0)
            if ((S.addrNoHook(STATE._LCDC) & (1 << 7)) == 0)
            {
                _mode = value;
                return;
            }

            if (value == PPU_Mode.OAM_SCAN && (S.addrNoHook(STATE._STAT) & (1 << 5)) != 0)
            {
                S.interrupts.Request(Interrupts.LCDStatBit);
            }
            else if (value == PPU_Mode.HBLANK && (S.addrNoHook(STATE._STAT) & (1 << 3)) != 0)
            {
                S.interrupts.Request(Interrupts.LCDStatBit);
            }
            else if (value == PPU_Mode.VBLANK && (S.addrNoHook(STATE._STAT) & (1 << 4)) != 0)
            {
                S.interrupts.Request(Interrupts.LCDStatBit);
            }

            _mode = value;
        }
    }

    public byte[] fb = new byte[160 * 144];

    public PPU(STATE S_)
    {
        this.S = S_;
        if ((S.addrNoHook(STATE._LCDC) & (1 << 7)) == 0)
        {
            S.addrNoHook(STATE._STAT) = (byte)((S.addrNoHook(STATE._STAT) & ~0x03) | 0x00 | 0x80);
            mode = PPU_Mode.DISABLED;
            X = 0;
        }
        else
        {
            S.addrNoHook(STATE._STAT) = (byte)((S.addrNoHook(STATE._STAT) & ~0x03) | ((byte)_mode & 0x03) | 0x80);
        }
    }

    const ushort _BGP = 0xff47;
    const ushort _SCY = 0xff42;
    const ushort _SCX = 0xff43;

    enum TileDataEntity
    {
        OBJECT,
        WINDOW,
        BACKGROUND
    }

    private Span<byte> VramSpan(ushort sidx, ushort eidx)
    {
        const ushort _vramStartIdx = 0x8000;
        return new Span<byte>(S.VRAM, sidx - _vramStartIdx, eidx - sidx);
    }

    private Span<byte> GetTileData(TileDataEntity e)
    {
        if (e == TileDataEntity.OBJECT)
        {
            return VramSpan(0x8000, 0x9000);
        } else
        {
            ushort sidx = 0x8000;
            bool tileDataSelect = (S.addrNoHook(STATE._LCDC) & (1 << 4)) != 0;
            if (!tileDataSelect)
            {
                sidx = 0x8800;
            }

            return VramSpan(sidx, (ushort)(sidx + 0x1000));
        }
    }

    private void RenderScanLine()
    {
        byte ly = S.LY;
        if (ly >= 144) return; // only visible lines

        byte lcdc = S.addrNoHook(STATE._LCDC);
        ushort tileMapBase = (lcdc & (1 << 3)) != 0 ? (ushort)0x9c00 : (ushort)0x9800;
        bool unsignedTileData = (lcdc & (1 << 4)) != 0;
        byte BGP = S.addrNoHook(_BGP);
        byte scy = S.addrNoHook(_SCY);
        byte scx = S.addrNoHook(_SCX);

        // Scrolling: background map is 256x256 wrapping (32x32 tiles)
        int bgY = (ly + scy) & 0xff;
        int tileRow = bgY / 8;
        int pixelRow = bgY % 8;

        Span<byte> tileData = GetTileData(TileDataEntity.BACKGROUND);

        for (int x = 0; x < 160; x++)
        {
            int bgX = (x + scx) & 0xff;
            int tileCol = bgX / 8;
            int px = bgX % 8;

            ushort mapAddr = (ushort)(tileMapBase + tileRow * 32 + tileCol);
            byte tileIdx = S.addrNoHook(mapAddr);

            int tileOffset;
            if (!unsignedTileData) {
                // 0x8800 signed: physical 0x9000 + (sbyte)tileIdx*16, span base is 0x8800
                // offset = 0x800 + (sbyte)tileIdx*16 == ((sbyte)tileIdx + 128)*16
                // https://gbdev.io/pandocs/Tile_Data.html?highlight=8800#vram-tile-data
                // tile indices reversed in 2nd addressing mode
                tileIdx = (byte)(tileIdx < 128 ? tileIdx + 128 : tileIdx - 128);
            }

             tileOffset = tileIdx * 16 + pixelRow * 2;

            // each tile is encoded in 2 bytes
            byte b0 = tileData[tileOffset];
            byte b1 = tileData[tileOffset + 1];

// https://gbdev.io/pandocs/Tile_Data.html#vram-tile-data
            int bit = 7 - px;
            int colorIndex = (((b1 >> bit) & 1) << 1) | ((b0 >> bit) & 1);
            byte shade = (byte)((BGP >> (colorIndex * 2)) & 0x03);
            fb[ly * 160 + x] = shade;
        }
    }

    public void Step(int C)
    {
        if ((S.addrNoHook(STATE._LCDC) & (1 << 7)) == 0)
        {
            X = 0;
            // suppress LYC STAT interrupt while LCD off, but still update coincidence flag
            // byte statSave = S.addrNoHook(STATE._STAT);
            // S.addrNoHook(STATE._STAT) &= unchecked((byte)~(1 << 6));
            S.LY = 0;
            // S.addrNoHook(STATE._STAT) = (byte)((S.addrNoHook(STATE._STAT) & ~0x40) | (statSave & 0x40));
            // S.addrNoHook(STATE._STAT) = (byte)((S.addrNoHook(STATE._STAT) & ~0x03) | 0x00 | 0x80);
            mode = PPU_Mode.DISABLED;
            return;
        }
        if (_mode == PPU_Mode.DISABLED)
        {
            // first tick after LCD re-enable: start at OAM scan
            X = 0;
            mode = PPU_Mode.OAM_SCAN;
        }
        X += C;

        switch (mode)
        {
            case PPU_Mode.OAM_SCAN:
                if (X >= 80)
                {
                    X -= 80; // reset counter for next node
                    mode = PPU_Mode.DRAW;
                }
                break;
            case PPU_Mode.DRAW:
                if (X >= 172)
                {
                    X -= 172;
                    mode = PPU_Mode.HBLANK;
                    RenderScanLine();
                }
                break;
            case PPU_Mode.HBLANK:
                if (X >= 204)
                {
                    X -= 204;
                    S.LY += 1;
                    if (S.LY == 144)
                    {
                        mode = PPU_Mode.VBLANK;
                        S.interrupts.Request(Interrupts.VBlankBit);
                    }
                    else
                    {
                        mode = PPU_Mode.OAM_SCAN;
                    }
                }
                break;
            case PPU_Mode.VBLANK:
                if (X >= 456)
                {
                    X -= 456;
                    S.LY += 1;
                    if (S.LY > 153)
                    {
                        S.LY = 0;
                        mode = PPU_Mode.OAM_SCAN;
                    }
                }
                break;
            default:
                break;
        }
    }
}