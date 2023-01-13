using System.Text;
using ArzEdit.Service;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace ArzEdit.Console;

internal class Program
{
    private const string VERSION = "0.2b5";
    private static byte[] footer = new byte[16];
    private static readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(x =>
    {
        x.AddConsole();
    });
    public static ILogger Log = _loggerFactory.CreateLogger<Program>();
    public static List<string> strtable = null;
    public static ARZStrings astrtable = null;
    public static List<int> strrefcount = null;
    public static SortedDictionary<string, int> strsearchlist = null;
    public static HashSet<string> resfiles = null;
    public static HashSet<string> dbrfiles = null;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return 1;
        }

        var result = 0;

        Parser.Default
            .ParseArguments<SetOptions, GetOptions, ExtractOptions, PackOptions, BuildOptions, UnarcOptions, ArcOptions>(args)
            .WithParsed<SetOptions>(x => ProcessSetVerb(x))
            .WithParsed<GetOptions>(x =>
            {
                System.Console.WriteLine("Getting records is not implemented yet!");
                result = 1;
            })
            .WithParsed<ExtractOptions>(x => ProcessExtractVerb(x))
            .WithParsed<PackOptions>(x => ProcessPackVerb(x))
            .WithParsed<BuildOptions>(x => ProcessBuildVerb(x))
            .WithParsed<UnarcOptions>(x => ProcessUnarcVerb(x))
            .WithParsed<ArcOptions>(x => ProcessArcVerb(x))
            .WithNotParsed(_ =>
            {
                result = 1;
            });

        return result;
    }

    private static int ProcessSetVerb(SetOptions opt)
    {
        System.Console.WriteLine("Setting records is not implemented yet");
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

    private static int ProcessExtractVerb(ExtractOptions opt) {
        //using ()
        var mdata = File.ReadAllBytes(opt.InputFile);
        var start = DateTime.Now;
        using (var memory = new MemoryStream(mdata))
        {
            var arzr = new ARZReader(memory);

            string outpath = null;
            if (string.IsNullOrEmpty(opt.OutputPath))
                outpath = Directory.GetCurrentDirectory();
            else
                outpath = Path.GetFullPath(opt.OutputPath);

            System.Console.WriteLine("Extracting to \"{0}\" ...", outpath);

            var ans = 'n';
            var overwriteall = opt.ForceOverwrite;
            using (var progress = new ProgressBar())
            {
                // foreach (ARZRecord rec in rectable)
                for (var i = 0; i < arzr.Count; i++)
                {
                    var rec = arzr[i]; 
                    var filename = Path.Combine(outpath, rec.Name.Replace('/', Path.DirectorySeparatorChar));
                    // Console.WriteLine("Writing \"{0}\"", filename); // Debug
                    Directory.CreateDirectory(Path.GetDirectoryName(filename));
                    var fileexists = File.Exists(filename);
                    if (!overwriteall && fileexists)
                    {
                        progress.SetHidden(true);
                        ans = char.ToLower(Ask(string.Format("File \"{0}\" exists, overwrite? yes/no/all/cancel (n): ", filename), "yYnNaAcC", 'n'));
                        if (ans == 'c')
                        {
                            System.Console.WriteLine("Aborted by user");
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
                            // Console.WriteLine("Error writing file \"{0}\", Message: ", filename, e.Message);
                            Log.LogError("Could not write file \"{0}\", Message: ", filename, e.Message);
                            return 1;
                        }
                    }

                    rec.DiscardData();
                    progress.Report(((double)i) / arzr.Count);
                }
            }
        }
        var end = DateTime.Now;
        System.Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));
        return 0;
    }

    private static int BuildAssets(string assetfolder, string sourcefolder, string buildfolder, string gamefolder)
    {
        if (!Directory.Exists(assetfolder))
        {
            Log.LogWarning("No asset folder {0}. Skipping compilation.", assetfolder);
            return 1;
        }
        var assetfiles = Directory.GetFiles(assetfolder, "*", SearchOption.AllDirectories);
        var ci = 0;
        using var progress = new ProgressBar();
        foreach (var afile in assetfiles)
        {
            var abuilder = new AssetBuilder(afile, assetfolder, gamefolder);
            abuilder.CompileAsset(sourcefolder, buildfolder);
            progress.Report((double)ci++ / assetfiles.Length);
        }

        return 0;
    }

    private static int ProcessBuildVerb(BuildOptions opt)
    {
        //// Add File logger:
        //if (!string.IsNullOrEmpty(opt.LogFile)) {
        //    File.AppendAllText(opt.LogFile, string.Format("Build process started at {0}\n", DateTime.Now));
        //    var fileTarget = new NLog.Targets.FileTarget();
        //    LogManager.Configuration.AddTarget("file", fileTarget);
        //    fileTarget.FileName = Path.GetFullPath(opt.LogFile);
        //    fileTarget.Layout = "${level}: ${message}";
        //    var filerule = new NLog.Config.LoggingRule("*", LogLevel.Debug, fileTarget);
        //    LogManager.Configuration.LoggingRules.Add(filerule);
        //}

        //// Reconfigure console logger:
        //if (opt.EnableVerbose)
        //{
        //    LogManager.Configuration.LoggingRules.First().EnableLoggingForLevels(LogLevel.Debug, LogLevel.Fatal);
        //}
        //if (opt.EnableSilent)
        //{
        //    NLog.Config.LoggingRule rule = LogManager.Configuration.LoggingRules.First();
        //    rule.DisableLoggingForLevel(LogLevel.Trace);
        //    rule.DisableLoggingForLevel(LogLevel.Debug);
        //    rule.DisableLoggingForLevel(LogLevel.Info);
        //    rule.DisableLoggingForLevel(LogLevel.Warn);
        //}

        //if (!string.IsNullOrEmpty(opt.LogFile) || opt.EnableVerbose || opt.EnableSilent)
        //{
        //    LogManager.ReconfigExistingLoggers();
        //}
            
        // Build begins
        var beginbuild = DateTime.Now;
        // Weird - parameters come in mangled, there's added " at the end if passed string ends with \";
        var packfolder = ""; var modname = ""; var buildfolder = ""; var gamefolder = "";
        try
        {
            packfolder = Path.GetFullPath(opt.ModPath);
            modname = Path.GetFileName(packfolder);
            buildfolder = string.IsNullOrEmpty(opt.BuildPath) ? packfolder : Path.GetFullPath(opt.BuildPath);
            gamefolder = string.IsNullOrEmpty(opt.GameFolder) ? Path.GetFullPath(".") : Path.GetFullPath(opt.GameFolder); // TODO: Make game folder detection more robust, check dir one level up
        } catch {
            // Console.WriteLine("Error parsing parameters, check for escape characters, especially \\\" (folders should not end in \\).");
            Log.LogError("Error parsing parameters, check for escape characters, especially \\\" (folders should not end in \\).");
        }
        if (!File.Exists(Path.Combine(gamefolder, "Grim Dawn.exe"))) {
            System.Console.WriteLine("Need correct game folder with mod tools (use parameter -g)");
            return 1;
        }
        if (!opt.SkipAssets)
        {
            // Assets
            System.Console.Write("Compiling Assets ... ");
            if (BuildAssets(Path.Combine(packfolder, "assets"), Path.Combine(packfolder, "source"), Path.Combine(buildfolder, "resources"), gamefolder) != 0)
                Log.LogWarning("Error building assets.");
            System.Console.WriteLine("Done");
        }
        //*
        if (!opt.SkipDB)
        {
            // Now Build Templates:
            System.Console.Write("Parsing templates ... ");
            var start = DateTime.Now;

            var templates = new Dictionary<string, TemplateNode>();
                
            // Read Vanilla templates directly from arc file:
            var vanillatpl = new ARCFile();
            using (var fs = new FileStream(Path.Combine(gamefolder, "database", "templates.arc"), FileMode.Open))
            {
                vanillatpl.ReadStream(fs);
                foreach (var aentry in vanillatpl.toc)
                {
                    var strlist = new List<string>();
                    using (var tplmem = new MemoryStream())
                    {
                        vanillatpl.UnpackToStream(aentry, tplmem);
                        tplmem.Seek(0, SeekOrigin.Begin);
                        using (var sr = new StreamReader(tplmem))
                        {
                            while (!sr.EndOfStream)
                            {
                                strlist.Add(sr.ReadLine());
                            }
                        }
                    }
                    var entryname = "database/templates/" + aentry.GetEntryString(vanillatpl.strs);
                    if (strlist.Count > 0 && entryname.ToLower().EndsWith(".tpl"))
                    {
                        // if (entryname == "chainlaserbeam.tpl")
                        // entryname = entryname;

                        var node = new TemplateNode(null, entryname);
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
                tfolders = opt.TemplatePaths; // Add only specified templates
            }
            else
            {
                tfolders = new string[1] { packfolder }; // Add templates from mod folder
            }

            try
            {
                templates = BuildTemplateDict(tfolders, templates); // Build template dictionary from folders
            }
            catch (Exception e)
            {
                // Console.WriteLine("Error parsing templates, reason - {0}\nStackTrace:\n{1}", e.Message, e.StackTrace);
                Log.LogError("Error parsing templates, reason - {0}\nStackTrace:\n{1}", e.Message, e.StackTrace);
                return 1;
            }

            // Fill template include list
            foreach (var tpln in templates)
            {
                // Console.WriteLine("{0} - {1} name {2}", tpln.Key, tpln.Value.kind, tpln.Value.values["name"]);
                tpln.Value.FillIncludes(templates);
                // DEBUG:
                // if (tpln.Value.includes.Count > 0)
                // Console.WriteLine("Template {0} has {1} includes", tpln.Value.TemplateFile, tpln.Value.includes.Count);
            }

            var end = DateTime.Now;
            System.Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

            // Got Templates
            System.Console.Write("Packing Database ... ");
            start = DateTime.Now;
            string[] alldbrs = null;
            try
            {
                alldbrs = Directory.GetFiles(packfolder, "*.dbr", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                // Console.WriteLine("Error listing *.dbr files, reason - {0} ", e.Message);
                Log.LogError("Error listing *.dbr files, reason - {0} ", e.Message);
                return 1;
            }

            // Now main job - parsing dbr's:
            var arzw = new ARZWriter(templates);
            try
            {
                using var progress = new ProgressBar();
                for (var cf = 0; cf < alldbrs.Length; cf++)
                {
                    var dbrfile = alldbrs[cf];
                    // Console.WriteLine("Packing {0}", dbrfile); // DEBUG
                    var recstrings = File.ReadAllLines(dbrfile, Encoding.ASCII);
                    arzw.WriteFromLines(DbrFileToRecName(dbrfile, packfolder), recstrings);
                    // Show Progress
                    progress.Report((double)cf / alldbrs.Length);
                } // for int cf = 0 ...
            }
            catch (Exception e)
            {
                //Console.WriteLine("Error while parsing records. Message: {0}\nStack Trace:\n{1}", e.Message, e.StackTrace);
                Log.LogError("Error while parsing records. Message: {0}\nStack Trace:\n{1}", e.Message, e.StackTrace);
                // Console.ReadKey(true);
                return 1;
            }

            end = DateTime.Now;
            System.Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

            // Save Database
            var dbfolder = Path.Combine(buildfolder, "database");
            if (!Directory.Exists(dbfolder))
                Directory.CreateDirectory(dbfolder);
            var outputfile = Path.Combine(dbfolder, modname + ".arz");
            using (var fs = new FileStream(outputfile, FileMode.Create))
                arzw.SaveToStream(fs);
        }

        if (!opt.SkipResources)
        {
            // Pack resources
            System.Console.WriteLine("Packing resources ... ");
            var resfolder = Path.GetFullPath(Path.Combine(buildfolder, "resources"));
            if (Directory.Exists(resfolder))
            {
                var resdirs = Directory.GetDirectories(resfolder, "*", SearchOption.TopDirectoryOnly);
                foreach (var resdir in resdirs)
                {
                    var outfile = Path.Combine(resfolder, Path.GetFileName(resdir) + ".arc");
                    Log.LogInformation("Packing folder \"{0}\", to: \"{1}\"", resdir, outfile);
                    ArcFolder(resdir, "*", outfile);
                }
            }
            else {
                Log.LogWarning("Resource folder {0} not found, skipping.", resfolder);
            }
            System.Console.WriteLine("Done");
        }
        //*/
        System.Console.WriteLine("Build successful. Done ({0:c})", (TimeSpan)(DateTime.Now - beginbuild));
           
        // Console.ReadKey(true);
            
        return 0;
    }

    private static int ProcessPackVerb(PackOptions opt)
    {
        var packfolder = Path.GetFullPath(opt.InputPath);
        System.Console.WriteLine("Packing is broken right now.");
        return 1;
        // Console.ReadKey(true); // DEBUG

        // return 0;
            
        if (string.IsNullOrEmpty(opt.OutputFile))
        {
            System.Console.WriteLine("Please specify output file as second parameter!");
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

        System.Console.Write("Parsing templates ... ");
        var start = DateTime.Now;

        Dictionary<string, TemplateNode> templates = null;
        try
        {
            templates = BuildTemplateDict(tfolders);
        } catch (Exception e)
        {
            System.Console.WriteLine("Error parsing templates, reason - {0}\nStackTrace:\n{1}", e.Message, e.StackTrace);
            return 1;
        }

        var end = DateTime.Now;
        System.Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

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
        System.Console.Write("Packing ... ");
        start = DateTime.Now;
        string[] alldbrs = null;
        try
        {
            alldbrs = Directory.GetFiles(packfolder, "*.dbr", SearchOption.AllDirectories);
        }
        catch (Exception e) {
            System.Console.WriteLine("Error listing *.dbr files, reason - {0} ", e.Message);
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

        var arzw = new ARZWriter(templates);
        try
        {
            using var progress = new ProgressBar();
            for (var cf = 0; cf < alldbrs.Length; cf++)
            {
                var dbrfile = alldbrs[cf];
                // if (dbrfile.ToLower().EndsWith("caravan_backgroundimage.dbr"))
                // Console.WriteLine("Buggy Here");
                var recstrings = File.ReadAllLines(dbrfile, Encoding.ASCII);
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
        }
        catch (Exception e)
        {
            System.Console.WriteLine("Error while parsing records. Message: {0}\nStack Trace:\n{1}", e.Message, e.StackTrace);
            return 1;
        }

        end = DateTime.Now;
        System.Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));

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

    private static int ProcessUnarcVerb(UnarcOptions opt)
    {
        var outpath = ".";
        if (!string.IsNullOrEmpty(opt.OutPath)) outpath = opt.OutPath;
        outpath = Path.GetFullPath(outpath);

        if (opt.ArcFiles.Length == 0)
        {
            System.Console.WriteLine("Please supply at least one arc file for extraction!");
            return 1;
        }
            
        foreach (var arcfilename in opt.ArcFiles)
        {
            // Console.WriteLine(arcfilename);
            var arcsub = "";
            if (opt.ArcFiles.Length > 1 || string.IsNullOrEmpty(opt.OutPath))
                arcsub = Path.Combine(outpath, Path.GetFileNameWithoutExtension(arcfilename));
            else
                arcsub = outpath;
            var archive = new ARCFile();
            using var arcstream = new FileStream(arcfilename, FileMode.Open);
            archive.ReadStream(arcstream);
            archive.UnpackAll(arcsub);
        }
        return 0;
    }

    private static int ProcessArcVerb(ArcOptions opt)
    {
        if (!string.IsNullOrEmpty(opt.Folder))
        {
            var afolder = Path.GetFullPath(opt.Folder);
            if (!Directory.Exists(afolder)) {
                System.Console.WriteLine("Cannot find folder \"{0}\"", afolder);
                return 1;
            }
            if (string.IsNullOrEmpty(opt.FileMask))
                opt.FileMask = "*";

            var outfile = Path.GetFullPath(Path.GetFileName(afolder)+".arc");
            if (!string.IsNullOrEmpty(opt.OutFile))
                outfile = Path.GetFullPath(opt.OutFile);
            // Console.WriteLine("Packing \"{0}\" mask: \"{1}\", out: \"{2}\"", afolder, opt.FileMask, outfile);
            Log.LogInformation("Packing \"{0}\" mask: \"{1}\", out: \"{2}\"", afolder, opt.FileMask, outfile);
            ArcFolder(afolder, opt.FileMask, outfile);
        } else return 1;
        return 0;
    }

    private static string DbrFileToRecName(string dbrfile, string packfolder)
    {
        var recname = dbrfile.Substring(packfolder.Length).ToLower().Replace(Path.DirectorySeparatorChar, '/').TrimStart('/');
        if (recname.StartsWith("database/")) recname = recname.Substring("database/".Length);
        return recname;
    }

    private static bool AskSaveData(ARZWriter arzw, string outputfile, bool force = false)
    {
        if (force || !File.Exists(outputfile) || char.ToUpper(Ask(string.Format("Output file \"{0}\" exists, overwrite? [y/n] n: ", Path.GetFullPath(outputfile)), "yYnN", 'N')) == 'Y')
        {
            var start = DateTime.Now;
            System.Console.Write("Saving database ... ");
            // CompactStringlist();
            // SaveData(outputfile);
            using (var fs = new FileStream(outputfile, FileMode.Create))
                arzw.SaveToStream(fs);
            var end = DateTime.Now;
            System.Console.WriteLine("Done ({0:c})", (TimeSpan)(end - start));
            return true;
        }
        else
        {
            System.Console.WriteLine("Aborted by user.");
            return false;
        }
    }

    /* // For later use, crosschecking if dbr's link to existing resources
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
    */

    // Iterates through folders and reads all template files adding them to list:
    private static Dictionary<string, TemplateNode> BuildTemplateDict(string[] tfolders, Dictionary<string, TemplateNode> templates = null)
    {
        if (templates == null) templates = new Dictionary<string, TemplateNode>();
        foreach (var tfolder in tfolders)
        {
            var tfullpath = Path.GetFullPath(tfolder).TrimEnd(Path.DirectorySeparatorChar);
            // string tfullpath = Path.GetFullPath(tfolder);
            var tbasepath = tfullpath;
            // If we are not starting at the base, go up.
            // TODO: what about mods not using /database for templates?
            var dirname = Path.GetFileName(tbasepath);
            if (Path.GetFileName(tbasepath) == "templates")
                tbasepath = Path.GetFullPath(Path.Combine(tbasepath, ".."));
            if (Path.GetFileName(tbasepath) == "database")
                tbasepath = Path.GetFullPath(Path.Combine(tbasepath, ".."));

            // string debugpath = Path.Combine(tbasepath, "templates");
            if (!Directory.Exists(Path.Combine(tbasepath, "database")) && !Directory.Exists(Path.Combine(tbasepath, "templates")))
            {
                // Console.WriteLine("Possibly wrong template base folder \"{0}\", does not contain database or templates folder!", tbasepath);
                Log.LogInformation("Possibly wrong template base folder \"{0}\", does not contain database or templates folder!", tbasepath);
            }

            var alltemplates = Directory.GetFiles(tfullpath, "*.tpl", SearchOption.AllDirectories);
            foreach (var tfile in alltemplates)
            {
                var tstrings = File.ReadAllLines(tfile);
                var tpath = tfile.Substring(tbasepath.Length).ToLower().Replace(Path.DirectorySeparatorChar, '/');
                if (tpath.StartsWith("/")) tpath = tpath.Substring(1);
                // TODO: sort out database prefix                            

                if (!tpath.StartsWith("database/"))
                {
                    tpath = "database/" + tpath;
                }

                var ntemplate = new TemplateNode(null, tpath);
                ntemplate.ParseNode(tstrings, 0);
                if (templates.ContainsKey(tpath))
                    Log.LogDebug("Template \"{0}\" already parsed, overriding with {1}", tpath, tfile); // TODO: Change to debug
                //Console.WriteLine("Template \"{0}\" already parsed, overriding with {1}", tpath, tfile); // DEBUG
                templates[tpath] = ntemplate;
            }
        }
        return templates;
    } // BuildTemplateDict

    // Utility function
    private static char Ask(string question, string answers, char adefault) {
        System.Console.Write(question);
        var key = System.Console.ReadKey();
        while (!answers.Contains(key.KeyChar) && key.Key != ConsoleKey.Enter)
            key = System.Console.ReadKey();
        System.Console.WriteLine();
        if (key.Key == ConsoleKey.Enter) return adefault;
        return key.KeyChar;
    }

    // Packs all files in a folder to arc file: 
    private static void ArcFolder(string afolder, string afilemask, string outfilename)
    {
        afolder = Path.GetFullPath(afolder);
        using var ofs = new FileStream(outfilename, FileMode.Create);
        using var awriter = new ARCWriter(ofs);
        var afiles = Directory.GetFiles(afolder, afilemask, SearchOption.AllDirectories);
        foreach (var afile in afiles)
        {
            var afiletime = File.GetLastWriteTime(afile);
            var aentry = Path.GetFullPath(afile).Substring(afolder.Length).Replace(Path.DirectorySeparatorChar, '/').ToLower().TrimStart('/');
            // string fname;
            // if (Path.GetFileName(afile) == "dermapteranwall01_nml.tex")
            // fname = afile;
            using var ifs = new FileStream(afile, FileMode.Open);
            ifs.Seek(0, SeekOrigin.Begin); // Go to start
            awriter.WriteFromStream(aentry, afiletime, ifs); // Pack and write
        }
    }

    //static void PrintUsage()
    //{
    //    Console.WriteLine("\nGrim Dawn Arz Editor, v{0}", VERSION);
    //    Console.WriteLine("\nUsage:");
    //    Console.WriteLine();
    //    Console.WriteLine("{0} <build|extract|arc|unarc> <suboptions>\n", Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location));
    //    Console.WriteLine("build <mod base> [<build path>] [-g <Grim Dawn folder>] [-t <additional template folders>] [-ADRvs] [-l <log file>]");
    //    Console.WriteLine("  <mod base>         folder that contains mod sources");
    //    Console.Write("  <build path>       folder where to put built mod files");
    //    var ht = new HelpText();
    //    ht.AddDashesToOption = true;
    //    ht.AddOptions(new BuildOptions());
    //    Console.WriteLine(ht);
    //    /*
    //    Console.WriteLine("pack <mod base> <output file> [-t <template base1> [<template base2> ...]] [-yr]\n");
    //    Console.WriteLine("  <mod base>         mod base path where loose *.dbr and *.tpl files reside");
    //    Console.Write("                     usually has /database/ and /resources/ folders");
    //    ht = new HelpText();
    //    ht.AddDashesToOption = true;
    //    ht.AddOptions(new PackOptions());
    //    Console.WriteLine(ht);
    //    */
    //    /*
    //    Console.Write("set <input file> [-o <output file>] [-y] [-r <record> {-e <entry1> [<entry2> ...] | -f <record file>}] [-p <patchfile1> [<patchfile2> ...]] [-b <base folder> [-s <subfolder>]] ");
    //    ht = new HelpText();
    //    ht.AddDashesToOption = true;
    //    ht.AddOptions(new SetOptions());
    //    Console.WriteLine(ht);
    //    Console.WriteLine("get <input file> -r <record> [-e <entry1> [<entry2 ...>]]");
    //    Console.Write("\nNot implemented yet!;");
    //    ht = new HelpText();
    //    ht.AddDashesToOption = true;
    //    ht.AddOptions(new GetOptions());
    //    Console.WriteLine(ht);
    //    */
    //    Console.WriteLine("extract <input file> [<output path>] [-y]");
    //    Console.WriteLine("<input file>         arz file to be extracted");
    //    Console.Write("<output path>        where to store dbr files");
    //    ht = new HelpText();
    //    ht.AddDashesToOption = true;
    //    ht.AddOptions(new ExtractOptions());
    //    Console.WriteLine(ht);
    //    Console.Write("arc <arc folder> <arc file> [-m <file mask>]");
    //    ht = new HelpText();
    //    ht.AddDashesToOption = true;
    //    ht.AddOptions(new ArcOptions());
    //    Console.WriteLine(ht);
    //    Console.Write("unarc <arc file1> [<arc file2> ...] [-o <output path>]");
    //    ht = new HelpText();
    //    ht.AddDashesToOption = true;
    //    ht.AddOptions(new UnarcOptions());
    //    Console.WriteLine(ht);
    //    // Console.ReadKey(true);
    //}
}