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
        const string VERSION = "0.2b0";
        static byte[] mdata = null;
        static byte[] footer = new byte[16];
        //TODO: Move those static fields to an instance object, arz file should be instance of an object, not stored in static fields in main program
        public static List<string> strtable = null;
        public static ARZStrings astrtable = null;
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
                else if (iVerb == "build")
                {
                    return ProcessBuildVerb(iOpt as BuildOptions);
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
            Console.WriteLine("Setting records is not implemented yet");
            return 1;
            /*
            if (string.IsNullOrEmpty(opt.OutputFile)) opt.OutputFile = opt.InputFile;
            ARZWriter arzw = new ARZWriter();

            byte[] mdata = File.ReadAllBytes(opt.InputFile);
            using (MemoryStream memory = new MemoryStream(mdata)) { 
                ARZReader arzr = new ARZReader(memory);
                for (int i = 0; i < arzr.Count; i++)
                {
                    ARZRecord rec = arzr[i];
                    arzw.WriteFromRecord(arzr[i]);
                    rec.DiscardData(); // Discard record data as it's not needed now
                }
            }

            using (FileStream fs = new FileStream(opt.OutputFile, FileMode.Create))
                arzw.SaveToStream(fs);

            return 0;
            //*/
        }

        static int ProcessSetVerb_Old(SetOptions opt)
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
                // if (!AskSaveData(opt.OutputFile, opt.ForceOverwrite)) return 1;
                throw new NotImplementedException();
            }
            else
            {
                // SaveData(OutputFile); // DEBUG: REMOVE
                Console.WriteLine("No changes, files untouched");
            }
            return 0;
        } // ProcessSetVerb

        static int ProcessExtractVerb(ExtractOptions opt) {
            //using ()
            byte[] mdata = File.ReadAllBytes(opt.InputFile);
            DateTime start = DateTime.Now;
            using (MemoryStream memory = new MemoryStream(mdata))
            {
                ARZReader arzr = new ARZReader(memory);

                string outpath = null;
                if (string.IsNullOrEmpty(opt.OutputPath))
                    outpath = Directory.GetCurrentDirectory();
                else
                    outpath = Path.GetFullPath(opt.OutputPath);

                Console.WriteLine("Extracting to \"{0}\" ...", outpath);

                char ans = 'n';
                bool overwriteall = opt.ForceOverwrite;
                using (ProgressBar progress = new ProgressBar())
                {
                    // foreach (ARZRecord rec in rectable)
                    for (int i = 0; i < arzr.Count; i++)
                    {
                        ARZRecord rec = arzr[i]; 
                        string filename = Path.Combine(outpath, rec.Name.Replace('/', Path.DirectorySeparatorChar));
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
                                rec.SaveToFile(filename);
                                File.SetCreationTime(filename, rec.rdFileTime); // TODO: Check if this is needed
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error writing file \"{0}\", Message: ", filename, e.Message);
                                return 1;
                            }
                        }

                        rec.DiscardData();
                        progress.Report(((double)i) / arzr.Count);
                    }
                }
            }
            DateTime end = DateTime.Now;
            Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));
            return 0;
        }

        static int ProcessExtractVerb_Old(ExtractOptions opt)
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

        static int BuildAssets(string assetfolder, string sourcefolder, string buildfolder, string gamefolder)
        {
            string[] assetfiles = Directory.GetFiles(assetfolder, "*", SearchOption.AllDirectories);
            int ci = 0;
            using (ProgressBar progress = new ProgressBar())
            {
                foreach (string afile in assetfiles)
                {
                    AssetBuilder abuilder = new AssetBuilder(afile, assetfolder, gamefolder);
                    abuilder.CompileAsset(sourcefolder, buildfolder);
                    progress.Report((double)ci++ / assetfiles.Length);
                }
            }
            return 0;
        }

        static int ProcessBuildVerb(BuildOptions opt)
        {
            DateTime beginbuild = DateTime.Now;
            // Weird - parameters come in mangled, there's added " at the end if passed string ends with \";
            string packfolder = ""; string modname = ""; string buildfolder = ""; string gamefolder = "";
            try
            {
                packfolder = Path.GetFullPath(opt.ModPath);
                modname = Path.GetFileName(packfolder);
                buildfolder = string.IsNullOrEmpty(opt.BuildPath) ? packfolder : Path.GetFullPath(opt.BuildPath);
                gamefolder = string.IsNullOrEmpty(opt.GameFolder) ? Path.GetFullPath(".") : Path.GetFullPath(opt.GameFolder); // TODO: Make game folder detection more robust, check dir one level up
            } catch {
                Console.WriteLine("Error parsing parameters, check for escape characters, especially \\\" (folders should not end in \\).");
            }
            if (!File.Exists(Path.Combine(gamefolder, "Grim Dawn.exe"))) {
                Console.WriteLine("Need correct game folder with mod tools (use parameter -g)");
                return 1;
            }
            // Assets
            Console.Write("Compiling Assets ... ");
            BuildAssets(Path.Combine(packfolder, "assets"), Path.Combine(packfolder, "source"), Path.Combine(buildfolder, "resources"), gamefolder);
            Console.WriteLine("Done");

            // Now Build Templates:
            Console.Write("Parsing templates ... ");
            DateTime start = DateTime.Now;

            Dictionary<string, TemplateNode> templates = new Dictionary<string, TemplateNode>();

            ARCFile vanillatpl = new ARCFile();
            using (FileStream fs = new FileStream(Path.Combine(gamefolder, "database", "templates.arc"), FileMode.Open))
            {
                vanillatpl.ReadStream(fs);
                foreach (ARCTocEntry aentry in vanillatpl.toc)
                {
                    List<string> strlist = new List<string>();
                    using (MemoryStream tplmem = new MemoryStream())
                    {
                        vanillatpl.UnpackToStream(aentry, tplmem);
                        tplmem.Seek(0, SeekOrigin.Begin);
                        using (StreamReader sr = new StreamReader(tplmem))
                        {
                            while (!sr.EndOfStream)
                            {
                                strlist.Add(sr.ReadLine());
                            }
                        }
                    }
                    string entryname = "database/templates/" + aentry.GetEntryString(vanillatpl.strs);
                    if (strlist.Count > 0 && entryname.ToLower().EndsWith(".tpl"))
                    {
                        // if (entryname == "chainlaserbeam.tpl")
                           // entryname = entryname;

                        TemplateNode node = new TemplateNode(null, entryname);
                        node.ParseNode(strlist.ToArray());
                        templates.Add(entryname, node);
                        // Console.WriteLine("Template {0}, Has Lines {1}", entryname, strlist.Count);
                    }
                }
            }

            // Now all other templates:
            string[] tfolders = null;
            if (opt.TemplatePaths != null)
            {
                tfolders = opt.TemplatePaths;
            }
            else
            {
                tfolders = new string[1] { opt.ModPath };
            }

            try
            {
                templates = BuildTemplateDict(tfolders, templates);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error parsing templates, reason - {0}\nStackTrace:\n{1}", e.Message, e.StackTrace);
                return 1;
            }

            // Fill include list
            foreach (KeyValuePair<string, TemplateNode> tpln in templates)
            {
                // Console.WriteLine("{0} - {1} name {2}", tpln.Key, tpln.Value.kind, tpln.Value.values["name"]);
                tpln.Value.FillIncludes(templates);
                // DEBUG:
                // if (tpln.Value.includes.Count > 0)
                    // Console.WriteLine("Template {0} has {1} includes", tpln.Value.TemplateFile, tpln.Value.includes.Count);
            }

            DateTime end = DateTime.Now;
            Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

            // Got Templates
            Console.Write("Packing Database ... ");
            start = DateTime.Now;
            string[] alldbrs = null;
            try
            {
                alldbrs = Directory.GetFiles(packfolder, "*.dbr", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error listing *.dbr files, reason - {0} ", e.Message);
                return 1;
            }

            ARZWriter arzw = new ARZWriter(templates);
            try
            {
                using (ProgressBar progress = new ProgressBar())
                {
                    for (int cf = 0; cf < alldbrs.Length; cf++)
                    {
                        string dbrfile = alldbrs[cf];
                        // Console.WriteLine("Packing {0}", dbrfile); // DEBUG
                        string[] recstrings = File.ReadAllLines(dbrfile);
                        arzw.WriteFromLines(DbrFileToRecName(dbrfile, packfolder), recstrings);
                        // Show Progress
                        progress.Report((double)cf / alldbrs.Length);
                    } // for int cf = 0 ...
                } // using progress
            }
            catch (Exception e)
            {
                Console.ReadKey(true);
                Console.WriteLine("Error while parsing records. Message: {0}\nStack Trace:\n{1}", e.Message, e.StackTrace);
                return 1;
            }

            end = DateTime.Now;
            Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

            // Save Data
            string dbfolder = Path.Combine(buildfolder, "database");
            if (!Directory.Exists(dbfolder))
                Directory.CreateDirectory(dbfolder);
            string outputfile = Path.Combine(dbfolder, modname + ".arz");
            using (FileStream fs = new FileStream(outputfile, FileMode.Create))
                arzw.SaveToStream(fs);

            // Pack resources
            string resfolder = Path.GetFullPath(Path.Combine(buildfolder, "resources"));
            if (Directory.Exists(resfolder))
            {
                Console.WriteLine("Packing resources:");
                string[] resdirs = Directory.GetDirectories(resfolder, "*", SearchOption.TopDirectoryOnly);
                foreach (string resdir in resdirs)
                {
                    string outfile = Path.Combine(resfolder, Path.GetFileName(resdir) + ".arc");
                    Console.WriteLine("Packing folder \"{0}\", to: \"{1}\"", resdir, outfile);
                    ArcFolder(resdir, "*", outfile);
                }
            }
            Console.WriteLine("Build successful. Done ({0:c})", (TimeSpan)(DateTime.Now - beginbuild));
            // Console.ReadKey(true);
            return 0;
        }

        static int ProcessPackVerb(PackOptions opt)
        {
            string packfolder = Path.GetFullPath(opt.InputPath);

            // Console.ReadKey(true); // DEBUG

            // return 0;
            //
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
            /*
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
            //*/
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

            /*
            if (opt.CheckReferences)
            {
                dbrfiles = new HashSet<string>();
                foreach (string dbrfile in alldbrs)
                {
                    dbrfiles.Add(DbrFileToRecName(dbrfile, packfolder));
                }
            }*/

            ARZWriter arzw = new ARZWriter(templates);
            try
            {
                using (ProgressBar progress = new ProgressBar())
                {
                    for (int cf = 0; cf < alldbrs.Length; cf++)
                    {
                        string dbrfile = alldbrs[cf];
                        string[] recstrings = File.ReadAllLines(dbrfile);
                        arzw.WriteFromLines(DbrFileToRecName(dbrfile, packfolder), recstrings);
                        // ARZRecord nrec = new ARZRecord(DbrFileToRecName(dbrfile, packfolder), recstrings, templates);
                        // ARZRecord nrec = null;
                        // rectable.Add(nrec);

                        /*
                        if (peek) // Peek and see if we have same data
                        {
                            ARZRecord peekrec = peekrectable.Find(pr => peekstrtable[pr.rfid] == strtable[nrec.rfid]);
                            CompareRecords(nrec, peekrec, strtable, peekstrtable);
                        } // If peek
                        */
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
            if (!AskSaveData(arzw, opt.OutputFile, opt.ForceOverwrite)) return 1;


            // Pack Resources
            /*
            string resfolder = Path.GetFullPath(Path.Combine(packfolder, "resources"));
            if (Directory.Exists(resfolder)) {
                Console.WriteLine("Packing resources:");
                string[] resdirs = Directory.GetDirectories(resfolder, "*", SearchOption.TopDirectoryOnly);
                foreach (string resdir in resdirs)
                {
                    string outfile = Path.Combine(resfolder, Path.GetFileName(resdir) + ".arc");
                    Console.WriteLine("Packing \"{0}\", to: \"{1}\"", resdir, outfile);
                    ArcFolder(resdir, "*", outfile);
                }
            }
            */
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
                    // archive.RepackToFile(Path.Combine(Path.GetDirectoryName(arcfilename), Path.GetFileNameWithoutExtension(arcfilename) + "r" + Path.GetExtension(arcfilename)));
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
                            if (pe.dtype == ARZEntryType.Int || pe.dtype == ARZEntryType.Bool)
                            {
                                if (pe.values[k] != ne.values[k]) {
                                    Console.WriteLine("Record {4} Entry {0} value {1} values differ peek \"{2}\" != new \"{3}\"", newstrtable[ne.dstrid], k, pe.values[k], ne.values[k], newstrtable[nrec.rfid]);
                                    differs = true;
                                }
                            }
                            else if (pe.dtype == ARZEntryType.Real)
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

        static bool AskSaveData(ARZWriter arzw, string outputfile, bool force = false)
        {
            if (force || !File.Exists(outputfile) || char.ToUpper(Ask(string.Format("Output file \"{0}\" exists, overwrite? [y/n] n: ", Path.GetFullPath(outputfile)), "yYnN", 'N')) == 'Y')
            {
                DateTime start = DateTime.Now;
                Console.Write("Saving database ... ");
                // CompactStringlist();
                // SaveData(outputfile);
                using (FileStream fs = new FileStream(outputfile, FileMode.Create))
                    arzw.SaveToStream(fs);
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

        static Dictionary<string, TemplateNode> BuildTemplateDict(string[] tfolders, Dictionary<string, TemplateNode> templates = null)
        {
            if (templates == null) templates = new Dictionary<string, TemplateNode>();
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
                    ARZHeader header = new ARZHeader(memory);
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

                    memory.Seek(header.StringTableStart, SeekOrigin.Begin);
                    astrtable = new ARZStrings(memory, header.StringTableSize);

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
                    // string fname;
                    // if (Path.GetFileName(afile) == "dermapteranwall01_nml.tex")
                       // fname = afile;
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
            [VerbOption("build", HelpText = "Build mod")]
            public BuildOptions BuildVerb { get; set; }
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

        class BuildOptions {
            [ValueOption(0)]
            public string ModPath { get; set; }
            [ValueOption(1)]
            public string BuildPath { get; set; }
            [Option('g', "game-folder", HelpText = "Grim Dawn folder (tools folder)")]
            public string GameFolder { get; set; }
            [OptionArray('t', "tbase", HelpText = "Folder(s) containing templates, if not specified - assumes templates are in mod folder. Order matters - later templates override prior. You would like game templates go first and your own templates second.")]
            public string[] TemplatePaths { get; set; }

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

        static List<string> ReadStringTable(BinaryReader br, int size)
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

        static List<ARZRecord> ReadRecordTable(BinaryReader br, int rcount) {
            List<ARZRecord> rlist = new List<ARZRecord>();
            rlist.Capacity = (int)rcount;
            for (int i = 0; i < rcount; i++)
            {
                rlist.Add(new ARZRecord(br, astrtable));
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
                    if (e.dtype == ARZEntryType.String)
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
                    if (e.dtype == ARZEntryType.String)
                        for (int i = 0; i < e.values.Length; i++)
                            e.values[i] = map[e.values[i]]; 
                }
            }

            strtable = target; // Replace string table with new one
        }

    }

}
