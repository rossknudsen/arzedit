using System;
using System.IO;
using System.Collections.Generic;
using LZ4;
using System.Linq;
using System.Text;
using CommandLine;
using CommandLine.Text;

namespace arzedit
{
    class Program
    {
        // static string ArzFile = "database.arz";
        // static string OutputFile = "database2.arz";
        static byte[] mdata = null;
        static byte[] footer = new byte[16];
        public static List<string> strtable = null;
        public static List<int> strrefcount = null;
        public static SortedDictionary<string, int> strsearchlist = null;
        static List<ARZRecord> rectable = null;
        static SortedList<string, int> recsearchlist = null;
        static void Main(string[] args)
        {
            var voptions = new VerbOptions();
            // ParserResult<object> result = CommandLine.Parser.Default.ParseArguments<SetOptions, GetOptions>(args);
            string iVerb = ""; object iOpt = null;
            if (args.Length > 0  && Parser.Default.ParseArguments(args, voptions, (verb, subOptions) => {
                iVerb = verb; iOpt = subOptions;
                })) {
                if (iVerb == "set")
                {
                    SetOptions opt = iOpt as SetOptions;
                    if (string.IsNullOrEmpty(opt.OutputFile)) opt.OutputFile = opt.InputFile;

                    // List<string> entries = new List<string>(opt.SetEntries);
                    // DEBUG:
                    // Console.WriteLine("In: {0}; Out: {1}; Rec: {2}; Entries #: {3}", opt.InputFile, opt.OutputFile, opt.SetRecord, entries.Count);
                    Console.Write("Parsing database ... ");
                    DateTime start = DateTime.Now;
                    if (!LoadFile(opt.InputFile)) return;
                    DateTime end = DateTime.Now;
                    Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));
                    // Pack everything under the base
                    bool bchanged = false;

                    if (!string.IsNullOrEmpty(opt.SetBase))
                    {
                        // traverse folders
                        string fullbase = Path.GetFullPath(opt.SetBase);
                        string subbase = fullbase;
                        if (!string.IsNullOrEmpty(opt.SetSubfolder))
                        {
                            subbase = opt.SetSubfolder.Replace('/', Path.DirectorySeparatorChar);
                            if (Directory.Exists(subbase))
                                subbase = Path.GetFullPath(subbase);
                            else
                            {
                                subbase = Path.Combine(fullbase, subbase);
                            }
                        }
                        string[] allfiles = Directory.GetFiles(subbase, "*.dbr", SearchOption.AllDirectories);

                        // Build record lookup table for faster matching
                        recsearchlist = new SortedList<string, int>();
                        recsearchlist.Capacity = rectable.Count;
                        for (int i = 0; i < rectable.Count; i++)
                        {
                            recsearchlist.Add(strtable[rectable[i].rfid], i);
                        }

                        // Update all files:
                        bool brchanged = false;
                        start = DateTime.Now;
                        Console.Write("Packing {0} record files ... ", allfiles.Length);
                        ProgressBar progress = new ProgressBar();
                        int ci = 0;
                        foreach (string dbrfile in allfiles)
                        {
                            string recordname = dbrfile.Substring(fullbase.Length).Replace(Path.DirectorySeparatorChar, '/').ToLower();
                            if (recordname.StartsWith("/")) recordname = recordname.Substring(1);
                            // Console.WriteLine(recordname + " f:" + dbrfile);
                            ARZRecord brec = null;
                            try
                            {
                                brec = rectable[recsearchlist[recordname]]; // TODO: Catch invalid key error here and print out error message to console
                            }
                            catch (KeyNotFoundException e) {
                                Console.WriteLine("Error packing record file \"{0}\": as record {1} - no such record in database!", dbrfile, recordname);
                                return;
                            }
                            List<string> fentries = new List<string>(File.ReadAllLines(dbrfile));
                            if (ModAllEntries(brec, fentries, out brchanged))
                            {
                                if (brchanged)
                                {
                                    // DEBUG:
                                    // Console.WriteLine("{0} {1}", recordname, brchanged ? "CHANGED " : "unchanged");
                                    brec.PackData();
                                }
                            }
                            else
                            {
                                Console.WriteLine("{0} Errored", recordname);
                                return;
                            }
                            progress.Report(((double)ci++) / allfiles.Length);
                            bchanged |= brchanged;
                        }
                        progress.Dispose();
                        progress = null;
                        end = DateTime.Now;
                        Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));
                    }

                    bool pchanged = false;

                    if (opt.SetPatches != null)
                    {
                        foreach (string patchfile in opt.SetPatches)
                        {
                            string[] pfstrings = File.ReadAllLines(patchfile);
                            string srec = "";
                            int cline = 0;
                            List<string> entrybuf = new List<string>();
                            bool pechanged = false;
                            while (cline < pfstrings.Length)
                            {
                                // Ignore comments
                                string sline = pfstrings[cline].Trim();
                                if (sline.StartsWith("#"))
                                {
                                    cline++; continue;
                                }
                                if (sline.StartsWith("[") && sline.EndsWith("]"))
                                {
                                    if (entrybuf.Count > 0 && srec != "")
                                    { // Got record and entries, change them
                                        ARZRecord crec = rectable.Find(r => strtable[r.rfid] == srec);
                                        Console.WriteLine("Patching {0}", srec); // DEBUG
                                        ModAllEntries(crec, entrybuf, out pechanged);
                                        if (pechanged) crec.PackData();
                                        pchanged |= pechanged;
                                        entrybuf.Clear();
                                    }
                                    srec = sline.Substring(1, sline.Length - 2);
                                    if (srec.Trim().StartsWith("/")) srec = srec.Trim().Substring(1); // TODO: May need lowering case
                                }
                                else
                                {
                                    entrybuf.Add(sline);
                                }
                                cline++;
                            }
                            // EOF, save changes:
                            if (entrybuf.Count > 0 && srec != "")
                            { // Got record and entries, change them
                                ARZRecord crec = rectable.Find(r => strtable[r.rfid] == srec);
                                Console.WriteLine("Patching {0}", srec); // DEBUG
                                ModAllEntries(crec, entrybuf, out pechanged);
                                if (pechanged) crec.PackData();
                                pchanged |= pechanged;
                            }

                        }
                    }

                    // Get a record to act upon if one is set
                    ARZRecord rec = null;

                    if (!string.IsNullOrEmpty(opt.SetRecord))
                    {
                        opt.SetRecord = opt.SetRecord.Trim();
                        if (opt.SetRecord.StartsWith("/")) opt.SetRecord = opt.SetRecord.Substring(1);
                        rec = rectable.Find(r => strtable[r.rfid] == opt.SetRecord);
                        if (rec == null)
                        {
                            Console.WriteLine("Record \"{0}\" does not exist in database, check case, spaces, and separators", opt.SetRecord);
                            return;
                        }
                    }

                    bool fchanged = false;

                    // Now file:
                    if (!string.IsNullOrEmpty(opt.SetFile) && rec != null)
                    {
                        List<string> fentries = new List<string>(File.ReadAllLines(opt.SetFile));
                        ModAllEntries(rec, fentries, out fchanged);
                    }

                    // Now list of entries:
                    bool echanged = false;

                    if (opt.SetEntries != null && rec != null)
                    {
                        List<string> eentries = new List<string>(opt.SetEntries);
                        ModAllEntries(rec, eentries, out echanged);
                    }


                    if (pchanged || bchanged || fchanged || echanged)
                    {
                        if (fchanged || echanged && rec != null) rec.PackData();
                        if (opt.ForceOverwrite || !File.Exists(opt.OutputFile) || char.ToUpper(Ask(string.Format("Output file \"{0}\" exists, overwrite? [y/n] n: ", Path.GetFullPath(opt.OutputFile)), "yYnN", 'N')) == 'Y')
                        {
                            start = DateTime.Now;
                            Console.Write("Saving database ... ");
                            CompactStringlist();
                            SaveData(opt.OutputFile);
                            end = DateTime.Now;
                            Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));
                        }
                        else
                        {
                            Console.WriteLine("Aborted by user.");
                        }

                        // Console.WriteLine("Success, press any key to exit ...");                    
                    }
                    else
                    {
                        // SaveData(OutputFile); // DEBUG: REMOVE
                        Console.WriteLine("No changes, files untouched");
                    }
                    // Console.ReadKey(false);

                    /*
                   // List<string> userecords = new List<string>(new string[] { opt.SetRecord });

                    recsearchlist = FindRecords(userecords);

                    foreach (string recname in userecords)
                    {
                        ARZRecord rec = rectable[recsearchlist[recname]];
                        foreach (string emod in modentries)
                        {
                            string ename = emod.Split(',')[0];
                            ARZEntry entry = rec.entries.Find(e => strtable[e.dstrid] == ename);
                            Console.WriteLine("Found:");
                            Console.WriteLine(entry);
                            // Modify a record:
                            if (!entry.TryAssign(emod)) return; // Try assigning data
                            if (entry.changed)
                            {
                                rec.PackData();
                                changed = true;
                            }
                        }
                    }*/
                }
                else if (iVerb == "extract")
                {
                    ExtractOptions opt = iOpt as ExtractOptions;
                    // Console.WriteLine("In: {0}; Out: {1}; Rec: {2}; Entries #: {3}", opt.InputFile, opt.OutputFile, opt.SetRecord, entries.Count);
                    Console.Write("Parsing database ... ");
                    DateTime start = DateTime.Now;
                    if (!LoadFile(opt.InputFile)) return;
                    DateTime end = DateTime.Now;
                    Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

                    string outpath = null;
                    if (string.IsNullOrEmpty(opt.OutputPath))
                        outpath = Directory.GetCurrentDirectory();
                    else
                        outpath = Path.GetFullPath(opt.OutputPath);

                    Console.WriteLine("Extracting to \"{0}\" ...", outpath);
                    
                    char ans = 'n';
                    bool overwriteall = opt.ForceOverwrite;
                    start = DateTime.Now;
                    using (ProgressBar progress = new ProgressBar())
                    {
                        int ci = 0;
                        foreach (ARZRecord rec in rectable)
                        {
                            string filename = Path.Combine(outpath, strtable[rec.rfid].Replace('/', Path.DirectorySeparatorChar));
                            // Console.WriteLine("Writing \"{0}\"", filename); // Debug
                            Directory.CreateDirectory(Path.GetDirectoryName(filename));
                            bool fileexists = File.Exists(filename);
                            if (!overwriteall && fileexists)
                            {
                                progress.SetHidden(true);
                                ans = char.ToLower(Ask(string.Format("File \"{0}\" exists, overwrite? yes/no/all/cancel (n): ", filename), "yYnNaAcC", 'n'));
                                if (ans == 'c') {
                                    Console.WriteLine("Aborted by user");
                                    return;
                                };
                                progress.SetHidden(false);
                                overwriteall = ans == 'a';
                            }

                            if (!fileexists || overwriteall || ans == 'y')
                            {
                                using (FileStream fs = new FileStream(filename, FileMode.Create))
                                using (StreamWriter sr = new StreamWriter(fs))
                                {
                                    sr.NewLine = "\n";
                                    foreach (ARZEntry etr in rec.entries)
                                        sr.WriteLine(etr);
                                }
                                // Set Date:
                                File.SetCreationTime(filename, rec.rdFileTime); // TODO: Check if this is needed
                            }
                            progress.Report(((double)ci++) / rectable.Count);
                        }
                    }
                    end = DateTime.Now;
                    Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));
                }
                else if (iVerb == "get")
                {
                    GetOptions opt = iOpt as GetOptions;
                    Console.Write("Getting records is not implemented yet!");
                    return;
                }

                    /*
                    recsearchlist = FindRecords(userecords);


                    bool changed = false;
                    foreach (string recname in userecords)
                    {
                        ARZRecord rec = rectable[recsearchlist[recname]];
                        foreach (string emod in modentries)
                        {
                            string ename = emod.Split(',')[0];
                            ARZEntry entry = rec.entries.Find(e => strtable[e.dstrid] == ename);
                            Console.WriteLine("Found:");
                            Console.WriteLine(entry);
                            // Modify a record:
                            if (!entry.TryAssign(emod)) return; // Try assigning data
                            if (entry.changed)
                            {
                                rec.PackData();
                                changed = true;
                            }
                        }
                    }
                    */
                    // }
                    return;
            } else {
                PrintUsage();
                return;
            }
            // Read file
            /*            
                                    if (args.Length < 3) { PrintUsage(); return; }
                                    int carg = 0;
                                    ArzFile = args[carg++];
                                    if (args[carg++].ToLower() == "-o")
                                        OutputFile = args[carg++];
                                    else
                                        OutputFile = ArzFile;

                                    List<string> userecords = new List<string>();
                                    userecords.Add(args[carg++]);

                                    List<string> modentries = new List<string>();
                                    for (; carg < args.Length; carg++) {
                                        modentries.Add(args[carg]);
                                    }
            */
            //*/
            // Find a record:
            // ARZRecord first = rectable[recsearchlist[strsearchlist["records/watertype/noisetextures/smoothwaves.dbr"]]];
            //            List<string> userecords = new List<string>(new string[] { "records/game/gameengine.dbr" });
            //            recsearchlist = FindRecords(userecords);

            // ARZRecord first = rectable[recsearchlist["records/game/gameengine.dbr"]];
            // int itemstrid = strsearchlist["playerDevotionCap"];
            // ARZEntry item = first.entries.Find(e => strtable[e.dstrid] == "playerDevotionCap");
            // WriteEntry(item);
            /*            Console.WriteLine("File: {0}", strtable[first.rfid]);
                        foreach (ARZEntry etr in first.entries)
                        {
                            Console.WriteLine(etr);
                        }
            */
/*            if (changed)
            {
                SaveData(OutputFile);
                Console.WriteLine("Success, press any key to exit ...");
            } else
            {
                Console.WriteLine("No changes, files untouched, press any key to exit ...");
            }
            Console.ReadKey(true);*/
        }

        static char Ask(string question, string answers, char adefault) {
            Console.Write(question);
            ConsoleKeyInfo key = Console.ReadKey();
            while (!answers.Contains(key.KeyChar) && key.Key != ConsoleKey.Enter)
                key = Console.ReadKey();
            Console.WriteLine();
            if (key.Key == ConsoleKey.Enter) return adefault;
            return key.KeyChar;
        }

        static bool LoadFile(string mfile) {
            if (!File.Exists(mfile))
            {
                Console.WriteLine("ERROR: Input file \"{0}\" does not exist!", Path.GetFullPath(mfile));
                return false;
            }
            mdata = File.ReadAllBytes(mfile);
            // TODO: Move this to separate function
            using (MemoryStream memory = new MemoryStream(mdata))
            {
                using (BinaryReader reader = new BinaryReader(memory))
                {
                    memory.Seek(0, SeekOrigin.Begin);
                    ARZHeader header = new ARZHeader(reader);
                    // header.ReadBytes(reader);
                    /* DEBUG:
                    Console.WriteLine("Unknown: {0}; Version: {1}; RecordTableStart: {2}; RecordTableSize: {3}; RecordTableEntries: {4}; StringTableStart: {5}; StringTableSize: {6};",
                        header.Unknown, header.Version, header.RecordTableStart, header.RecordTableSize, header.RecordTableEntries, header.StringTableStart, header.StringTableSize);
                    */
                    if (header.Unknown != 2 || header.Version != 3)
                    {
                        Console.WriteLine("ERROR: database file \"{0}\" has invalid header (magick does not match), check if it's valid .arz file.");
                        return false;
                    }


                    // read footer
                    memory.Seek(-16, SeekOrigin.End);
                    footer = reader.ReadBytes(16);
                    
                    memory.Seek(header.StringTableStart, SeekOrigin.Begin);
                    strtable = ReadStringTable(reader, header.StringTableSize);

                    // Create searchable string list
                    /*
                    strsearchlist = new SortedList<string, int>();
                    strsearchlist.Capacity = strtable.Count;
                    for (int i = 0; i < strtable.Count; i++) {
                        strsearchlist.Add(strtable[i], i);
                    }
                    */

                    memory.Seek(header.RecordTableStart, SeekOrigin.Begin);
                    // read record table
                    rectable = ReadRecordTable(reader, header.RecordTableEntries);

                    // fill record data (unzip and read)
                    /* recsearchlist = new SortedList<int, int>();
                    recsearchlist.Capacity = rectable.Count;
                    */
                    ARZRecord rec = null;

                    for (int i = 0; i < rectable.Count; i++)
                    // foreach (ARZRecord rec in rectable)
                    {
                        rec = rectable[i];
                        memory.Seek(ARZHeader.HEADER_SIZE + rec.rdOffset, SeekOrigin.Begin);
                        rec.ReadData(reader);
                        // Generate lookup list
                        // recsearchlist.Add(rec.rfid, i);
                    }
                    // TODO: Check how record type name is determined, also what entry value types are used throughout database
                }
            }
            mdata = null;
            return true;
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("{0} <set|get|extract> <suboptions>\n", Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location));
            Console.Write("set <input file> [-o <output file>] [-y] [-r <record> {-e <entry1> [<entry2> ...] | -f <record file>}] [-p <patchfile1> [<patchfile2> ...]] [-b <base folder> [-s <subfolder>]] ");
            var ht = new HelpText();
            ht.AddDashesToOption = true;
            ht.AddOptions(new SetOptions());
            Console.WriteLine(ht);
            Console.Write("get <input file> -r <record> [-e <entry1> [<entry2 ...>]]");
            Console.Write("Not implemented yet!;");
            ht = new HelpText();
            ht.AddDashesToOption = true;
            ht.AddOptions(new GetOptions());
            Console.WriteLine(ht);
            Console.Write("extract <input file> [<output path>] [-y]");
            ht = new HelpText();
            ht.AddDashesToOption = true;
            ht.AddOptions(new ExtractOptions());
            Console.WriteLine(ht);
            // Console.ReadKey(true);
        }

        static bool ModAllEntries(ARZRecord rec, List<string> entries, out bool changed)
        {
            changed = false;
            SortedDictionary<string, int> esearch = null;
            //*
            if (rec.entries.Count > 700)
            {
                esearch = new SortedDictionary<string, int>();
                for (int i = 0; i < rec.entries.Count; i++) {
                    esearch.Add(strtable[rec.entries[i].dstrid], i);
                }
            }  
            /*
            Not really faster with lookup dictionary, not sure why
            */ 
            for (int i = 0; i < entries.Count; i++)
            {
                string nmod = entries[i].Trim();
                if (!nmod.EndsWith(",")) nmod += ',';
                string ename = nmod.Split(',')[0];
                // Guesstimate first, then search:
                ARZEntry entry = null;
                if (strtable[rec.entries[i].dstrid] == ename) // Guess that record id matches entry id
                    entry = rec.entries[i]; // Guessed well 
                else
                {
                    //*
                    if (esearch != null)
                        entry = rec.entries[esearch[ename]]; // TODO: May error out, Catch error here or check for key existence before
                    else
                    {
                        entry = rec.entries.Find(e => strtable[e.dstrid] == ename); // Full search
                    }
                    //*/
                    // entry = rec.entries.Find(e => strtable[e.dstrid] == ename); // Full search
                }
                if (entry == null)
                {
                    Console.WriteLine("Record \"{0}\" does not contain entry \"{1}\"", strtable[rec.rfid], ename);
                    return false;
                }
                // Modify a record:
                // DEBUG:
                // Console.WriteLine("{0} -> {1}", entry, nmod);
                if (!entry.TryAssign(nmod))
                {
                    // DEBUG:
                    Console.WriteLine("Record \"{0}\" error when setting {1} -> {2}", strtable[rec.rfid], entry, nmod);
                    return false; // Try assigning data
                }
                // DEBUG: Should use verbosity level flags like none, changed, unchanged, all (or two bools)
                // Console.WriteLine(entry.changed ? " [changed]" : " [no change]");
                changed |= entry.changed;
            }
            return true;
        }


        static SortedList<string, int> FindRecords(List<string> recordnames)
        {
            SortedList<string, int> recmap = new SortedList<string, int>();
            recmap.Capacity = recordnames.Count;
            foreach (string rname in recordnames)
                recmap[rname] = -1;
            for (int i = 0; i < rectable.Count; i++)
            {
                if (recmap.ContainsKey(strtable[rectable[i].rfid])) // Only listed keys need to be searched for
                    recmap[strtable[rectable[i].rfid]] = i;
            }
            return recmap;
        }

        static void SaveData(string filename) {
            // Calculate sizes for proper memory allocation
            int strtablesize = 4 + strtable.Count * 4;
            foreach (string s in strtable)
                strtablesize += s.Length;
            int rectablesize = 28 * rectable.Count;
            int recdatasize = 0;
            foreach (ARZRecord r in rectable)
            {
                rectablesize += r.rtype.Length;
                recdatasize += r.rdSizeCompressed;
            }

            using (MemoryStream mrdata = new MemoryStream(recdatasize)) 
            using (MemoryStream mrtable = new MemoryStream(rectablesize))
            using (MemoryStream mstable = new MemoryStream(strtablesize))
            using (MemoryStream mheader = new MemoryStream(ARZHeader.HEADER_SIZE)) {                
                using (BinaryWriter brdata = new BinaryWriter(mrdata))
                using (BinaryWriter brtable = new BinaryWriter(mrtable))
                using (BinaryWriter bstable = new BinaryWriter(mstable))
                using (BinaryWriter bwheader = new BinaryWriter(mheader))
                {
                    WriteRecords(brtable, brdata);
                    WriteStringTable(bstable);
                    // Write to file
                    using (FileStream fs = new FileStream(filename, FileMode.Create))
                    using (BinaryWriter bf = new BinaryWriter(fs))
                    {
                        // Calc header                         
                        int rtablestart = ARZHeader.HEADER_SIZE + (int)mrdata.Length;
                        int rtablesize = (int)mrtable.Length;
                        int rtableentr = rectable.Count;
                        int stablestart = rtablestart + rtablesize;
                        int stablesize = (int)mstable.Length;
                        // Write header magick
                        bwheader.Write((ushort)0x02);
                        bwheader.Write((ushort)0x03);
                        // Write header values
                        bwheader.Write(rtablestart);
                        bwheader.Write(rtablesize);
                        bwheader.Write(rtableentr);
                        bwheader.Write(stablestart);
                        bwheader.Write(stablesize);
                        byte[] bufheader = mheader.GetBuffer();

                        // Write header
                        bf.Write(bufheader);
                        // Calculate hashes:

                        Adler32.checksum = 1;
                        byte[] bufrdata = mrdata.GetBuffer();
                        uint csrdata = Adler32.ComputeHash(bufrdata, 0, bufrdata.Length);
                        // Console.WriteLine("Data 0x{0:X4}", csrdata);
                        Adler32.checksum = 1;
                        byte[] bufrtable = mrtable.GetBuffer();
                        uint csrtable = Adler32.ComputeHash(bufrtable, 0, bufrtable.Length);
                        // Console.WriteLine("Record Table 0x{0:X4}", csrtable);
                        Adler32.checksum = 1;
                        byte[] bufstable = mstable.GetBuffer();
                        uint csstable = Adler32.ComputeHash(bufstable, 0, bufstable.Length);
                        // Console.WriteLine("String Table 0x{0:X4}", csstable);
                        // Checksum for whole file w/o footer
                        Adler32.checksum = 1;
                        Adler32.ComputeHash(bufheader, 0, bufheader.Length); // Header
                        Adler32.ComputeHash(bufrdata, 0, bufrdata.Length); // Data
                        Adler32.ComputeHash(bufrtable, 0, bufrtable.Length); // Data Table
                        uint csall = Adler32.ComputeHash(bufstable, 0, bufstable.Length); // String Table
                        // Console.WriteLine("All 0x{0:X4}", Adler32.checksum);

                        // Write data
                        bf.Write(bufrdata); // Record data
                        bf.Write(bufrtable); // Record table
                        bf.Write(bufstable); // String table                      

                        // Write footer
                        bf.Write(csall);
                        bf.Write(csstable);
                        bf.Write(csrdata);
                        bf.Write(csrtable);
                        // bf.Write(footer);
                        /*
                        Unknown = bytes.ReadUInt16();
                        Version = bytes.ReadUInt16();
                        RecordTableStart = bytes.ReadUInt32();
                        RecordTableSize = bytes.ReadUInt32();
                        RecordTableEntries = bytes.ReadUInt32();
                        StringTableStart = bytes.ReadUInt32();
                        StringTableSize = bytes.ReadUInt32();
                        */
                    }
                }
                // DEBUG:
                // Console.ReadKey(true);
            }
        }

        static class Adler32
        {
            public static uint checksum = 1;

            /// <summary>Performs the hash algorithm on given data array.</summary>
            /// <param name="bytesArray">Input data.</param>
            /// <param name="byteStart">The position to begin reading from.</param>
            /// <param name="bytesToRead">How many bytes in the bytesArray to read.</param>
            public static uint ComputeHash(byte[] bytesArray, int byteStart, int bytesToRead)
            {
                int n;
                uint s1 = checksum & 0xFFFF;
                uint s2 = checksum >> 16;

                while (bytesToRead > 0)
                {
                    n = (3800 > bytesToRead) ? bytesToRead : 3800;
                    bytesToRead -= n;

                    while (--n >= 0)
                    {
                        s1 = s1 + (uint)(bytesArray[byteStart++] & 0xFF);
                        s2 = s2 + s1;
                    }

                    s1 %= 65521;
                    s2 %= 65521;
                }

                checksum = (s2 << 16) | s1;
                return checksum;
            }
        }

        class VerbOptions
        {
            [VerbOption("set", HelpText = "Set values in database")]
            public SetOptions SetVerb { get; set; }
            [VerbOption("get", HelpText = "Get records/values in database")]
            public GetOptions GetVerb { get; set; }
            [VerbOption("extract", HelpText = "Extract records from database")]
            public ExtractOptions ExtractVerb { get; set; }
            [HelpOption]
            public string GetUsage()
            {
                return HelpText.AutoBuild(this, (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
            }
        }

        // [Verb("set", HelpText = "Set entries in specified record")]
        class SetOptions
        {
            [ValueOption(0)]
            public string InputFile { get; set; }

            [Option('o', "output", HelpText="Write changes to specified file. NOTE: If not provided - overwrites input file, make backups!")]
            public string OutputFile { get; set; }

            [Option('y', "overwrite", HelpText="Force overwrite output file, be careful, make backups!")]
            public bool ForceOverwrite { get; set; }

            [Option('b', "base", HelpText="Pack records based at this folder (usually one which has /records/ folder)")]
            public string SetBase { get; set; }

            [Option('s', "subfolder", HelpText="Pack only records in subfolder and below, relative to base being packed, like \"records\\game\\\"")]
            public string SetSubfolder { get; set; }

            [Option('r', "record", HelpText="Record to be changed, format example: \"records/game/gameengine.dbr\" if it contains spaces - enclose in double quotes (\")")]
            public string SetRecord { get; set; }

            [OptionArray('p', "patch", HelpText="Use patch file to update multiple records and entries")]
            public string[] SetPatches { get; set; }

            [Option('f', "file", HelpText="Record file (*.dbr) to be assigned to the record.")]
            public string SetFile { get; set; }

            [OptionArray('e', "entries", HelpText= "Entry names with values. Entry example: \"playerDevotionCap,56,\", Multiple entries are separated by spaces, if entry contains spaces it must be enclosed in doubleqoutes (\").")]
            public string[] SetEntries { get; set; }

            // List<string> modentries = new List<string>();
            [HelpOption]
            public string GetUsage()
            {
                return HelpText.AutoBuild(this, (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
            }

            // public string ShortUsage()
            // {           }
        }

        // [Verb("get", HelpText = "Get all/specific entries in a record")]
        class GetOptions {
            [ValueOption(0)]
            public string InputFile { get; set; }
            [HelpOption]
            public string GetUsage()
            {
                return HelpText.AutoBuild(this, (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
            }
        }

        class ExtractOptions
        {
            [ValueOption(0)]
            public string InputFile { get; set; }
            [ValueOption(1)]
            public string OutputPath { get; set; }
            [Option('y', "overwrite", HelpText = "Force overwrite files in target folder, be careful, make backups!")]
            public bool ForceOverwrite { get; set; }
            [HelpOption]
            public string GetUsage()
            {
                return HelpText.AutoBuild(this, (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
            }
        }

        static List<string> ReadStringTable(BinaryReader br, uint size)
        {
            List<string> slist = new List<string>();
            slist.Capacity = 0;
            int pos = 0;
            while (pos < size) {
                int count = br.ReadInt32(); pos += 4; // Read Count
                // DEBUG:
                // Console.WriteLine("Block at pos: {0}; Count: {1}", pos, count);
                slist.Capacity += count;
                for (int i = 0; i < count; i++) {
                    int length = br.ReadInt32(); pos += 4;
                    // Console.Write("String at pos: {0}; Length: {1}; ", pos, length);
                    slist.Add(new string(br.ReadChars(length))); pos += length;
                    // Console.WriteLine("Value = \"{0}\"", str);
                }
            }
            return slist;
        }

        static List<ARZRecord> ReadRecordTable(BinaryReader br, uint rcount) {
            List<ARZRecord> rlist = new List<ARZRecord>();
            rlist.Capacity = (int)rcount;
            for (int i = 0; i < rcount; i++)
            {
                rlist.Add(new ARZRecord(br));
                // ARZRecord rec = rlist.Last();
                // Console.WriteLine("File: {0}; Type: {1}; Offset: {2}; Compressed/Decompressed: {3}/{4}; File Time: {5};", strtable[rec.rfid], rec.rtype, rec.rdOffset, rec.rdSizeCompressed, rec.rdSizeDecompressed, rec.rdFileTime);
            }

            return rlist;
        }

        static void WriteRecords(BinaryWriter rtable, BinaryWriter rdata)
        {
            int doffset = 0;
            foreach (ARZRecord rec in rectable) {
                rec.WriteRecord(rtable, doffset);
                rdata.Write(rec.cData);
                doffset += rec.rdSizeCompressed;
            }
        }

        static void WriteStringTable(BinaryWriter bw)
        {
            bw.Write((int)strtable.Count);
            foreach (string s in strtable)
            {
                bw.Write((int)s.Length);
                bw.Write(s.ToCharArray());
            }
        }

        public static void BuildStringReferences()
        {
            strrefcount = new List<int>(new int[strtable.Count]);
            // Buld refernce count list:
            foreach (ARZRecord r in rectable)
            {
                strrefcount[r.rfid]++;
                foreach (ARZEntry e in r.entries)
                {
                    strrefcount[e.dstrid]++;
                    if (e.dtype == 2)
                        foreach (int v in e.values) strrefcount[v]++;
                }
            }
        }

        public static void BuildStringSearchList()
        {
            strsearchlist = new SortedDictionary<string, int>();
            for (int i = 0; i < strtable.Count; i++)
            {
                strsearchlist.Add(strtable[i], i);
            }
        }

        public static int ModifyString(int index, string newvalue)
        // All string modifications should pass through this function, it takes care of reference counters
        // index - string being referenced by variable, if index < 0 means no variable is referencing, in other words adding new reference
        // newvalue - new string value
        {
            if (index >= 0 && strtable[index] == newvalue) return index;

            if (strrefcount == null) { // Build reference counters or first access
                BuildStringReferences();
            }

            if (index < 0 || strrefcount[index] > 0) // Find existing or add new, replace
            {
                if (strsearchlist == null) // Build string search list on first access
                    BuildStringSearchList();

                // Find if exists
                if (strsearchlist.ContainsKey(newvalue))
                {
                    if (index >= 0) strrefcount[index]--; // Dereference original, if there's one
                    int newidx = strsearchlist[newvalue];
                    strrefcount[newidx]++; // Reference new
                    return newidx;
                }
                else // New value
                {
                    if (index >= 0 && strrefcount[index] == 1) // Has reference and it's single - modify in place
                    {
                        strtable[index] = newvalue;
                        return index;
                    }
                    else // More references, or no reference - append new value
                    {
                        if (index >= 0) strrefcount[index]--; // Dereference original, if there's one
                        strtable.Add(newvalue); // Add item
                        strrefcount.Add(1); // Add referece
                        strsearchlist.Add(newvalue, strtable.Count - 1); // Add to searchlist
                        return strtable.Count - 1;
                    }
                }
            }
            else {
                // DEBUG: Execution should not end up here
                Console.WriteLine("WARN: Modifying unreferenced string \"{0}\" at index {1}", strtable[index], index);
                return -1;
            }
        }

        public static void CompactStringlist()
        // This function has some critical code section if it fails in the middle of remapping we will end up with untidy state of reference/entry data structure
        {
            // Find all unreferenced strings
            if (strrefcount == null) BuildStringReferences();
            // Check for needs compacting
            bool needscompacting = false;
            for (int i = 0; i < strrefcount.Count; i++)
            {
                needscompacting = strrefcount[i] == 0 && strtable[i] != ""; // If unreferenced string, but ignoring empty ones
                if (needscompacting) break;
            }
            if (!needscompacting) return;
            Console.WriteLine("Compacting strings ...");
            // Do the job
            List<string> target = new List<string>();
            int[] map = new int[strtable.Count];
            target.Capacity = strtable.Capacity;
            int ti = 0;
            for (int si = 0; si < strtable.Count; si++)
            {
                if (strrefcount[si] > 0 || strtable[si] == "")
                {
                    target.Add(strtable[si]);
                    map[si] = ti;
                    ti++;
                }
                else
                    map[si] = -1;
            }
            
            // Got targetlist + remapping info, traverse all structures and remap strings
            foreach (ARZRecord r in rectable)
            {
                r.rfid = map[r.rfid];
                foreach (ARZEntry e in r.entries)
                {
                    e.dstrid = map[e.dstrid];
                    if (e.dtype == 2)
                        for (int i = 0; i < e.values.Length; i++)
                            e.values[i] = map[e.values[i]]; 
                }
            }

            strtable = target; // Replace string table with new one
        }

    }

    class ARZRecord {
        public int rfid;
        public string rtype;
        public int rdOffset;
        public int rdSizeCompressed;
        public int rdSizeDecompressed;
        public DateTime rdFileTime;
        public byte[] cData;
        public byte[] aData;
        public List<ARZEntry> entries = null;
        public ARZRecord(BinaryReader rdata)
        {
            ReadBytes(rdata);
        }
        public void ReadBytes(BinaryReader rdata)
        {
            // read record info
            rfid = rdata.ReadInt32();
            // string record_file = strtable[rfid];
            int rtypelen = rdata.ReadInt32();
            rtype = new string(rdata.ReadChars(rtypelen));
            rdOffset = rdata.ReadInt32();
            rdSizeCompressed = rdata.ReadInt32();
            rdSizeDecompressed = rdata.ReadInt32();
            rdFileTime = DateTime.FromFileTimeUtc(rdata.ReadInt64());
        }        

        public List<ARZEntry> ReadData(BinaryReader brdata)
        {
            cData = brdata.ReadBytes(rdSizeCompressed);
            aData = LZ4Codec.Decode(cData, 0, rdSizeCompressed, rdSizeDecompressed);
            entries = new List<ARZEntry>();
            // entries.Capacity = ((rdSizeDecompressed - 8) / 4); // Wrong capacity
            using (MemoryStream eMem = new MemoryStream(aData))
            {
                using (BinaryReader eDbr = new BinaryReader(eMem))
                {
                    while (eMem.Position < eMem.Length)
                        entries.Add(new ARZEntry(eDbr));
                }
            }
            aData = null; // Free up memory
            return entries;
        }

        public void PackData() {
            int datasize = entries.Count * 8; // Headers
            foreach (ARZEntry e in entries)
                datasize += e.values.Length * 4; // + Data
            using (MemoryStream mStream = new MemoryStream(datasize)) {
                using (BinaryWriter bWriter = new BinaryWriter(mStream))
                {
                    mStream.Seek(0, SeekOrigin.Begin);
                    foreach (ARZEntry e in entries) {
                        e.WriteBytes(bWriter);
                    }
                }
        
                // Replace data
                aData = mStream.GetBuffer();
                rdSizeDecompressed = aData.Length;
                cData = LZ4Codec.Encode(aData, 0, rdSizeDecompressed);
                aData = null;
                rdSizeCompressed = cData.Length;
            }
        }

        public void WriteRecord(BinaryWriter rtable, int dataoffset)
        {
            rdOffset = dataoffset;
            rtable.Write(rfid);
            rtable.Write((int)rtype.Length);
            rtable.Write(rtype.ToCharArray());
            rtable.Write(rdOffset);
            rtable.Write(rdSizeCompressed);
            rtable.Write(rdSizeDecompressed);
            rtable.Write(rdFileTime.ToFileTimeUtc());
        }
        
    }

    public class ARZEntry {
        public ushort dtype;
        public ushort dcount;
        public int dstrid;
        public int[] values;
        public bool changed = false; // TODO: everhead
        static List<string> strtable = null;
        // static SortedList<string, int> strsearchlist = null;
        public static List<string> StrTable { get { if (strtable == null) strtable = Program.strtable; return strtable; } set { strtable = value; } }
        // public static SortedList<string, int> StrSearchList { get { if (strsearchlist == null) strsearchlist = Program.strsearchlist; return strsearchlist; } set { strsearchlist = value; } }

        public ARZEntry(BinaryReader edata)
        {
            ReadBytes(edata);
        }

        public void ReadBytes(BinaryReader edata) {
            // header
            dtype = edata.ReadUInt16();
            dcount = edata.ReadUInt16();
            dstrid = edata.ReadInt32();
            // read all entries
            values = new int[dcount];
            for (int i = 0; i < dcount; i++)
            {
                values[i] = edata.ReadInt32();
            }
        }

        public void WriteBytes(BinaryWriter edata)
        {
            edata.Write(dtype);
            edata.Write(dcount);
            edata.Write(dstrid);
            foreach (int v in values)
              edata.Write(v);
        }

        public int AsInt(int eid)
        {
            return values[eid];
        }

        public float AsFloat(int eid)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(values[eid]), 0);
        }

        public string AsString(int eid, List<string> strtable) {
            return strtable[values[eid]];
        }

        public bool TryAssign(string fromstr) {
            string[] estrs = fromstr.Split(',');
            if (estrs.Length != 3)
            {
                Console.WriteLine("Malformed assignment string \"{0}\"", fromstr);
                return false;
            }
            string entryname = estrs[0];
            // int entryid = StrSearchList[entryname];
            if (StrTable[dstrid] != entryname)
            {
                Console.WriteLine("Cannot assign \"{0}\" to \"{1}\" field (entry names differ).", entryname, StrTable[dstrid]);
                return false;
            }
            string[] strs = estrs[1].Split(';');            
            
            if (strs.Length != values.Length)
            {
                // Console.WriteLine("Array size mismatch: assigning {0} values to {1} array size", strs.Length, values.Length);
                // TODO: All kinds of weirdiness with string packing, simplest solution would be ignoring string variable parsing altogether, but if we want to repack correctly we'll need this
                if (dtype == 2) // If it is string it may be stored as single value, or may have sequence of empty fields which are condensed to a single entry with ;'s inside
                {
                    if (values.Length == 1)
                    {
                        strs = new string[1] { estrs[1] };
                    } else
                    {
                        // This is when it get's weird:
                        // try compacting multiple empty strings to ;;
                        List<string> cstrs = new List<string>();
                        string accum = "";
                        for (int i = 0; i < strs.Length; i++) // 
                        {
                            if (strs[i] == "") { 
                                if (i + 1 < strs.Length && strs[i + 1] == "") accum += ";"; // Ignore last ; as it is a separator
                            }
                            else
                            {
                                if (accum != "")
                                {
                                    cstrs.Add(accum);
                                    accum = "";
                                }
                                cstrs.Add(strs[i]);
                            }
                        }
                        if (accum != "") cstrs.Add(accum);
                        strs = cstrs.ToArray<string>();
                    }
                }                
            }

            // DEBUG: array size changing
            //*
            if (strs.Length != values.Length)            {
                Console.WriteLine("WARN: Array size mismatch: assigning {0} values to array of size {1}.\nSetting: {2} -> {3}", strs.Length, values.Length, this, fromstr);
                // return false;
            }//*/

            float fval = (float)0.0;
            int[] nvalues = new int[strs.Length];

            bool strmodified = false;
            for (int i = 0; i < strs.Length; i++) {
                switch (dtype)
                {
                    case 0: // TODO: Move entry types to static Consts for clarity
                        if (!int.TryParse(strs[i], out nvalues[i]))
                        {
                            Console.WriteLine("Error parsing integer value #{0}=\"{1}\"", i, strs[i]);
                            return false;
                        }
                        break;
                    case 1:
                        if (!float.TryParse(strs[i], out fval))
                        {
                            Console.WriteLine("Error parsing float value #{0}=\"{1}\"", i, strs[i]);
                            return false;
                        }
                        nvalues[i] = BitConverter.ToInt32(BitConverter.GetBytes(fval), 0);
                        break;
                    case 2: // String
                        if (i < values.Length)
                            if (strs[i] != StrTable[values[i]])
                            {
                                string origstr = StrTable[values[i]];
                                nvalues[i] = Program.ModifyString(values[i], strs[i]);
                                // Console.WriteLine("Changing string \"{0}\" to \"{1}\", Index {2} -> {3}", origstr, strs[i], values[i], nvalues[i]); // DEBUG
                                strmodified = true;
                            }
                            else
                                nvalues[i] = values[i];
                        else { // New string
                            nvalues[i] = Program.ModifyString(-1, strs[i]);
                            // Console.WriteLine("Adding string \"{0}\", Index {1}", strs[i], nvalues[i]); // DEBUG
                        }
                        break;
                    case 3:
                        if (!int.TryParse(strs[i], out nvalues[i]) || nvalues[i] > 1)
                        {
                            Console.WriteLine("Error parsing boolean value #{0}=\"{1}\"", i - 1, strs[i]);
                            return false;
                        }
                        break;
                    default:
                        Console.WriteLine("Unknown data type in database"); // TODO: make more informative
                        return false;
                }
            }
            if (strmodified || values.Length != nvalues.Length || !values.SequenceEqual(nvalues))
            {
                values = nvalues;
                changed = true;
            }
            return true;
        }

        public override string ToString()
        {
            if (StrTable == null) return "";
            StringBuilder sb = new StringBuilder();
            sb.Append(StrTable[dstrid]).Append(',');
            bool firstentry = true;
            foreach (int value in values)
            {
                if (!firstentry) sb.Append(";");
                switch (dtype)
                {
                    case 0:
                    case 3:
                    default:
                        sb.Append(value); // values are signed!
                        break;
                    case 1:
                        sb.AppendFormat("{0:0.000000}", BitConverter.ToSingle(BitConverter.GetBytes(value), 0));
                        break;
                    case 2:
                        sb.Append(strtable[value]);
                        // Console.Write(strtable[value]);
                        break;
                }
                firstentry = false;
            }
            sb.Append(',');
            return sb.ToString();
        }
    }


    class ARZHeader {
        public const int HEADER_SIZE = 24;
        public ushort Unknown;
        public ushort Version;
        public uint RecordTableStart;
        public uint RecordTableSize;
        public uint RecordTableEntries;
        public uint StringTableStart;
        public uint StringTableSize;
        public ARZHeader() {
        }

        public ARZHeader(BinaryReader bytes) {
            ReadBytes(bytes);
        }

        public void ReadBytes(BinaryReader bytes) {
            Unknown = bytes.ReadUInt16();
            Version = bytes.ReadUInt16();
            RecordTableStart = bytes.ReadUInt32();
            RecordTableSize = bytes.ReadUInt32();
            RecordTableEntries = bytes.ReadUInt32();
            StringTableStart = bytes.ReadUInt32();
            StringTableSize = bytes.ReadUInt32();
        }
    }
}
