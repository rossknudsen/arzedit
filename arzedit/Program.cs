using System;
using System.IO;
using System.Collections.Generic;
using LZ4;
using System.Linq;
using System.Text;
using CommandLine;
using CommandLine.Text;

namespace arzedit
{   // TODO: Add resource base when packing and crosscheck if files exist, ideal would be just point to mod folder and it does all work
    class Program
    {
        // static string ArzFile = "database.arz";
        // static string OutputFile = "database2.arz";
        const string VERSION = "0.1b4";
        static byte[] mdata = null;
        static byte[] footer = new byte[16];
        //TODO: Move those static fields to an instance object, arz file should be instance of an object, not stored in static fields in main program
        public static List<string> strtable = null; 
        public static List<int> strrefcount = null;
        public static SortedDictionary<string, int> strsearchlist = null;
        public static HashSet<string> resfiles = null;
        public static HashSet<string> dbrfiles = null;
        static List<ARZRecord> rectable = null;
        static SortedList<string, int> recsearchlist = null;
        static int Main(string[] args)
        {
            var voptions = new VerbOptions();
            // ParserResult<object> result = CommandLine.Parser.Default.ParseArguments<SetOptions, GetOptions>(args);
            string iVerb = ""; object iOpt = null;
            if (args.Length > 0 && Parser.Default.ParseArguments(args, voptions, (verb, subOptions) => {
                iVerb = verb; iOpt = subOptions;
            }))
            {
                if (iVerb == "set")
                {
                    return ProcessSetVerb(iOpt as SetOptions);
                }
                else if (iVerb == "extract")
                {
                    return ProcessExtractVerb(iOpt as ExtractOptions);
                }
                else if (iVerb == "pack")
                {
                    return ProcessPackVerb(iOpt as PackOptions);
                }
                else if (iVerb == "get")
                {
                    GetOptions opt = iOpt as GetOptions;
                    Console.WriteLine("Getting records is not implemented yet!");
                    return 1;
                }
                else if (iVerb == "unarc")
                {
                    return ProcessUnarcVerb(iOpt as UnarcOptions);
                }
                else if (iVerb == "arc")
                {
                    return ProcessArcVerb(iOpt as ArcOptions);
                }
                return 1; // Should not ever be here, but just in case unknown verb pops up
            } else {
                PrintUsage();
                return 1;
            }
        }

        static int ProcessSetVerb(SetOptions opt)
        {
            if (string.IsNullOrEmpty(opt.OutputFile)) opt.OutputFile = opt.InputFile;

            // List<string> entries = new List<string>(opt.SetEntries);
            // DEBUG:
            // Console.WriteLine("In: {0}; Out: {1}; Rec: {2}; Entries #: {3}", opt.InputFile, opt.OutputFile, opt.SetRecord, entries.Count);
            Console.Write("Parsing database ... ");
            DateTime start = DateTime.Now;
            if (!LoadFile(opt.InputFile)) return 1;
            DateTime end = DateTime.Now;
            Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

            // Change everything under the base
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
                    catch (KeyNotFoundException)
                    {
                        Console.WriteLine("Error packing record file \"{0}\": as record {1} - no such record in database!", dbrfile, recordname);
                        return 1;
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
                        return 1;
                    }
                    progress.Report(((double)ci++) / allfiles.Length);
                    bchanged |= brchanged;
                }
                progress.Dispose();
                progress = null;
                end = DateTime.Now;
                Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));
            }

            // Now patches
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
                    return 1;
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

            // Save changes
            if (pchanged || bchanged || fchanged || echanged)
            {
                if (fchanged || echanged && rec != null) rec.PackData();
                if (!AskSaveData(opt.OutputFile, opt.ForceOverwrite)) return 1;
            }
            else
            {
                // SaveData(OutputFile); // DEBUG: REMOVE
                Console.WriteLine("No changes, files untouched");
            }
            return 0;
        } // ProcessSetVerb

        static int ProcessExtractVerb(ExtractOptions opt)
        {
            Console.Write("Parsing database ... ");
            DateTime start = DateTime.Now;
            if (!LoadFile(opt.InputFile)) return 1;
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
                        if (ans == 'c')
                        {
                            Console.WriteLine("Aborted by user");
                            return 1;
                        };
                        progress.SetHidden(false);
                        overwriteall = ans == 'a';
                    }

                    if (!fileexists || overwriteall || ans == 'y')
                    {
                        try
                        {
                            using (FileStream fs = new FileStream(filename, FileMode.Create))
                            using (StreamWriter sr = new StreamWriter(fs))
                            {
                                sr.NewLine = "\n";
                                foreach (ARZEntry etr in rec.entries)
                                {
                                    string estring = etr.ToString();
                                    if (estring.Contains('\n') || estring.Contains(Environment.NewLine))
                                    {
                                        Console.WriteLine("Record \"{0}\" entry \"{1}\" contains newline(s), fixing.", strtable[rec.rfid], strtable[etr.dstrid]);
                                        estring = System.Text.RegularExpressions.Regex.Replace(estring, @"\r\n?|\n", "");
                                    }
                                    sr.WriteLine(estring);
                                }
                            }
                            // Set Date:
                            File.SetCreationTime(filename, rec.rdFileTime); // TODO: Check if this is needed
                        } catch (Exception e)
                        {
                            Console.WriteLine("Error writing file \"{0}\", Message: ", filename, e.Message);
                            return 1;
                        }
                    }
                    progress.Report(((double)ci++) / rectable.Count);
                }
            }
            end = DateTime.Now;
            Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

            return 0;
        } // ProcessExtractVerb

        static int ProcessPackVerb(PackOptions opt)
        {
            string packfolder = Path.GetFullPath(opt.InputPath);
            if (string.IsNullOrEmpty(opt.OutputFile))
            {
                Console.WriteLine("Please specify output file as second parameter!");
                PrintUsage();
                return 1;
            }

            // Process templates
            string[] tfolders = null;
            if (opt.TemplatePaths != null)
            {
                tfolders = opt.TemplatePaths;
            }
            else
            {
                tfolders = new string[1] { opt.InputPath };
            }

            Console.Write("Parsing templates ... ");
            DateTime start = DateTime.Now;

            Dictionary<string, TemplateNode> templates = null;
            try
            {
                templates = BuildTemplateDict(tfolders);
            } catch (Exception e)
            {
                Console.WriteLine("Error parsing templates, reason - {0}\nStackTrace:\n{1}", e.Message, e.StackTrace);
                return 1;
            }

            DateTime end = DateTime.Now;
            Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

            // Got templates

            // create empty strtable

            // Check for peeking
            bool peek = false;
            List<string> peekstrtable = null;
            List<ARZRecord> peekrectable = null;
            if (!string.IsNullOrEmpty(opt.PeekFile))
            {
                LoadFile(opt.PeekFile);
                peekstrtable = strtable;
                peekrectable = rectable;
                peek = true;
            }

            strtable = new List<string>();
            rectable = new List<ARZRecord>();
            if (opt.CheckReferences)
            {
                resfiles = BuildResourceSet(Path.Combine(packfolder, "resources"));
            }

            // Pack records
            Console.Write("Packing ... ");
            start = DateTime.Now;
            string[] alldbrs = null;
            try
            {
                alldbrs = Directory.GetFiles(packfolder, "*.dbr", SearchOption.AllDirectories);
            }
            catch (Exception e) {
                Console.WriteLine("Error listing *.dbr files, reason - {0} ", e.Message);
                return 1;
            }

            if (opt.CheckReferences)
            {
                dbrfiles = new HashSet<string>();
                foreach (string dbrfile in alldbrs)
                {
                    dbrfiles.Add(DbrFileToRecName(dbrfile, packfolder));
                }
            }
            try
            {
                using (ProgressBar progress = new ProgressBar())
                {
                    for (int cf = 0; cf < alldbrs.Length; cf++)
                    {
                        string dbrfile = alldbrs[cf];
                        string[] recstrings = File.ReadAllLines(dbrfile);
                        ARZRecord nrec = new ARZRecord(DbrFileToRecName(dbrfile, packfolder), recstrings, templates);
                        rectable.Add(nrec);

                        if (peek) // Peek and see if we have same data
                        {
                            ARZRecord peekrec = peekrectable.Find(pr => peekstrtable[pr.rfid] == strtable[nrec.rfid]);
                            CompareRecords(nrec, peekrec, strtable, peekstrtable);
                        } // If peek

                        // Show Progress
                        progress.Report((double)cf / alldbrs.Length);
                    } // for int cf = 0 ...
                } // using progress
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while parsing records. Message: {0}\nStack Trace:\n{1}", e.Message, e.StackTrace);
                return 1;
            }

            end = DateTime.Now;
            Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

            // Save Data
            if (!AskSaveData(opt.OutputFile, opt.ForceOverwrite)) return 1;

            return 0;
        } // ProcessPackVerb

        static int ProcessUnarcVerb(UnarcOptions opt)
        {
            string outpath = ".";
            if (!string.IsNullOrEmpty(opt.OutPath)) outpath = opt.OutPath;
            outpath = Path.GetFullPath(outpath);

            if (opt.ArcFiles.Count == 0)
            {
                Console.WriteLine("Please supply at least one arc file for extraction!");
                return 1;
            }
            
            foreach (string arcfilename in opt.ArcFiles)
            {
                // Console.WriteLine(arcfilename);
                string arcsub = "";
                if (opt.ArcFiles.Count > 1 || string.IsNullOrEmpty(opt.OutPath))
                    arcsub = Path.Combine(outpath, Path.GetFileNameWithoutExtension(arcfilename));
                else
                    arcsub = outpath;
                ARCFile archive = new ARCFile();
                using (FileStream arcstream = new FileStream(arcfilename, FileMode.Open))
                {
                    archive.ReadStream(arcstream);
                    archive.UnpackAll(arcsub);
                    // Debug repacking:
                    archive.RepackToFile(Path.Combine(Path.GetDirectoryName(arcfilename), Path.GetFileNameWithoutExtension(arcfilename) + "r" + Path.GetExtension(arcfilename)));
                }

            }
            return 0;
        }

        static int ProcessArcVerb(ArcOptions opt)
        {
            if (!string.IsNullOrEmpty(opt.Folder))
            {
                string afolder = Path.GetFullPath(opt.Folder);
                if (!Directory.Exists(afolder)) {
                    Console.WriteLine("Cannot find folder \"{0}\"", afolder);
                    return 1;
                }
                if (string.IsNullOrEmpty(opt.FileMask))
                    opt.FileMask = "*";

                string outfile = Path.GetFullPath(Path.GetFileName(afolder)+".arc");
                if (!string.IsNullOrEmpty(opt.OutFile))
                    outfile = Path.GetFullPath(opt.OutFile);
                Console.WriteLine("Packing \"{0}\" mask: \"{1}\", out: \"{2}\"", afolder, opt.FileMask, outfile);
                ArcFolder(afolder, opt.FileMask, outfile);
            } else return 1;
            return 0;
        }

        static string DbrFileToRecName(string dbrfile, string packfolder)
        {
            string recname = dbrfile.Substring(packfolder.Length).ToLower().Replace(Path.DirectorySeparatorChar, '/').TrimStart('/');
            // if (recname.StartsWith("/")) recname = recname.Substring(1);
            if (recname.StartsWith("database/")) recname = recname.Substring("database/".Length);
            /* DEBUG
            if (!recname.StartsWith("records/")) {
                // Console.WriteLine("Record {0} not under records/ path.", recname);
                // continue; // TODO: Fix Me, need proper subfolder path passed as parameter
            }
            */
            return recname;
        }

        static bool CompareRecords(ARZRecord nrec, ARZRecord peekrec, List<string> newstrtable, List<string> peekstrtable) {
            bool differs = false;
            // Console.WriteLine("Comparing {0}", newstrtable[nrec.rfid]);
            if (peekrec.rtype != nrec.rtype) { Console.WriteLine("Type differs \"{0}\" != \"{1}\"", nrec.rtype, peekrec.rtype); differs = true; }
            if (peekrec.entries.Count != nrec.entries.Count) { Console.WriteLine("Entry count differs peek {0} != new {1}", peekrec.entries.Count, nrec.entries.Count); differs = true; }
            else
            {
                for (int i = 0; i < nrec.entries.Count; i++)
                {
                    ARZEntry ne = nrec.entries[i];
                    ARZEntry pe = peekrec.entries[i];
                    if (peekstrtable[pe.dstrid] != newstrtable[ne.dstrid])
                    { Console.WriteLine("Entry {0} name differs peek \"{1}\" != new \"{2}\"", i, peekstrtable[pe.dstrid], newstrtable[ne.dstrid]); differs = true; }
                    if (pe.dtype != ne.dtype) { Console.WriteLine("Entry {0} type differs peek {1} != new {2}", i, pe.dtype, ne.dtype); differs = true; }
                    else
                    if (pe.dcount != ne.dcount) { Console.WriteLine("Entry {0} array size differs peek {1} != new {2}", i, pe.dcount, ne.dcount); differs = true; }
                    else
                    {
                        for (int k = 0; k < pe.dcount; k++)
                        {
                            if (pe.dtype == 0 || pe.dtype == 3)
                            {
                                if (pe.values[k] != ne.values[k]) {
                                    Console.WriteLine("Record {4} Entry {0} value {1} values differ peek \"{2}\" != new \"{3}\"", newstrtable[ne.dstrid], k, pe.values[k], ne.values[k], newstrtable[nrec.rfid]);
                                    differs = true;
                                }
                            }
                            else if (pe.dtype == 1)
                            {
                                if (pe.AsFloat(k) != ne.AsFloat(k)) {
                                    Console.WriteLine("Record {4} Entry {0} value {1} values differ peek \"{2}\" != new \"{3}\"", newstrtable[ne.dstrid], k, pe.AsFloat(k), pe.AsFloat(k), newstrtable[nrec.rfid]);
                                    differs = true;
                                }
                            }
                            else
                            {
                                if (peekstrtable[pe.values[k]] != newstrtable[ne.values[k]])
                                {
                                    Console.WriteLine("Record {4} Entry {0} value {1} strings differ peek \"{2}\" != new \"{3}\"", newstrtable[ne.dstrid], k, peekstrtable[pe.values[k]], newstrtable[ne.values[k]], newstrtable[nrec.rfid]);
                                    differs = true;
                                }
                            }
                        }
                    }
                }
            }

            return differs;
        }

        static bool AskSaveData(string outputfile, bool force = false)
        {
            if (force || !File.Exists(outputfile) || char.ToUpper(Ask(string.Format("Output file \"{0}\" exists, overwrite? [y/n] n: ", Path.GetFullPath(outputfile)), "yYnN", 'N')) == 'Y')
            {
                DateTime start = DateTime.Now;
                Console.Write("Saving database ... ");
                CompactStringlist();
                SaveData(outputfile);
                DateTime end = DateTime.Now;
                Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));
                return true;
            }
            else
            {
                Console.WriteLine("Aborted by user.");
                return false;
            }
        }

        static HashSet<string> BuildResourceSet(string respath)
        {
            string[] resfolders = new string[1] { respath };
            foreach (string resfolder in resfolders)
            {
                if (Directory.Exists(resfolder))
                {
                    if (resfiles == null)
                        resfiles = new HashSet<string>();
                    string[] allresfiles = Directory.GetFiles(resfolder, "*.*", SearchOption.AllDirectories);
                    foreach (string aresfile in allresfiles)
                    {
                        string relresfile = aresfile.Substring(resfolder.Length).Replace(Path.DirectorySeparatorChar, '/').TrimStart('/');
                        // if (relresfile.StartsWith("/")) relresfile = relresfile.Substring(1);
                        // Console.WriteLine("Adding reasource file \"{0}\"", relresfile); // DEBUG
                        resfiles.Add(relresfile);
                    }
                }
            }

            if (resfiles != null && resfiles.Count == 0) // No files to reference to - disable option
            {
                // Console.WriteLine("Check references option was passed, but there are no resource files to be referenced against. Disabling.");
                resfiles = null;
                // opt.CheckReferences = false;
            }

            return resfiles;
        } // BuildResourceSet()

        static Dictionary<string, TemplateNode> BuildTemplateDict(string[] tfolders)
        {
            Dictionary<string, TemplateNode> templates = new Dictionary<string, TemplateNode>();
            foreach (string tfolder in tfolders)
            {
                string tfullpath = Path.GetFullPath(tfolder).TrimEnd(Path.DirectorySeparatorChar);
                // string tfullpath = Path.GetFullPath(tfolder);
                string tbasepath = tfullpath;
                // If we are not starting at the base, go up.
                // TODO: what about mods not using /database for templates?
                string dirname = Path.GetFileName(tbasepath);
                if (Path.GetFileName(tbasepath) == "templates")
                    tbasepath = Path.GetFullPath(Path.Combine(tbasepath, ".."));
                if (Path.GetFileName(tbasepath) == "database")
                    tbasepath = Path.GetFullPath(Path.Combine(tbasepath, ".."));

                // string debugpath = Path.Combine(tbasepath, "templates");
                if (!Directory.Exists(Path.Combine(tbasepath, "database")) && !Directory.Exists(Path.Combine(tbasepath, "templates")))
                {
                    Console.WriteLine("Possibly wrong template base folder \"{0}\", does not contain database or templates folder!", tbasepath);
                }

                string[] alltemplates = Directory.GetFiles(tfullpath, "*.tpl", SearchOption.AllDirectories);
                foreach (string tfile in alltemplates)
                {
                    string[] tstrings = File.ReadAllLines(tfile);
                    string tpath = tfile.Substring(tbasepath.Length).ToLower().Replace(Path.DirectorySeparatorChar, '/');
                    if (tpath.StartsWith("/")) tpath = tpath.Substring(1);
                    // TODO: sort out database prefix                            

                    if (!tpath.StartsWith("database/"))
                    {
                        tpath = "database/" + tpath;
                    }

                    TemplateNode ntemplate = new TemplateNode(null, tpath);
                    ntemplate.ParseNode(tstrings, 0);
                    if (templates.ContainsKey(tpath))
                        Console.WriteLine("Template \"{0}\" already parsed, overriding with {1}", tpath, tfile); // DEBUG
                    templates[tpath] = ntemplate;
                }
            }

            foreach (KeyValuePair<string, TemplateNode> tpln in templates)
            {
                // Console.WriteLine("{0} - {1} name {2}", tpln.Key, tpln.Value.kind, tpln.Value.values["name"]);
                tpln.Value.FillIncludes(templates);
            }
            return templates;
        } // BuildTemplateDict

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

        static void ArcFolder(string afolder, string afilemask, string outfilename)
        {
            afolder = Path.GetFullPath(afolder);
            using (FileStream ofs = new FileStream(outfilename, FileMode.Create))
            using (ARCWriter awriter = new ARCWriter(ofs))
            {
                string[] afiles = Directory.GetFiles(afolder, afilemask, SearchOption.AllDirectories);
                foreach (string afile in afiles)
                {
                    DateTime afiletime = File.GetLastWriteTime(afile);
                    string aentry = Path.GetFullPath(afile).Substring(afolder.Length).Replace(Path.DirectorySeparatorChar, '/').ToLower().TrimStart('/');
                    using (FileStream ifs = new FileStream(afile, FileMode.Open))
                    {
                        ifs.Seek(0, SeekOrigin.Begin); // Go to start
                        awriter.WriteFromStream(aentry, afiletime, ifs); // Pack and write
                    }
                }
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("\nGrim Dawn Arz Editor, v{0}", VERSION);
            Console.WriteLine("\nUsage:");
            Console.WriteLine();
            Console.WriteLine("{0} <pack|set|get|extract> <suboptions>\n", Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location));
            Console.WriteLine("pack <mod base> <output file> [-t <template base1> [<template base2> ...]] [-yr]\n");
            Console.WriteLine("  <mod base>         mod base path where loose *.dbr and *.tpl files reside");
            Console.WriteLine("                     usually has /database/ and /resources/ folders");
            Console.WriteLine("                     Note: all *.dbr and *.tpl files in this directory");
            Console.Write("                     and under will be packed to db, keep it tidy.");
            var ht = new HelpText();
            ht.AddDashesToOption = true;
            ht.AddOptions(new PackOptions());
            Console.WriteLine(ht);
            Console.Write("set <input file> [-o <output file>] [-y] [-r <record> {-e <entry1> [<entry2> ...] | -f <record file>}] [-p <patchfile1> [<patchfile2> ...]] [-b <base folder> [-s <subfolder>]] ");
            ht = new HelpText();
            ht.AddDashesToOption = true;
            ht.AddOptions(new SetOptions());
            Console.WriteLine(ht);
            Console.WriteLine("get <input file> -r <record> [-e <entry1> [<entry2 ...>]]");
            Console.Write("\nNot implemented yet!;");
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

                        Adler32 adler = new Adler32();
                        byte[] bufrdata = mrdata.GetBuffer();
                        uint csrdata = adler.ComputeHash(bufrdata, 0, bufrdata.Length);
                        // Console.WriteLine("Data 0x{0:X4}", csrdata);
                        adler = new Adler32();
                        byte[] bufrtable = mrtable.GetBuffer();
                        uint csrtable = adler.ComputeHash(bufrtable, 0, bufrtable.Length);
                        // Console.WriteLine("Record Table 0x{0:X4}", csrtable);
                        adler = new Adler32();
                        byte[] bufstable = mstable.GetBuffer();
                        uint csstable = adler.ComputeHash(bufstable, 0, bufstable.Length);
                        // Console.WriteLine("String Table 0x{0:X4}", csstable);
                        // Checksum for whole file w/o footer
                        adler = new Adler32();
                        adler.ComputeHash(bufheader, 0, bufheader.Length); // Header
                        adler.ComputeHash(bufrdata, 0, bufrdata.Length); // Data
                        adler.ComputeHash(bufrtable, 0, bufrtable.Length); // Data Table
                        uint csall = adler.ComputeHash(bufstable, 0, bufstable.Length); // String Table
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

        class VerbOptions
        {
            [VerbOption("set", HelpText = "Set values in database")]
            public SetOptions SetVerb { get; set; }
            [VerbOption("get", HelpText = "Get records/values in database")]
            public GetOptions GetVerb { get; set; }
            [VerbOption("extract", HelpText = "Extract records from database")]
            public ExtractOptions ExtractVerb { get; set; }
            [VerbOption("pack", HelpText = "Pack records to database")]
            public PackOptions PackVerb { get; set; }
            [VerbOption("unarc", HelpText = "Unpack arc file(s)")]
            public UnarcOptions UnarcVerb { get; set; }
            [VerbOption("arc", HelpText = "pack arc file(s)")]
            public ArcOptions ArcVerb { get; set; }
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

            [OptionArray('e', "entries", HelpText= "Entry names with values. Entry example: \"playerDevotionCap,56,\", Multiple entries are separated by spaces, if entry contains spaces it must be enclosed in doublequotes (\").")]
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

        class PackOptions
        {
            [ValueOption(0)]
            public string InputPath { get; set; }
            [ValueOption(1)]
            public string OutputFile { get; set; }
            [OptionArray('t', "tbase", HelpText = "Folder(s) containing templates, if not specified - assumes templates are in mod folder. Order matters - later templates override prior. You would like game templates go first and your own templates second.")]
            public string[] TemplatePaths { get; set; }
            [Option('p', "peek", HelpText = "Peek at database and compare results - debugging option")]
            public string PeekFile { get; set; }
            [Option('y', "overwrite", HelpText = "Force overwrite target file, make backups!")]
            public bool ForceOverwrite { get; set; }
            [Option('r', "refs", HelpText = "Check if referenced files exist. Needs \"resources\" folder in <mod base>. May generate a lot of messages.")]
            public bool CheckReferences { get; set; }
            [HelpOption]
            public string GetUsage()
            {
                return HelpText.AutoBuild(this, (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
            }
        }

        class UnarcOptions {
            [ValueList(typeof(List<string>))]
            public List<string> ArcFiles { get; set; }
            [Option('o', "out-path", HelpText = "Path where to store unpacked files")]
            public string OutPath { get; set; }
        }

        class ArcOptions {
            [ValueOption(0)]
            public string Folder { get; set; }
            [ValueOption(1)]
            public string OutFile { get; set; }
            [Option('m', "mask", HelpText = "Mask for file inclusion, All files are added if not specified")]
            public string FileMask { get; set; }
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

        public ARZRecord(string rname, string[] rstrings, Dictionary<string, TemplateNode> templates)
        {
            rfid = Program.ModifyString(-1, rname);
            rtype = ""; // TODO: IMPORTANT: How record type is determined, last piece of info
            this.rdFileTime = DateTime.Now;
            entries = new List<ARZEntry>();
            TemplateNode tpl = null;
            foreach (string estr in rstrings)
            {
                TemplateNode vart = null;
                string[] eexpl = estr.Split(',');
                string varname = eexpl[0];
                string vvalue = eexpl[1];
                if (varname == "templateName") {
                    try
                    {
                        tpl = templates[vvalue];
                        vart = TemplateNode.TemplateNameVar;
                    } catch (KeyNotFoundException e)
                    {
                        Console.WriteLine("Template file \"{0}\" used by record {1} not found!", vvalue, rname);
                        throw e;
                    }
                } else {
                    // Find variable in templates
                    if (tpl != null) // Find variable
                        vart = tpl.FindVariable(varname);
                }
                if (varname.ToLower() == "class") rtype = vvalue;
                /*
                foreach (KeyValuePair<string, TemplateNode> tkv in templates)
                {
                    List<TemplateNode> nodeswithvars = tkv.Value.findValue(varname);
                    found |= nodeswithvars.Count > 0;
                    foreach (TemplateNode tn in nodeswithvars)
                    {
                        // Console.WriteLine("Found variable in {0} {1}", tkv.Key, tn.values.ToString());
                    }
                }
                */
                
                if (vart == null)
                {
                    Console.WriteLine("Record \"{1}\" Variable \"{0}\" not found in any included templates.", varname, rname);
                }
                else {
                    ARZEntry newentry = new ARZEntry(estr, vart, rname);
                    entries.Add(newentry);
                }
            }
            this.PackData();
        }

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
                        // e.dcount;
                        e.dcount = (ushort) e.values.Length;
                        // Console.WriteLine("Packing {0} - Len: {1} ", Program.strtable[e.dstrid], e.dcount); // DEBUG
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
        public bool changed = false; // TODO: overhead
        public bool isarray = false;
        static List<string> strtable = null;
        // static SortedList<string, int> strsearchlist = null;
        public static List<string> StrTable { get { if (strtable == null) strtable = Program.strtable; return strtable; } set { strtable = value; } }
        // public static SortedList<string, int> StrSearchList { get { if (strsearchlist == null) strsearchlist = Program.strsearchlist; return strsearchlist; } set { strsearchlist = value; } }

        public ARZEntry(BinaryReader edata)
        {
            ReadBytes(edata);
        }

        public ARZEntry(string estr, TemplateNode tpl, string recname)
        {
            string vtype = null;
            string entryname = estr.Split(',')[0];

            try
            {
                vtype = tpl.values["type"];
            }
            catch (KeyNotFoundException e) {
                Console.WriteLine("ERROR: Template {0} does not contain value type for entry {1}! I'm not guessing it.", tpl.GetTemplateFile(), entryname);
                throw e; // rethrow            
            }

            isarray = tpl.values.ContainsKey("class") && tpl.values["class"] == "array"; // This is an array act accordingly when packing strings

            if (vtype.StartsWith("file_")) {
                // Check for resources
                if (Program.resfiles != null || Program.dbrfiles != null)
                {
                    string[] rfiles = null;
                    if (!isarray) rfiles = new string[1] { estr.Split(',')[1] };
                    else { rfiles = estr.Split(',')[1].Split(';'); };

                    if (vtype == "file_dbr" && Program.dbrfiles != null)
                    {
                        foreach (string rfile in rfiles)
                        {
                            if (!Program.dbrfiles.Contains(rfile))
                            {
                                Console.WriteLine("Missing database file \"{0}\" referenced by \"{1}\" in record \"{2}\".", rfile, entryname, recname);
                            }
                        }
                    } else if (Program.resfiles != null)
                    {
                        foreach (string rfile in rfiles)
                        {
                            if (!Program.resfiles.Contains(rfile))
                            {
                                Console.WriteLine("Missing record file \"{0}\" referenced by \"{1}\" in record \"{2}\".", rfile, entryname, recname);
                            }
                        }
                    }
                }

                vtype = "string"; // Make it string
            }

            switch (vtype) {
                case "string":
/* Got tired of this, all files under one hood - string
                case "file_dbr":
                case "file_tex":
                case "file_msh":
                case "file_lua":
                case "file_qst":
                case "file_anm":
                case "file_ssh":
*/
                case "equation":
                    dtype = 2; // string type
                    break;
                case "real":
                    dtype = 1;
                    break;
                case "bool":
                    dtype = 3;
                    break;
                case "int":
                    dtype = 0;
                    break;
                default:
                    Console.WriteLine("ERROR: Template {0} has unknown type {1} for entry {1}", tpl.GetTemplateFile(), tpl.values["type"], entryname);
                    throw new Exception("Unknown variable type");
                    // break;
            }

            values = new int[0];
            dstrid = Program.ModifyString(-1, entryname);
            if (!TryAssign(estr))
                throw new Exception(string.Format("Error assigning entry {0}", entryname));
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
            // if (estrs.Length > 3 || estrs.Length == 1)
            if (estrs.Length != 3)
            {
                Console.WriteLine("Malformed assignment string \"{0}\"", fromstr);
                // Console.ReadKey(true);
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
                    if (!isarray || values.Length == 1)
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

            // DEBUG: array size changing but not creating new record
            //*
            if (values.Length != 0 && strs.Length != values.Length)            {
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
                this.dcount = (ushort)values.Length;
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

    // Template Objects
    public class TemplateNode {
        public static readonly TemplateNode TemplateNameVar;
        public string TemplateFile = null;
        public TemplateNode parent = null;
        public string kind = "";
        public Dictionary<string, string> values = new Dictionary<string, string>();
        public SortedDictionary<string, TemplateNode> varsearch = null;
        public List<TemplateNode> subitems = new List<TemplateNode>();
        public List<TemplateNode> includes = new List<TemplateNode>();

        static TemplateNode()
        {
            TemplateNameVar = new TemplateNode();
            TemplateNameVar.kind = "variable";
            TemplateNameVar.values["name"] = "templateName";
            TemplateNameVar.values["class"] = "variable";
            TemplateNameVar.values["type"] = "string";
        }

        public TemplateNode(TemplateNode aparent = null, string aTemplateFile = null) {
            parent = aparent;
            TemplateFile = aTemplateFile;
            if (aparent == null) // I am root
                varsearch = new SortedDictionary<string, TemplateNode>();
        }

        public string GetTemplateFile()
        {
            if (!string.IsNullOrEmpty(TemplateFile))
                return TemplateFile;
            else 
                if (parent != null) return parent.GetTemplateFile(); 
                else return null;
        }

        public int ParseNode(string[] parsestrings, int parsestart = 0) {
            int i = parsestart;
            while (string.IsNullOrWhiteSpace(parsestrings[i])) i++;
            kind = (parsestrings[i++].Trim().ToLower()); // Check for proper kind
            while (parsestrings[i].Trim() != "{") i++; // Find Opening bracket
            i++;
            while (string.IsNullOrWhiteSpace(parsestrings[i])) i++;
            while (parsestrings[i].Trim() != "}")
            {
                if (string.IsNullOrWhiteSpace(parsestrings[i])) { i++; continue; }
                if (parsestrings[i].Trim().Contains('='))
                { // This is entry value
                    string[] sval = parsestrings[i].Split('=');
                    string akey = (sval[0].Trim());
                    string aval = (sval[1].Trim().Trim('"'));
                    values[akey] = aval;
                } else
                { // subitem
                    TemplateNode sub = new TemplateNode(this);
                    i = sub.ParseNode(parsestrings, i);
                    subitems.Add(sub);
                }
                i++;
            }
            return i;
        }
        
        public List<TemplateNode> findValue(string aval) {
            List<TemplateNode> res = new List<TemplateNode>();
            if (values.ContainsValue(aval)) res.Add(this);
            foreach (TemplateNode sub in subitems)
            {
                res.AddRange(sub.findValue(aval));
            }
            return res;
        }

        public TemplateNode FindVariable(string aname) {
            if (parent == null && varsearch.ContainsKey(aname))
            {
                return varsearch[aname];
            }

            if (kind == "variable" && values.ContainsKey("name") && values["name"] == aname)
            {
                if (parent == null) varsearch.Add(aname, this);
                return this;
            }
            // Not this, recurse subitems:
            TemplateNode res = null;
            foreach (TemplateNode sub in subitems) {
                res = sub.FindVariable(aname);
                if (res != null)
                {
                    if (parent == null) varsearch.Add(aname, res);
                    return res;
                }
            }
            // No entry in subitems, check includes
            foreach (TemplateNode incl in includes)
            {
                res = incl.FindVariable(aname);
                if (res != null)
                {
                    if (parent == null) varsearch.Add(aname, res);
                    return res;
                }
            }
            // Giving up:
            return null;
        }

        public void FillIncludes(Dictionary<string, TemplateNode> alltempl) {
            foreach (TemplateNode sub in subitems)
            {
                if (sub.kind == "variable" && sub.values.ContainsKey("type") && sub.values["type"] == "include")
                {
                    string incstr = sub.values.ContainsKey("value") ? sub.values["value"] : "";
                    if (incstr == "")
                        incstr = sub.values.ContainsKey("defaultValue") ? sub.values["defaultValue"] : "";
                    incstr = incstr.ToLower().Replace("%template_dir%", "").Replace(Path.DirectorySeparatorChar, '/');
                    if (incstr.StartsWith("/")) incstr = incstr.Substring(1);
                    if (alltempl.ContainsKey(incstr))
                    {
                        // Console.WriteLine("Include {0}", incstr);
                        // Check for cycles
                        TemplateNode itemplate = alltempl[incstr];
                        if (itemplate == this || includes.Contains(itemplate))
                            Console.WriteLine("WARNING: When parsing template {0} include \"{1}\" found out it's already included by another file, include might be cyclic.", GetTemplateFile(), incstr);
                        includes.Add(itemplate);
                    } else
                    {
                        TemplateNode tproot = this;
                        while (tproot.parent != null) tproot = tproot.parent;
                        string intemplate = alltempl.First(t => t.Value == tproot).Key;
                        Console.WriteLine("Cannot find include {0} referenced in {1}", incstr, intemplate);
                    }
                }
                else if (sub.kind == "group") {
                    sub.FillIncludes(alltempl);
                }
            }
        }
    }

}
