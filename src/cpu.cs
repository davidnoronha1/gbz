struct Flags
{
    public byte byte_value = 0x00;

    public bool zero
    {
        get => (byte_value & 1 << 7) != 0;
        set
        {
            if (value)
                byte_value |= (byte)(1 << 7);
            else
            {
                byte mod = 1 << 7;
                byte_value &= (byte)~(mod);
            }
        }
    }

    public bool negative
    {
        get => (byte_value & 1 << 6) != 0;
        set
        {
            if (value)
                byte_value |= (byte)(1 << 6);
            else
            {
                byte mod = 1 << 6;
                byte_value &= (byte)~(mod);
            }
        }
    }

    public bool half_carry
    {
        get => (byte_value & 1 << 5) != 0;
        set
        {
            if (value)
                byte_value |= (byte)(1 << 5);
            else
            {
                byte mod = 1 << 5;
                byte_value &= (byte)~(mod);
            }
        }
    }

    public bool carry
    {
        get => (byte_value & 1 << 4) != 0;
        set
        {
            if (value)
                byte_value |= (byte)(1 << 4);
            else
            {
                byte mod = 1 << 4;
                byte_value &= (byte)~(mod);
            }
        }
    }

    public Flags(bool _z, bool _n, bool _hc, bool _c)
    {
        zero = _z; negative = _n; half_carry = _hc; carry = _c;
    }
}

class CPU
{
    public Flags F = new Flags(true, false, false, false); // zero negative half-carry carry
    byte A = 0x11;
    byte garbage = 0xff;
    ushort[] registerCache = new ushort[8];
    void cache_store()
    {
        registerCache[0] = AF;
        registerCache[1] = BC;
        registerCache[2] = DE;
        registerCache[3] = HL;
        registerCache[4] = SP;
        registerCache[5] = PC;
    }

    public void rewind()
    {
        AF = registerCache[0];
        BC = registerCache[1];
        DE = registerCache[2];
        HL = registerCache[3];
        SP = registerCache[4];
        PC = registerCache[5];
    }

    public ushort AF
    {
        get => (ushort)(A << 8 | F.byte_value);
        private set
        {
            F.byte_value = (byte)(value & 0x00f0);
            A = (byte)(value >> 8);
        }
    }
    byte B = 0x00;
    byte C = 0x00;
    public ushort BC
    {
        get => (ushort)(B << 8 | C);
        private set
        {
            C = (byte)(value & 0x00ff);
            B = (byte)(value >> 8);
        }
    }
    byte D = 0xff;
    byte E = 0x56;
    public ushort DE
    {
        get => (ushort)(D << 8 | E);
        private set
        {
            E = (byte)(value & 0x00ff);
            D = (byte)(value >> 8);
        }
    }
    byte H = 0x00;
    byte L = 0x0D;
    public ushort HL
    {
        get => (ushort)(H << 8 | L);
        private set
        {
            L = (byte)(value & 0x00ff);
            H = (byte)(value >> 8);
        }
    }

    public int instructionCounter { get; private set; } = -1;

    // ushort _PC = 0x0100;
    public UInt16 PC = 0x0100;

    public UInt16 SP { get; private set; } = 0xFFFE;
    STATE S;

    // T-cycle cost per opcode. For conditional branches (JR/JP/CALL/RET cc)
    // this holds the "taken" cost; the not-taken cost is overridden inline
    // in Tick() since it's cheaper than encoding both in the table.
    byte[] cycleTable = new byte[256];
    byte[] cbCycleTable = new byte[256];

    void loadCycleTable()
    {
        var opcodes_json = System.IO.File.ReadAllText("src/opcodes.json").Split('\n');
        foreach (string line in opcodes_json)
        {
            if (line.Trim().Length == 0) continue;
            var instruction = System.Text.Json.JsonSerializer.Deserialize<Instruction>(line);
            int idx = System.Convert.ToInt32(instruction.opcode, 16);
            if (instruction.prefix == null)
                cycleTable[idx] = (byte)instruction.cycles;
            else
                cbCycleTable[idx] = (byte)instruction.cycles;
        }
    }

    public CPU(STATE _s)
    {
        S = _s;
        loadCycleTable();
        reset();
        no_modify_pc = false;
    }

    public void resetBGB()
    {
        PC = 0x0100;
        SP = 0xFFFE;
        AF = 0x1180;
        BC = 0x0000;
        DE = 0xFF56;
        HL = 0x000D;
        no_modify_pc = true;
    }

    public void reset()
    {
        instructionCounter = -1;
        PC = 0x0100;
        SP = 0xFFFE;
        A = 0x01;
        F.byte_value = 0xB0;
        B = 0x00;
        C = 0x13;
        D = 0x00;
        E = 0xD8;
        H = 0x01;
        L = 0x4D;
        no_modify_pc = true;
    }
    public ushort getShortAtPC()
    {
        byte a = S.read8((ushort)(PC + 1));
        byte b = S.read8((ushort)(PC + 2));
        return (ushort)(b << 8 | a);
    }

    public byte getByteAtPC()
    {
        return S.read8((ushort)(PC + 1));
    }

    void writeShortAtSP(ushort v)
    {
        // Console.WriteLine("WRITING {0:X4} to SP @ PC: {1:X4} IC: {2}", v, PC, instructionCounter);
        S.write8(--SP, (byte)(v >> 8));
        S.write8(--SP, (byte)(v & 0x00ff));
    }

    ushort getShortAtSP()
    {
        byte a = S.read8(SP++);
        byte b = S.read8(SP++);
        return (ushort)(b << 8 | a);
    }

    byte getRegByIdx(int idx)
    {
        return (idx % 8) switch
        {
            0 => B,
            1 => C,
            2 => D,
            3 => E,
            4 => H,
            5 => L,
            6 => S.read8(HL),
            7 => A,
            _ => garbage
        };
    }

    void ld_rr(int ai, int bi)
    {
        if (ai == 0) // 0x4
        {
            if (bi <= 7)
            {
                B = getRegByIdx(bi);
            }
            else
            {
                C = getRegByIdx(bi);
            }
        }
        else if (ai == 1) // 0x5
        {
            if (bi <= 7)
            {
                D = getRegByIdx(bi);
            }
            else
            {
                E = getRegByIdx(bi);
            }
        }
        else if (ai == 2) // 0x6
        {
            if (bi <= 7)
            {
                H = getRegByIdx(bi);
            }
            else
            {
                L = getRegByIdx(bi);
            }
        }
        else if (ai == 3) // 0x7
        {
            if (bi <= 7)
            {
                S.write8(HL, getRegByIdx(bi));
            }
            else
            {
                A = getRegByIdx(bi);
            }
        }
    }

    void sbc_reg(ref byte reg, int v)
    {
        short carry = F.carry ? (short)1 : (short)0;
        short sum = (short)(A - (short)v - carry);
        byte total = (byte)sum;
        F.zero = total == 0;
        F.half_carry = (short)(A & 0x0f) - (short)(v & 0x0f) - carry < 0;
        F.carry = sum < 0;
        F.negative = true;
        A = total;
    }

    void add_reg(ref byte register, byte value)
    {
        F.half_carry = (register & 0x0f) + (value & 0x0f) > 0x0f;
        int result = (int)(register) + (int)(value);
        register = (byte)result;
        F.zero = ((byte)result) == 0;
        F.negative = false;
        F.carry = result > 0xff;
    }

    ushort add_wide(int wr, int v)
    {
        var result = wr + v;
        F.carry = result > 0xffff;
        // F.zero = false;
        F.half_carry = (wr & 0xfff) > (result & 0xfff);
        F.negative = false;
        return (ushort)(result);
    }

    void inc_reg(ref byte register)
    {
        F.half_carry = (register & 0x0f) == 0x0f;
        register++;
        F.zero = register == 0;
        F.negative = false;
    }

    delegate void register_method(ref byte i);
    void al_rr(int op_operand, register_method left, register_method right)
    {
        if (op_operand <= 7)
        {
            left(ref A);
        }
        else
        {
            right(ref A);
        }
    }

    void cb_rr(int op_operand, register_method left, register_method right)
    {
        var method = left;
        if (op_operand > 7)
        {
            method = right;
        }
        switch (op_operand % 8)
        {
            case 0:
                method(ref B);
                break;
            case 1:
                method(ref C);
                break;
            case 2:
                method(ref D);
                break;
            case 3:
                method(ref E);
                break;
            case 4:
                method(ref H);
                break;
            case 5:
                method(ref L);
                break;
            case 6:
                {
                    byte tmp = S.read8(HL);
                    method(ref tmp);
                    S.write8(HL, tmp);
                }
                break;
            case 7:
                method(ref A);
                break;
        }
    }

    void or_reg(ref byte reg, byte v)
    {
        F.negative = false;
        F.carry = false;
        F.half_carry = false;
        reg |= v;
        F.zero = reg == 0;
    }

    void and_reg(ref byte reg, byte v)
    {
        F.negative = false;
        F.carry = false;
        F.half_carry = true;
        reg &= v;
        F.zero = reg == 0;
    }

    void xor_reg(ref byte reg, byte v)
    {
        F.negative = false;
        F.carry = false;
        F.half_carry = false;
        reg ^= v;
        F.zero = reg == 0;
    }

    void cp_reg(ref byte reg, byte v)
    {
        F.zero = reg == v;
        F.negative = true;
        F.half_carry = (v & 0x0f) > (reg & 0x0f);
        F.carry = v > reg;
    }

    // TODO: See if inlining helps any, https://stackoverflow.com/questions/473782/inline-functions-in-c
    void sub_reg(ref byte register, byte value)
    {
        F.negative = true;
        F.carry = value > register;
        F.half_carry = (value & 0x0f) > (register & 0x0f);// 1 > ... = false
        register -= value;
        F.zero = register == 0;
    }

    void adc_reg(ref byte reg, byte v)
    {
        short carry = F.carry ? (short)1 : (short)0;
        byte o1 = A;
        byte o2 = v;
        short result = (short)((short)A + (short)v + carry);
        A = (byte)result;
        F.zero = (byte)result == 0;
        F.negative = false;
        F.half_carry = (o1 & 0xf) + (o2 & 0xf) + (byte)(carry) > 0xf;
        F.carry = result > 0xff;
    }

    void dec_reg(ref byte register)
    {
        F.half_carry = (register & 0x0f) == 0;
        register = (byte)(register - 1);
        F.zero = register == 0;
        F.negative = true;
    }

    bool no_modify_pc = false;
    public int Tick()
    {
        cache_store();
        instructionCounter += 1;
        var op = S.read8(PC);
        int cycles = cycleTable[op];
        switch (op)
        {
            case 0x00:
                return cycles;
            case 0x01:
                BC = getShortAtPC();
                return cycles;
            case 0x02:
                S.write8(getByteAtPC(), A);
                return cycles;
            case 0x03:
                BC++;
                return cycles;
            case 0x0b:
                BC--;
                return cycles;
            case 0x08:
                var address = getShortAtPC();
                S.write8(address++, (byte)(SP & 0x00ff));
                S.write8(address, (byte)(SP >> 8));
                return cycles;
            case 0x04:
                inc_reg(ref B);
                return cycles;
            case 0x0c:
                inc_reg(ref C);
                return cycles;
            case 0x18:
                // JR i8
                PC = (ushort)(PC + (sbyte)getByteAtPC());
                // no_modify_pc = true;
                return cycles;

            case 0x36:
                S.write8(HL, getByteAtPC());
                return cycles;
            case 0x16:
                D = getByteAtPC();
                return cycles;
            case 0xc3:
                // JP u16
                PC = getShortAtPC();
                no_modify_pc = true;
                return cycles;
            case 0x21:
                HL = (getShortAtPC());
                return cycles;
            case 0x23:
                HL++;
                return cycles;

            case 0x1b:
                DE--;
                return cycles;
            case 0x2B:
                HL--;
                return cycles;
            case 0x26:
                H = getByteAtPC();
                return cycles;
            case 0x2d:
                dec_reg(ref L);
                return cycles;

            case 0x13:
                DE++;
                return cycles;
            case 0x33:
                SP++;
                return cycles;
            case 0xde:
                sbc_reg(ref A, getByteAtPC());
                return cycles;
            case 0x11:
                DE = getShortAtPC();
                return cycles;

            case 0x3b:
                SP--;
                return cycles;
            case 0x09:
                HL = add_wide(HL, BC);
                return cycles;
            case 0x19:
                HL = add_wide(HL, DE);
                return cycles;
            case 0x38:
                // JR C i8
                sbyte rel = (sbyte)getByteAtPC();
                if (F.carry)
                {
                    PC = (ushort)(PC + rel);
                    // no_modify_pc=true;
                }
                else
                {
                    cycles = 8;
                }
                return cycles;
            case 0xCA:
                if (F.zero)
                {
                    PC = getShortAtPC();
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 12;
                }
                return cycles;
            case 0xDA:
                if (F.carry)
                {
                    PC = getShortAtPC();
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 12;
                }
                return cycles;

            case 0xD2:
                // JP NC u16
                if (!F.carry)
                {
                    PC = getShortAtPC();
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 12;
                }
                return cycles;
            case 0x39:
                // var ohl = HL;
                HL = add_wide(HL, SP);
                return cycles;
            case 0xe8:
                {
                    var vi = (sbyte)getByteAtPC();
                    var res = (ushort)((int)vi + (int)SP);
                    var tmp = res ^ (ushort)vi ^ SP;
                    SP = res;
                    F.zero = false;
                    F.negative = false;
                    F.half_carry = (tmp & 0x10) == 0x10;
                    F.carry = (tmp & 0x100) == 0x100;
                    return cycles;
                }
            case 0xf8:
                {
                    var vi = (sbyte)getByteAtPC();
                    var res = (ushort)((int)vi + (int)SP);
                    var tmp = res ^ (ushort)vi ^ SP;
                    HL = res;
                    F.zero = false;
                    F.negative = false;
                    F.half_carry = (tmp & 0x10) == 0x10;
                    F.carry = (tmp & 0x100) == 0x100;
                    return cycles;
                }
            case 0xf9:
                SP = HL;
                return cycles;
            // -- 
            case 0x12:
                // if (DE == 0xc800)
                S.write8(DE, A);
                // S.addr(DE) = A;
                // if (DE == 0xc800)
                // Console.WriteLine("LD DE, A WROTE {0:X2} to {1:X4}", A, DE);
                return cycles;
            // --
            case 0x1c:
                inc_reg(ref E);//add_reg(ref E, 1);
                return cycles;
            case 0x1d:
                dec_reg(ref E);//sub_reg(ref E, 1);
                return cycles;
            case 0x0d:
                dec_reg(ref C);
                // sub_reg(ref C, 1);
                return cycles;
            case 0x14:
                inc_reg(ref D);//add_reg(ref D, 1);
                return cycles;
            case 0x24:
                inc_reg(ref H);
                // add_reg(ref H, 1);
                return cycles;
            case 0x32:
                S.write8(HL--, A);
                // A = S.addr(HL--);
                return cycles;
            case 0x22:
                S.write8(HL++, A);
                return cycles;
            case 0x1a:
                A = S.read8(DE);
                return cycles;
            case 0x0a:
                A = S.read8(BC);
                return cycles;
            case 0x27:

                if (!F.negative)
                {
                    if (F.carry || A > 0x99)
                    {
                        F.carry = true;
                        A += 0x60;
                    }
                    if (F.half_carry || (A & 0x0f) > 0x09)
                    {
                        // F.half_carry = false;
                        A += 0x06;
                    }
                }
                else if (F.carry && F.half_carry)
                {
                    A += 0x9A;
                    // F.half_carry = false;
                }
                else if (F.carry)
                {
                    A += 0xA0;
                }
                else if (F.half_carry)
                {
                    A += 0xFA;
                    // F.half_carry = false;
                }

                F.half_carry = false;
                F.zero = A == 0;
                return cycles;
            case 0x29:
                var ohl = HL;
                HL = add_wide(ohl, ohl);
                return cycles;
            case 0xe0:
                S.write8((ushort)(0xff00 + getByteAtPC()), A);
                return cycles;
            case 0xf0:
                A = S.read8((ushort)(0xff00 + getByteAtPC()));
                return cycles;
            case 0xfe:
                cp_reg(ref A, getByteAtPC());
                return cycles;
            case 0x20:
                if (!F.zero)
                {
                    // JR NZ, i8
                    PC = (ushort)(PC + (sbyte)getByteAtPC());
                    // no_modify_pc = true;
                }
                else
                {
                    cycles = 8;
                }
                return cycles;
            case 0xe9:
                PC = HL;
                no_modify_pc = true;
                return cycles;
            case 0x3c:
                inc_reg(ref A);
                return cycles;
            case 0xc2:
                // JP NZ u16
                if (!F.zero)
                {

                    PC = getShortAtPC();
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 12;
                }
                return cycles;

            case 0x28:
                if (F.zero)
                {
                    // JR Z, i8
                    PC = (ushort)(PC + (sbyte)(getByteAtPC()));
                    // no_modify_pc = true;
                }
                else
                {
                    cycles = 8;
                }
                return cycles;
            case 0x30:
                if (!F.carry)
                {
                    // JR NC, i8
                    PC = (ushort)(PC + (sbyte)(getByteAtPC()));
                    // no_modify_pc = true;
                }
                else
                {
                    cycles = 8;
                }
                return cycles;
            case 0x1f:
                var c = F.carry ? 1 << 7 : 0;
                F.carry = (A & (1 << 0)) == 1;
                A >>= 1;
                A |= (byte)c;
                F.negative = false;
                F.zero = false;
                F.half_carry = false;
                return cycles;
            case 0xee:
                xor_reg(ref A, getByteAtPC());
                return cycles;
            case 0x25:
                dec_reg(ref H);
                return cycles;
            case 0x3d:
                dec_reg(ref A);
                return cycles;
            case 0xce:
                adc_reg(ref A, getByteAtPC());
                return cycles;
            // case 0xc8:

            case 0x0e:
                C = getByteAtPC();
                return cycles;
            case 0x2a:
                A = S.read8(HL);
                HL++;
                return cycles;
            case 0x3a:
                A = S.read8(HL);
                HL--;
                return cycles;
            case 0x31:
                SP = getShortAtPC();
                return cycles;

            case 0xe5:
                writeShortAtSP(HL);
                return cycles;
            case 0xf5:
                writeShortAtSP(AF);
                return cycles;
            case 0xd5:
                writeShortAtSP(DE);
                return cycles;
            case 0xc5:
                writeShortAtSP(BC);
                return cycles;
            case 0xe1:
                HL = getShortAtSP();
                return cycles;
            case 0xf1:
                AF = getShortAtSP();
                return cycles;
            case 0xd1:
                DE = getShortAtSP();
                return cycles;
            case 0xc1:
                BC = getShortAtSP();
                return cycles;
            #region RST INSTRUCTIONS
            case 0xff:
                // RST 38h
                writeShortAtSP((ushort)(PC + 1));
                PC = 0x0000 + 0x38;
                no_modify_pc = true;
                return cycles;
            case 0xef:
                // RST 28h
                writeShortAtSP((ushort)(PC + 1));
                PC = 0x0000 + 0x28;
                no_modify_pc = true;
                return cycles;
            case 0xdf:
                // RST 38h
                // Console.WriteLine("RST: {0:X4}", PC + 1);
                writeShortAtSP((ushort)(PC + 1));
                PC = 0x0000 + 0x18;
                no_modify_pc = true;
                return cycles;
            case 0xd7:
                // RST 10h
                // Console.WriteLine("RST: {0:X4}", PC+1);
                writeShortAtSP((ushort)(PC + 1));
                PC = 0x0000 + 0x10;
                no_modify_pc = true;
                return cycles;
            case 0xe7:
                // RST 20h
                // Console.WriteLine("RST: {0:X4}", PC+1);
                writeShortAtSP((ushort)(PC + 1));
                PC = 0x0000 + 0x20;
                no_modify_pc = true;
                return cycles;
            case 0xf7:
                // RST 30h
                // Console.WriteLine("RST: {0:X4}", PC);
                writeShortAtSP((ushort)(PC + 1));
                PC = 0x0000 + 0x30;
                no_modify_pc = true;
                return cycles;
            case 0xcf:
                // RST 38h
                // Console.WriteLine("RST: {0:X4}", PC);
                writeShortAtSP((ushort)(PC + 1));
                PC = 0x0000 + 0x08;
                no_modify_pc = true;
                return cycles;
            case 0xc7:
                // Console.WriteLine("RST: {0:X4}", PC);
                writeShortAtSP((ushort)(PC + 1));
                PC = 0x0000 + 0x00;
                no_modify_pc = true;
                return cycles;
            #endregion
            case 0xEA:
                var addr = getShortAtPC();
                S.write8(addr, A);
                return cycles;
            case 0xf3:
                S.interrupts.enabled = false;
                return cycles;
            case 0xfb:
                S.interrupts.enabled = true;
                return cycles;
            case 0x3e:
                A = getByteAtPC();
                return cycles;
            case 0x2e:
                L = getByteAtPC();
                return cycles;
            case 0x1e:
                E = getByteAtPC();
                return cycles;

            #region RET INSTRUCTIONS
            case 0xc9:
                // RET
                PC = getShortAtSP();
                // Console.WriteLine("RET: {0:X4}", PC);
                no_modify_pc = true;
                return cycles;
            case 0xc8:
                // RET Z
                if (F.zero)
                {
                    PC = getShortAtSP();
                    // Console.WriteLine("RET: {0:X4}", PC);
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 8;
                }
                return cycles;
            case 0xc0:
                // RET NZ
                if (!F.zero)
                {
                    PC = getShortAtSP();
                    // Console.WriteLine("RET: {0:X4}", PC);
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 8;
                }
                return cycles;

            case 0xd0:
                // RET NC
                if (!F.carry)
                {
                    PC = getShortAtSP();
                    // Console.WriteLine("RET: {0:X4}", PC);
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 8;
                }
                return cycles;

            case 0xd8:
                // RET C
                if (F.carry)
                {
                    PC = getShortAtSP();
                    // Console.WriteLine("RET: {0:X4}", PC);
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 8;
                }
                return cycles;
            case 0xd9:
                // RETI
                PC = (ushort)(getShortAtSP() + 0);
                S.interrupts.enabled = true;
                // Console.WriteLine("PC: {0:X4}", PC);
                no_modify_pc = true;
                return cycles;
            #endregion

            case 0x35:
                {
                    byte tmp = S.read8(HL);
                    dec_reg(ref tmp);
                    S.write8(HL, tmp);
                }
                return cycles;

            #region CALL INSTRUCTIONS
            case 0xCD:
                // Console.WriteLine("CALL: {0:X4}", (ushort)(PC + 3));
                writeShortAtSP((ushort)(PC + 3));
                PC = getShortAtPC();
                no_modify_pc = true;

                return cycles;
            case 0xd4:
                if (!F.carry)
                {
                    // Console.WriteLine("CALL: {0:X4}", (ushort)(PC + 3));
                    writeShortAtSP((ushort)(PC + 3));
                    PC = getShortAtPC();
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 12;
                }
                return cycles;

            case 0xCC:
                if (F.zero)
                {
                    // Console.WriteLine("CALL: {0:X4}", (ushort)(PC + 3));
                    writeShortAtSP((ushort)(PC + 3));
                    PC = getShortAtPC();
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 12;
                }
                return cycles;

            case 0xC4:
                // CALL NZ, u16
                if (!F.zero)
                {
                    // Console.WriteLine("CALL: {0:X4}", (ushort)(PC + 3));
                    writeShortAtSP((ushort)(PC + 3));
                    PC = getShortAtPC();
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 12;
                }
                return cycles;

            case 0xdc:
                if (F.carry)
                {
                    // Console.WriteLine("CALL: {0:X4}", (ushort)(PC + 3));
                    writeShortAtSP((ushort)(PC + 3));
                    PC = getShortAtPC();
                    no_modify_pc = true;
                }
                else
                {
                    cycles = 12;
                }
                return cycles;
            #endregion
            case 0xc6:
                add_reg(ref A, getByteAtPC());
                return cycles;
            case 0xd6:
                sub_reg(ref A, getByteAtPC());
                return cycles;
            case 0xfa:
                {
                    A = S.read8(getShortAtPC());
                    return cycles;
                }
            case 0xe6:
                and_reg(ref A, getByteAtPC());
                return cycles;
            case 0xf6:
                or_reg(ref A, getByteAtPC());
                return cycles;
            case 0x06:
                B = getByteAtPC();
                return cycles;
            case 0x05:
                dec_reg(ref B);//sub_reg(ref B, 1);
                return cycles;
            case 0x2c:
                inc_reg(ref L);
                // add_reg(ref L, 1);
                return cycles;
            // case 0x0e:
            //     C = getByteAtPC();
            //     return cycles;
            case 0xCB:
                var cb_op = getByteAtPC();
                wideInstruction();
                cycles = cbCycleTable[cb_op];
                return cycles;

            case 0xf2:
                A = S.read8((ushort)(0xff00 + C));
                // no_modify_pc=true;
                return cycles;

            case 0x2f:
                F.negative = true;
                F.half_carry = true;
                A = (byte)~A;
                return cycles;

            case 0x3f:
                F.negative = false;
                F.half_carry = false;
                F.carry = !F.carry;
                return cycles;

            case 0x17:
                // RLA
                var cc = F.carry ? 1 : 0;
                F.carry = (A & 0x80) == 0x80;
                A = (byte)((A << 1) & 0xff | cc);
                F.zero = false;
                F.half_carry = false;
                F.negative = false;
                return cycles;

            case 0x0f:
                // RRCA
                A = (byte)(A >> 1 | (A & 1) << 7);
                F.zero = false;
                F.half_carry = false;
                F.carry = A > 0x7f;
                F.negative = false;
                return cycles;

            case 0x07:
                // RLCA
                F.carry = A > 0x7f;
                A = (byte)(A << 1 | A >> 7);
                F.zero = false;
                F.half_carry = false;
                F.negative = false;
                return cycles;

            case 0x15:
                dec_reg(ref D);
                return cycles;

            case 0x37:
                F.negative = false;
                F.half_carry = false;
                F.carry = true;
                return cycles;

            case 0xe2:
                S.write8((ushort)(0xff00 + C), A);
                return cycles;

            case 0x34:
                S.write8(HL, (byte)(S.read8(HL) + 1));
                return cycles;
            default:
                // Console.WriteLine("[!!] Unexpected instruction: {0,2:x}", op);
                break;
        }

        var op_id = op >> 4;
        var op_operand = op & 0x0f;

        switch (op_id)
        {
            case 0x4:
            case 0x5:
            case 0x6:
            case 0x7:
                ld_rr(op_id - 4, op_operand);
                return cycles;
            case 0x8:
                al_rr(op_operand, (ref byte x) => add_reg(ref x, getRegByIdx(op_operand)), (ref byte x) => adc_reg(ref x, getRegByIdx(op_operand)));
                return cycles;
            case 0x9:
                al_rr(op_operand, (ref byte x) => sub_reg(ref x, getRegByIdx(op_operand)), (ref byte x) => sbc_reg(ref x, getRegByIdx(op_operand)));
                return cycles;
            case 0xB:
                al_rr(op_operand, (ref byte x) => or_reg(ref x, getRegByIdx(op_operand)), (ref byte x) => cp_reg(ref x, getRegByIdx(op_operand)));
                return cycles;
            case 0xA:
                al_rr(op_operand, (ref byte x) => and_reg(ref x, getRegByIdx(op_operand)), (ref byte x) => xor_reg(ref x, getRegByIdx(op_operand)));
                return cycles;
                // case 
        }

        Console.WriteLine("\x1b[31m[!!] Unexpected instruction: {0:x2}\x1b[0m", op);
        found_unimplemented_instr = true;
        return cycles;
    }


    void wideInstruction()
    {
        var opcode = getByteAtPC();
        var op_id = opcode >> 4;
        var op_operand = opcode & 0x0f;
        switch (op_id)
        {
            case 0x0:
                cb_rr(op_operand, (ref byte x) =>
                {
                    // RLC
                    var carry = x >> 7;
                    var rot = x << 1 & 0xff | carry;
                    x = (byte)rot;
                    F.zero = rot == 0;
                    F.negative = false;
                    F.half_carry = false;
                    F.carry = carry == 1;
                },
                (ref byte x) =>
                {
                    // RRC
                    F.negative = false;
                    F.half_carry = false;
                    F.carry = (x & 1 << 0) == 1;
                    x >>= 1;
                    x |= (byte)(F.carry ? 1 << 7 : 0);
                    F.zero = x == 0;
                });
                return;
            case 0x1:
                cb_rr(op_operand, (ref byte x) =>
                {
                    // RL
                    var oc = F.carry ? 1 : 0;
                    var nc = x >> 7;
                    var rot = (x << 1) & 0xff | oc;
                    x = (byte)rot;
                    F.zero = rot == 0;
                    F.carry = nc == 1;
                    F.negative = false;
                    F.half_carry = false;
                    return;
                },
                (ref byte x) =>
                {
                    // RR
                    F.negative = false;
                    F.half_carry = false;
                    var oc = F.carry;
                    F.carry = (x & 1 << 0) == 1;
                    x >>= 1;
                    x |= (byte)(oc ? 1 << 7 : 0);
                    F.zero = x == 0;
                });
                return;
            case 0x2:
                cb_rr(op_operand, (ref byte x) =>
                {
                    // SLA
                    var cc = x >> 7;
                    var res = (x << 1) & 0xff;
                    x = (byte)res;
                    F.zero = res == 0;
                    F.negative = false;
                    F.half_carry = false;
                    F.carry = cc == 1;
                    return;
                }
                ,
                (ref byte x) =>
                {
                    F.negative = false;
                    F.half_carry = false;
                    var oc = F.carry;
                    F.carry = (x & 1 << 0) == 1;
                    x = (byte)((x & 1 << 7) | x >> 1);
                    F.zero = x == 0;
                });
                return;
            case 0x3:
                cb_rr(op_operand, (ref byte x) =>
                {
                    // SWAP
                    F.negative = false;
                    F.half_carry = false;
                    F.carry = false;
                    x = (byte)(x >> 4 | x << 4);
                    F.zero = x == 0;
                },
                (ref byte x) =>
                {
                    // SRL
                    F.negative = false;
                    F.half_carry = false;
                    F.carry = (x & 1 << 0) == 1;
                    x >>= 1;
                    F.zero = x == 0;
                });
                return;
            case 0x4:
                cb_rr(op_operand, (ref byte x) =>
                {
                    F.half_carry=true;
                    F.negative=false;
                    F.zero = (x & 1) == 0;
                    return;
                },
                (ref byte x) =>
                {
                    // SRL
                    F.negative = false;
                    F.half_carry = true;
                    F.zero = (x & 1 << 1) == 0;
                });
                return;
            case 0x5:
                cb_rr(op_operand, (ref byte x) =>
                {
                    F.half_carry = true;
                    F.negative = false;
                    F.zero = (x & 1 << 2) == 0;
                    return;
                },
                (ref byte x) =>
                {
                    // SRL
                    F.negative = false;
                    F.half_carry = true;
                    F.zero = (x & 1 << 3) == 0;
                });
                return;
            case 0x6:
                cb_rr(op_operand, (ref byte x) =>
                {
                    F.half_carry = true;
                    F.negative = false;
                    F.zero = (x & 1 << 4) == 0;
                    return;
                },
                (ref byte x) =>
                {
                    // SRL
                    F.negative = false;
                    F.half_carry = true;
                    F.zero = (x & 1 << 5) == 0;
                });
            
                return;
            case 0x7:
                cb_rr(op_operand, (ref byte x) =>
                {
                    F.half_carry = true;
                    F.negative = false;
                    F.zero = (x & 1 << 6) == 0;
                    return;
                },
                (ref byte x) =>
                {
                    // SRL
                    F.negative = false;
                    F.half_carry = true;
                    F.zero = (x & 1 << 7) == 0;
                });
                return;
            case 0x8:
                cb_rr(op_operand, (ref byte x) =>
                {
                    x = (byte)(x & ~(1 << 0));
                    return;
                },
                (ref byte x) =>
                {
                    x = (byte)(x & ~(1 << 1));
                });
                return;
            case 0x9:
                cb_rr(op_operand, (ref byte x) =>
                {
                    x = (byte)(x & ~(1 << 2));
                    return;
                },
                (ref byte x) =>
                {
                    x = (byte)(x & ~(1 << 3));
                });
                return;
            case 0xa:
                cb_rr(op_operand, (ref byte x) =>
                {
                    x = (byte)(x & ~(1 << 4));
                    return;
                },
                (ref byte x) =>
                {
                    x = (byte)(x & ~(1 << 5));
                });
                return;
            case 0xb:
                cb_rr(op_operand, (ref byte x) =>
                {
                    x = (byte)(x & ~(1 << 6));
                    return;
                },
                (ref byte x) =>
                {
                    x = (byte)(x & ~(1 << 7));
                });
                return;
            case 0xc:
                cb_rr(op_operand, (ref byte x) =>
                {
                    x = (byte)(x | (1 << 0));
                    return;
                },
                (ref byte x) =>
                {
                    x = (byte)(x | (1 << 1));
                });
                return;
            case 0xd:
                cb_rr(op_operand, (ref byte x) =>
                {
                    x = (byte)(x | (1 << 2));
                    return;
                },
                (ref byte x) =>
                {
                    x = (byte)(x | (1 << 3));
                });
                return;
            case 0xe:
                cb_rr(op_operand, (ref byte x) =>
                {
                    x = (byte)(x | (1 << 4));
                    return;
                },
                (ref byte x) =>
                {
                    x = (byte)(x | (1 << 5));
                });
                return;
            case 0xf:
                cb_rr(op_operand, (ref byte x) =>
                {
                    x = (byte)(x | (1 << 6));
                    return;
                },
                (ref byte x) =>
                {
                    x = (byte)(x | (1 << 7));
                });
                return;
        }
        // instructionCounter += 1;
    }

    public bool found_unimplemented_instr = false;
    public void incrementPC(int v)
    {
        if (!no_modify_pc)
        {
            PC += (ushort)v;
        }
        else
        {
            // Console.WriteLine("skipping increment cuz of jump / call / ret");
            no_modify_pc = false;
        }
    }

    public void SETA(byte v)
    {
        A = v;
    }
}