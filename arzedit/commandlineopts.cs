using CommandLine;
using CommandLine.Text;
using System;
using System.Collections.Generic;

namespace arzedit
{
    // Classes used by CommandLine parameter parsing library

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

    class SetOptions
    {
        [ValueOption(0)]
        public string InputFile { get; set; }

        [Option('o', "output", HelpText = "Write changes to specified file. NOTE: If not provided - overwrites input file, make backups!")]
        public string OutputFile { get; set; }

        [Option('y', "overwrite", HelpText = "Force overwrite output file, be careful, make backups!")]
        public bool ForceOverwrite { get; set; }

        [Option('b', "base", HelpText = "Pack records based at this folder (usually one which has /records/ folder)")]
        public string SetBase { get; set; }

        [Option('s', "subfolder", HelpText = "Pack only records in subfolder and below, relative to base being packed, like \"records\\game\\\"")]
        public string SetSubfolder { get; set; }

        [Option('r', "record", HelpText = "Record to be changed, format example: \"records/game/gameengine.dbr\" if it contains spaces - enclose in double quotes (\")")]
        public string SetRecord { get; set; }

        [OptionArray('p', "patch", HelpText = "Use patch file to update multiple records and entries")]
        public string[] SetPatches { get; set; }

        [Option('f', "file", HelpText = "Record file (*.dbr) to be assigned to the record.")]
        public string SetFile { get; set; }

        [OptionArray('e', "entries", HelpText = "Entry names with values. Entry example: \"playerDevotionCap,56,\", Multiple entries are separated by spaces, if entry contains spaces it must be enclosed in doublequotes (\").")]
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
    class GetOptions
    {
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

    class BuildOptions
    {
        [ValueOption(0)]
        public string ModPath { get; set; }
        [ValueOption(1)]
        public string BuildPath { get; set; }
        [Option('g', "game-folder", HelpText = "Grim Dawn folder (tools folder)")]
        public string GameFolder { get; set; }
        [OptionArray('t', "tbase", HelpText = "Folder(s) containing additional templates.")]
        public string[] TemplatePaths { get; set; }
        [Option('D', "skip-db", HelpText = "Skip building database")]
        public Boolean SkipDB { get; set; }
        [Option('A', "skip-assets", HelpText = "Skip compiling assets")]
        public Boolean SkipAssets { get; set; }
        [Option('R', "skip-res", HelpText = "Skip compressing Resources folder")]
        public Boolean SkipResources { get; set; }
        [Option('v', "verbose", HelpText = "Output Debug level messages")]
        public Boolean EnableVerbose { get; set; }
        [Option('s', "silent", HelpText = "Output only Error messages")]
        public Boolean EnableSilent { get; set; }
        [Option('l', "log-file", HelpText = "Log all messages (of any level) to file")]
        public string LogFile { get; set; }
    }

    class UnarcOptions
    {
        [ValueList(typeof(List<string>))]
        public List<string> ArcFiles { get; set; }
        [Option('o', "out-path", HelpText = "Path where to store unpacked files")]
        public string OutPath { get; set; }
    }

    class ArcOptions
    {
        [ValueOption(0)]
        public string Folder { get; set; }
        [ValueOption(1)]
        public string OutFile { get; set; }
        [Option('m', "mask", HelpText = "Mask for file inclusion, All files are added if not specified")]
        public string FileMask { get; set; }
    }
}
