class Instruction {
    public string? opcode {get;set;}
    public string? prefix {get;set;}
    public string? mnemonic {get;set;}
    public string[]? operands {get;set;}
    public int bytes {get;set;}
    public int cycles {get;set;}
    public string[]? flagsZNHC {get;set;}
}

class Debugger {
    Instruction[] instructions = new Instruction[16 * 16];
    Instruction[] wide_instructions = new Instruction[16 * 16];
    STATE S;
    CPU C;
    StreamReader logfile;
    string log_path = "";
    // string[] logFile;
    public void SetNonInteractive(){ stepDebug=false; stop_at_invalid_access=false; no_print_every_instr=true; }

    public Debugger(STATE _s, CPU _c, string rom_path) {
        S = _s;
        C = _c;
        S.debug_hook = this;
        var opcodes_json = System.IO.File.ReadAllText("src/opcodes.json").Split('\n');   
        log_path = rom_path.Replace(".gb", ".txt");
        try{ logfile = new StreamReader(log_path); } catch{ logfile = StreamReader.Null; }
        foreach (string opcode in opcodes_json) {
            var instruction = System.Text.Json.JsonSerializer.Deserialize<Instruction>(opcode);
            if (instruction.prefix == null) {
                instructions[System.Convert.ToInt32(instruction.opcode, 16)] = instruction;
            } else {
                wide_instructions[System.Convert.ToInt32(instruction.opcode, 16)] = instruction;
            }
            // instructions.Insert(System.Convert.ToInt32(instruction.opcode, 16), instruction);
        }
    }

    Instruction getInstruction(int opcode) {
        if (opcode == 0xCB) {
            return wide_instructions[C.getByteAtPC()];
        } else {
            return instructions[opcode];
        }
    }

    string color(string opname) {
        return opname switch {
            "JP" or "JR" => $"\x1b[33m{opname}\x1b[0m",
            "CALL" or "RET" => $"\x1b[34m{opname}\x1b[0m",
            "POP" or "PUSH" => $"\x1b[35m{opname}\x1b[0m",
            "LD" or "LDI" or "LDH" => $"\x1b[32m{opname}\x1b[0m",
            "ADD" or "ADC" or "SUB" or "SBC" or "OR" or "AND" or "XOR" or "INC" or "DEC" => $"\x1b[36m{opname}\x1b[0m",
            _ => opname
        };
    }

    // bool full_jump = false;
    byte opcode = 0x00;
    bool stepDebug = true;
    bool noprintNoOp = true;
    HashSet<ushort> breakpoints = new HashSet<ushort>();
    HashSet<byte> breakinstrs = new HashSet<byte>();
    HashSet<int> breakinstrcount = new HashSet<int>();

    void printMemoryAtAddress(int addr) {
        for (int i = 0; i < 10; i++) {
            for (int j = 0; j < 16; j++) {
                Console.Write($"{S.addrNoHook((ushort)(addr + j + i*16)).ToString("X2")} ");
            }
            Console.WriteLine();
        }
    } 

    void printStack() {
        for (int start = C.SP; start < C.SP + 8; start++) {
            Console.Write($"{S.addrNoHook((ushort)start).ToString("x2")} ");
        }
        Console.WriteLine();
    }

    string getInstructionPretty(int opcode) {
        string operands = "";
        var instruction = getInstruction(opcode);
        string name = instruction.mnemonic;
        if (instruction.operands != null)
        {
            foreach (string operand in instruction.operands)
            {
                operands += " ";
                if (operand == "a16" || operand == "(a16)")
                {
                    operands += $"${C.getShortAtPC().ToString("X4")}"; //C.getShortAtPC().ToString("X4");
                }
                else if (operand == "a8" || operand == "(a8)")
                {
                    operands += $"${(name == "LDH" ? 0xff00 + C.getByteAtPC() : 0x00000 + C.getByteAtPC()).ToString("X2")}";
                }
                else if (operand == "r8")
                {
                    operands += $"${(C.PC + (sbyte)C.getByteAtPC() + instructions[opcode].bytes).ToString("X4")}";
                }
                else if (operand == "d16")
                {
                    operands += $"0x{C.getShortAtPC().ToString("X4")}";
                }
                else if (operand == "d8")
                {
                    operands += $"0x{C.getByteAtPC().ToString("X2")}";
                }
                else
                {
                    operands += operand;
                }
            }
        }
        return String.Format("{0:x2} {1}{2}", opcode, color(name), operands);
    }

    void printState(int opcode, bool inBreakpoint) {
        int operandLength = 0;
        string leftFmt = string.Format("{0:x4}: {1}", inBreakpoint ? $"\x1b[31m{C.PC.ToString("x4")}\x1b[0m" : C.PC, getInstructionPretty(opcode));
        string rightFmt = string.Format("[IC:{0}] AF:{1:X4} BC:{2:X4} DE:{3:X4} HL:{4:X4} SP:{5:X4}", C.instructionCounter, C.AF, C.BC, C.DE, C.SP, C.HL);
        System.Console.WriteLine("{0}\t{1}", leftFmt, rightFmt);
        // System.Console.WriteLine(getStateRepr());
    }

    string getStateRepr() {
        var af = C.AF;
        return String.Format("A: {0:X2} F: {1:X2} B: {2:X2} C: {3:X2} D: {4:X2} E: {5:X2} H: {6:X2} L: {7:X2} SP: {8:X4} PC: 00:{9:X4} ({10:X2} {11:X2} {12:X2} {13:X2})",
            af >> 8,
            af & 0x00ff,
            C.BC >> 8,
            C.BC & 0x00ff,
            C.DE >> 8,
            C.DE & 0x00ff,
            C.HL >> 8,
            C.HL & 0x00ff,
            C.SP,
            C.PC,
            S.addrNoHook(C.PC), S.addrNoHook((ushort)(C.PC + 1)), S.addrNoHook((ushort)(C.PC + 2)), S.addrNoHook((ushort)(C.PC + 3)));
    }

    bool verifyCorrectState() {
        // if (opcode == 0x00) 
        string? fromLog = logfile.ReadLine();
        if (fromLog == null) {
            Console.WriteLine("Ran out of log - assume correct\n");
            System.Environment.Exit(0);
        }
        var fromState = getStateRepr();
        if (fromLog != fromState) {
            Console.WriteLine("\x1b[31mDETECTED DISCREPANCY @ {0:X4}:\x1b[0m\n\tLOG: {1}\n\tSTA: {2}", C.PC, fromLog, fromState);
            return false;
        }
        return true;
    }

    HashSet<ushort> watch_addr = new HashSet<ushort>( 0xc800 );
    bool print_watch_addr = false;
    public void memAccessHook(int addr) {
        if (watch_addr.Contains((ushort)addr)) {
            var instr = getInstruction(opcode);
            Console.WriteLine("\x1b[1mAccess to\x1b[0m {0:X4} by {1} @ {2} & PC:{3:X4}", addr, getInstructionPretty(opcode), C.instructionCounter, C.PC);
            print_watch_addr=true;
        }
    }

    bool stop_at_invalid_access = true;
    bool cross_verify = false;
    bool no_print_every_instr = true;
    string serial_buffer = "";
    int contN = 0;
    public void DebugTick() {
        opcode = S.addrNoHook(C.PC);
        bool inBreakpoint = breakpoints.Contains(C.PC) || breakinstrs.Contains(opcode) || breakinstrcount.Contains(C.instructionCounter);
        if (inBreakpoint) {
            Console.WriteLine("reached breakpoint");
            stepDebug = true;
        }

        var name = instructions[opcode].mnemonic;

        if ((S.had_invalid_access && stop_at_invalid_access) || C.found_unimplemented_instr) {
            Console.WriteLine("activated debugger cuz of invalid memory access / unimplemented instr");
            stepDebug = true;
            S.had_invalid_access = false;
        }

        if (S.addrNoHook(0xff02) == 0x81) {
            char c = (char)S.addrNoHook(0xff01);
            serial_buffer += c;// Console.WriteLine($"SERIAL OUTPUT: {(char)S.addrNoHook(0xff01)}");
            if (c == '\n') {
                Console.Write("\x1b[1mSERIAL: \x1b[0m");
                Console.Write(serial_buffer);
            }
            S.addrNoHook(0xff02) = 0;
        }

        if (cross_verify && !verifyCorrectState()) {
            stepDebug = true;
        }

        if (inBreakpoint || !no_print_every_instr)
            printState(opcode, inBreakpoint);

        if (print_watch_addr) {
            foreach (ushort addr in watch_addr) {
                Console.WriteLine("{0:X4}: {1:X2} ", addr, S.addrNoHook(addr));
            }
            print_watch_addr=false;
        }

        if (!stepDebug)
            return;

takeInput:

        if (contN-- > 0) {
            C.found_unimplemented_instr = false;
            stepDebug = false;
            return;
        }

        var userinput = Console.ReadLine()!.Split(" ");
        if (userinput[0] == "c" || userinput[0] == "n") {
            return;
        } else if (userinput[0] == "show") {
            printState(opcode, false);
            return;
        } else if (userinput[0] == "contn") {
            contN = Int32.Parse(userinput[1]) + (100 - 64);
            return;
        }else if (userinput[0] == "b") {
            var bp = System.Convert.ToUInt16(userinput[1], 16);
            breakpoints.Add(bp);
            Console.WriteLine("Added breakpoint 0x{0:X4}", bp);
        } else if (userinput[0] == "bins") {
            breakinstrs.Add(System.Convert.ToByte(userinput[1], 16));
        } else if (userinput[0] == "bic") {
            breakinstrcount.Add(Int32.Parse(userinput[1]));
        }else if (userinput[0] == "ex") {
            System.Environment.Exit(0);
        } else if (userinput[0] == "cont" || userinput[0] == "next") {
            C.found_unimplemented_instr = false;
            stepDebug = false;
            return;
        } else if (userinput[0] == "rmb") {
            breakpoints.Remove(System.Convert.ToUInt16(userinput[1], 16));
        } else if (userinput[0] == "rmbins") {
            breakinstrs.Remove(System.Convert.ToByte(userinput[1], 16));
        } else if (userinput[0] == "mhead") {
            int addr = userinput[1] switch {
                "wram" => 0xc000,
                "rom" => 0,
                "pc" or "PC" => C.PC,
                "sp" or "SP" => C.SP,
                _ => System.Convert.ToInt32(userinput[1], 16)
            };
            printMemoryAtAddress(addr);
        } else if (userinput[0] == "reset") {
            C.reset();
            logfile = new StreamReader(log_path);
            S.reset();
            printState(S.addrNoHook(C.PC), false);
            return;
        } else if (userinput[0] == "resetbgb"){
            C.resetBGB();
            S.reset();
            printState(S.addrNoHook(C.PC), false);
            cross_verify = false;
            return;
        }else if (userinput[0] == "showprev") {
            Console.WriteLine("Please `reset` if you want to continue discrepancy checking");
            C.rewind();
            printState(S.addrNoHook(C.PC), false);
            // return;
        } else if (userinput[0] == "stopinvalidacc") {
            stop_at_invalid_access = !stop_at_invalid_access; // TODO: make this work correctly
        } else if (userinput[0] == "nextaddr") {
            Console.WriteLine($"${ (C.PC + instructions[opcode].bytes).ToString("x4") }");
        } else if (userinput[0] == "help") {
            Console.WriteLine("-- RTFM --");
        } else if (userinput[0] == "pstack") {
            printStack();
        } else if (userinput[0] == "flags") {
            Console.WriteLine("Z: [{0}] N: [{1}] HC: [{2}] C: [{3}]", C.F.zero, C.F.negative, C.F.half_carry, C.F.carry);
        } else if (userinput[0] == "AF") {
            Console.WriteLine("{0:X4}", C.AF);
        }  else if (userinput[0] == "HL") {
            Console.WriteLine("{0:X4}", C.HL);
        } else if (userinput[0] == "PC") {
            Console.WriteLine("{0:X4}", C.PC);
        } else if (userinput[0] == "tgverify") {
            cross_verify = !cross_verify;
        } else if (userinput[0] == "tgnoprint") {
            no_print_every_instr = !no_print_every_instr;
        } else if (userinput[0] == "modify") {
            S.addrNoHook(Convert.ToUInt16(userinput[1], 16)) = Convert.ToByte(userinput[2], 16);
        } else if (userinput[0] == "watchaddr") {
            watch_addr.Add(Convert.ToUInt16(userinput[1], 16));
        }else if (userinput[0] == "rmw") {
            watch_addr.Remove(Convert.ToUInt16(userinput[1], 16));
        } else if (userinput[0] == "SETA") {
            C.SETA(Convert.ToByte(userinput[1], 16));
        } else if (userinput[0] == "IC") {
            Console.WriteLine("{0}", C.instructionCounter);
        }
        goto takeInput;
    }

    // Flags Register
    //----------------
    // Bit 0 - 3 => 0
    // Bit 4     => CARRY FLAG
    // Bit 5     => HALF CARRY FLAG
    // Bit 6     => SUBTRACTION FLAG
    // Bit 7     => ZERO FLAG
    // C = 1100       |            | Subtraction | Zero
    // B = 1011 Carry | Half Carry |             | Zero
    // A = 1010       | Half Carry |             | Zero
    // 8 = 1000       |            |             | Zero
    // 7 = 0111 Carry | Half Carry | Subtraction 
    // 3 = 0011 Carry | Half Carry |
    // 2 = 0010       | Half Carry |
    // 1 = 0001 Carry |            |             |

    public void IncrementPC() {
        C.incrementPC(getInstruction(opcode).bytes);
    }
}