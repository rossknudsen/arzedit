using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using LZ4;
using System.Text;
using Microsoft.Extensions.Logging;

namespace arzedit {
    // Classes responsible for Reading/Writing arz database, and database objects themselves

    public class ARZWriter
    {
        public ARZStrings strtable = null;
        int rcount = 0;
        MemoryStream mrecdata;
        MemoryStream mrectable;
        ARZHeader hdr;
        public Dictionary<string, TemplateNode> ftemplates = null;
        public ARZWriter(Dictionary<string, TemplateNode> atemplates = null)
        {
            ftemplates = atemplates;
            hdr = new ARZHeader();
            strtable = new ARZStrings();
            mrectable = new MemoryStream();
            mrecdata = new MemoryStream();
        }

        public void BeginWrite()
        {
            // hdr.WriteToStream(ost);
        }

        public void WriteFromRecord(ARZRecord rec)
        {
            ARZRecord clone = new ARZRecord(rec, strtable);
            WriteRecord(clone);
        }

        public void WriteRecord(ARZRecord rec)
        {
            rec.rdOffset = (int)mrecdata.Position; 
            mrecdata.Write(rec.cData, 0, rec.rdSizeCompressed); // Write Record Data
            rec.WriteToStream(mrectable); // Write to record table
            rcount += 1;
        }

        public void WriteFromLines(string recordname, string[] lines)
        {
            // if (recordname.Contains(@"petskill_demo_attack.dbr"))
               // Console.WriteLine("BUG:");
            ARZRecord nrec = new ARZRecord(recordname, lines, ftemplates, strtable);
            WriteRecord(nrec);
        }

        public void SaveToStream(Stream ost)
        {
            // Prepare header
            hdr.RecordTableStart = ARZHeader.HEADER_SIZE + (int)mrecdata.Length;
            hdr.RecordTableSize = (int)mrectable.Length;
            hdr.RecordTableEntries = rcount;
            MemoryStream mstrtable = new MemoryStream();
            strtable.WriteToStream(mstrtable);
            hdr.StringTableStart = hdr.RecordTableStart + hdr.RecordTableSize;
            hdr.StringTableSize = (int)mstrtable.Length;
            // Write header
            ost.Seek(0, SeekOrigin.Begin);
            MemoryStream mhdr = new MemoryStream(ARZHeader.HEADER_SIZE);
            hdr.WriteToStream(mhdr);

            // Checksums:
            Adler32 hashall = new Adler32();
            byte[] buf = mhdr.GetBuffer();
            hashall.ComputeHash(buf, 0, (int)mhdr.Length);
            buf = mrecdata.GetBuffer();
            hashall.ComputeHash(buf, 0, (int)mrecdata.Length);
            uint hrdata = (new Adler32()).ComputeHash(buf, 0, (int)mrecdata.Length);
            buf = mrectable.GetBuffer();
            hashall.ComputeHash(buf, 0, (int)mrectable.Length);
            uint hrtable = (new Adler32()).ComputeHash(buf, 0, (int)mrectable.Length);
            buf = mstrtable.GetBuffer();
            hashall.ComputeHash(buf, 0, (int)mstrtable.Length);
            uint hstable = (new Adler32()).ComputeHash(buf, 0, (int)mstrtable.Length);
            uint hall = hashall.checksum;
            
            // Write data
            mhdr.WriteTo(ost);
            mrecdata.WriteTo(ost);
            mrectable.WriteTo(ost);
            mstrtable.WriteTo(ost);

            // Write Footer:
            using (BinaryWriter bw = new BinaryWriter(ost, Encoding.ASCII, true))
            {
                bw.Write(hashall.checksum);
                bw.Write(hstable);
                bw.Write(hrdata);
                bw.Write(hrtable);
            }
        }        
    }

    public class ARZReader {        
        private Stream fstream = null;
        public ARZHeader hdr = null;
        public ARZStrings strtable = null;
        private List<ARZRecord> rectable = null;
        
        public ARZReader(Stream astream)
        {
            fstream = astream;
            ReadStream(fstream);
        }

        public ARZRecord this[int i] { get { return GetRecord(i); } }

        public int Count { get { return rectable != null ? rectable.Count : 0; } }

        public void ReadStream(Stream astream)
        {
            // Header
            astream.Seek(0, SeekOrigin.Begin);
            hdr = new ARZHeader(astream);
            
            // String table
            astream.Seek(hdr.StringTableStart, SeekOrigin.Begin);
            strtable = new ARZStrings(astream, hdr.StringTableSize);

            // Record table
            astream.Seek(hdr.RecordTableStart, SeekOrigin.Begin);
            rectable = ReadRecordTable(astream, hdr.RecordTableEntries);
        }

        public List<ARZRecord> ReadRecordTable(Stream astream, int rcount)
        {
            List<ARZRecord> rlist = new List<ARZRecord>();
            rlist.Capacity = (int)rcount;
            using (BinaryReader br = new BinaryReader(astream, Encoding.ASCII, true))
            {
                for (int i = 0; i < rcount; i++)
                {
                    rlist.Add(new ARZRecord(br, strtable));
                }
            }
            return rlist;
        }

        public ARZRecord GetRecord(int id) {
            ARZRecord rec = rectable[id];
            if (rec.entries == null)
            {
                fstream.Seek(ARZHeader.HEADER_SIZE + rec.rdOffset, SeekOrigin.Begin);
                using (BinaryReader br = new BinaryReader(fstream, Encoding.ASCII, true))
                    rec.ReadData(br);
            }
            return rec;
        }

    }

    public class ARZStrings {
        private List<string> strtable = null;
        private SortedDictionary<string, int> strsearchlist = null;

        public ARZStrings()
        {
            strtable = new List<string>();
        }

        public ARZStrings(Stream astream, int size) : this()
        {
            ReadStream(astream, size);
        }

        public string this[int i] { get { return strtable[i]; } set { strtable[i] = value; } }

        public int Count { get { return strtable.Count; } }

        public void ReadStream(Stream astream, int size)
        {
            if (strtable == null)
                strtable = new List<string>();
            strtable.Capacity = 0;
            int pos = 0;
            using (BinaryReader br = new BinaryReader(astream, Encoding.ASCII, true))
            {
                while (pos < size)
                {
                    int count = br.ReadInt32(); pos += 4; // Read Count
                    // Logger.Log.Trace("Block at pos: {0}; Count: {1}", pos, count);
                    strtable.Capacity += count;
                    for (int i = 0; i < count; i++)
                    {
                        int length = br.ReadInt32(); pos += 4;
                        strtable.Add(new string(br.ReadChars(length))); pos += length;
                    }
                }
            }
        }


        public void WriteToStream(Stream astream)
        {
            using (BinaryWriter bw = new BinaryWriter(astream, Encoding.ASCII, true))
            {
                bw.Write((int)strtable.Count);
                foreach (string s in strtable)
                {
                    bw.Write((int)s.Length);
                    bw.Write(s.ToCharArray());
                }
            }
        }        

        public int AddString(string newvalue)
        {
            if (strsearchlist == null) // Build string search list on first access
                BuildStringSearchList();

            if (strsearchlist.ContainsKey(newvalue))
                return strsearchlist[newvalue];
            else // New value
            {
               strtable.Add(newvalue);
               strsearchlist.Add(newvalue, strtable.Count - 1); // Add to searchlist
               return strtable.Count - 1;
            }
        }

        public int GetStringID(string searchstr)
        {
            if (strsearchlist == null)
                BuildStringSearchList();

            if (strsearchlist.ContainsKey(searchstr))
                return strsearchlist[searchstr];
            else
                return -1;
        }

        public void BuildStringSearchList()
        {
            strsearchlist = new SortedDictionary<string, int>();
            for (int i = 0; i < strtable.Count; i++)
            {
                strsearchlist.Add(strtable[i], i);
            }
        }


    }

    public class ARZRecord
    {
        public int rfid;
        public string rtype;
        public int rdOffset;
        public int rdSizeCompressed;
        public int rdSizeDecompressed;
        public DateTime rdFileTime;
        public byte[] cData;
        public byte[] aData;
        public List<ARZEntry> entries = null;
        public ARZStrings strtable = null;
        private HashSet<string> entryset = null;
        public string Name { get { return strtable?[rfid]; } }

        private static ARZEntryComparer NameComparer = new ARZEntryComparer();
   
        
        public ARZRecord(string rname, string[] rstrings, Dictionary<string, TemplateNode> templates, ARZStrings astrtable)
        {
            strtable = astrtable;
            rfid = strtable.AddString(rname);
            rtype = ""; // TODO: IMPORTANT: How record type is determined, last piece of info
            rdFileTime = DateTime.Now; // TODO: Correct file time should be passed here somehow
            entries = new List<ARZEntry>();
            entryset = new HashSet<string>();
            TemplateNode tpl = null;
            foreach (string line in rstrings) {
                if (line.StartsWith("templateName"))
                {
                    string[] eexpl = line.Split(',');
                    try
                    {
                        tpl = templates[eexpl[1]];
                        ARZEntry newentry = new ARZEntry(eexpl[0], TemplateNode.TemplateNameVar, rname, this);
                        if (newentry.TryAssign(eexpl[0], eexpl[1], strtable, ""))
                        {
                            entries.Add(newentry);
                            entryset.Add(eexpl[0]);
                        }
                    }
                    catch (KeyNotFoundException e)
                    {
                        // Console.WriteLine("Template file \"{0}\" used by record {1} not found!", eexpl[1], rname);
                        Logger.Log.LogError("Template file \"{0}\" used by record {1} not found!", eexpl[1], rname);
                        throw e;
                    }
                    break;
                }
            }
            if (tpl == null)
            {
                Logger.Log.LogError("Record {0} has no template!", rname); // DEBUG
                throw new Exception(string.Format("Record {0} has no template!", rname));
            }
            foreach (string estr in rstrings)
            {
                TemplateNode vart = null;
                string[] eexpl = estr.Split(',');
                if (eexpl.Length != 3)
                {
                    // Console.WriteLine("Record \"{0}\" - Malformed assignment string \"{1}\"", Name, estr);
                    if (eexpl.Length == 2)
                        Logger.Log.LogWarning("Record \"{0}\" - Malformed assignment string \"{1}\", No comma at the end, Recoverable.", Name, estr); // DEBUG:
                    else
                    {
                        Logger.Log.LogWarning("Record \"{0}\" - Malformed assignment string \"{1}\", Skipping", Name, estr);
                        continue;
                    }
                }
                string varname = eexpl[0];
                string vvalue = eexpl[1];
                string defaultsto = "";
                if (string.IsNullOrEmpty(varname))
                {
                    Logger.Log.LogWarning("Record \"{0}\" - Has empty entry name, Skipping", Name);
                    continue;
                }
                if (varname == "templateName")
                {
                    continue; // templateName should have been assigned already
                }
                else
                {
                    // Find variable in templates
                    // TODO: All this is rubbish, introduces ambiguity, find root cause, not hack & tack
                    /*
                    if (!notfoundvars.Contains(varname)) // Find variable template (if not already searched for and not found) 
                    {
                        vart = tpl?.FindVariable(varname);
                        if (vart == null) { // Includes has given up, search whole template database
                            Logger.Log.Debug("Entry {0}/{1} template not found in includes, looking up globally.", rname, varname);
                            foreach (TemplateNode otpl in templates.Values) {                                
                                if ((vart = otpl.FindVariable(varname)) != null)
                                    break;
                            }
                        }
                    }*/
                    vart = tpl.FindVariable(varname); 
                    if (vart != null)
                    {
                        if (vart.values.ContainsKey("defaultValue"))
                            defaultsto = vart.values["defaultValue"];
                        if (entryset.Contains(varname))
                        {
                            // Console.WriteLine("Record {0} duplicate entry {1} - Overwriting.", rname, varname); // TODO: Do not ignore, Overwrite
                            Logger.Log.LogInformation("Record {0} duplicate entry {1} - Overwriting.", rname, varname);
                            ARZEntry entry = entries.Find(e => e.Name == varname);
                            entry.TryAssign(varname, vvalue, strtable, defaultsto);
                            continue;
                        }
                        else
                        {

                            if (varname.ToLower() == "class") rtype = vvalue;
                            // DEBUG:
                            // if (rname.EndsWith("caravan_backgroundimage.dbr") && varname == "FileDescription" && eexpl[1].StartsWith("BitmapSingle"))
                            // Console.WriteLine("Error here...");
                            ARZEntry newentry = new ARZEntry(varname, vart, rname, this);
                            if (newentry.TryAssign(varname, vvalue, strtable, defaultsto))
                            {
                                entries.Add(newentry);
                                entryset.Add(varname);
                            }
                        }
                    }
                    else {
                        // Console.WriteLine("Entry {0} in {1} has no template.", varname, rname);
                        Logger.Log.LogDebug("Entry {0}/{1} template not found. Skipping.", rname, varname);
                        // notfoundvars.Add(varname);
                    }
                }
                    
            }
            entries.Sort(1, entries.Count - 1, NameComparer); // Sort all except first "templateName" entry
            PackData();
        }

        public ARZRecord(ARZRecord tocopy, ARZStrings astrtable) {
            strtable = astrtable;
            rfid = strtable.AddString(tocopy.Name);
            rtype = tocopy.rtype;
            rdFileTime = tocopy.rdFileTime;
            entries = new List<ARZEntry>();
            foreach (ARZEntry tce in tocopy.entries)
            {
                entries.Add(new ARZEntry(tce, this));
            }
            PackData();
        }

        public ARZRecord(BinaryReader rdata, ARZStrings astrtable)
        {
            strtable = astrtable;
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

        public List<ARZEntry> ReadData(BinaryReader brdata) // Reads record entry data from reader and creates entries list
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
                        entries.Add(new ARZEntry(eDbr, this));
                }
            }
            aData = null; // Free up memory
            return entries;
        }

        public void DiscardData()
        {
            entries = null;
            cData = null;
        }

        public void PackData()
        {
            int datasize = entries.Count * 8; // Headers
            foreach (ARZEntry e in entries)
                datasize += e.values.Length * 4; // + Data
            using (MemoryStream mStream = new MemoryStream(datasize))
            {
                using (BinaryWriter bWriter = new BinaryWriter(mStream))
                {
                    mStream.Seek(0, SeekOrigin.Begin);
                    foreach (ARZEntry e in entries)
                    {
                        // e.dcount;
                        e.dcount = (ushort)e.values.Length;
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

        public void SaveToFile(string filename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create))
                SaveToStream(fs);
        }

        public void SaveToStream(Stream astream)
        {
            using (StreamWriter sr = new StreamWriter(astream))
            {
                sr.NewLine = "\n";
                foreach (ARZEntry etr in entries)
                {
                    string estring = etr.ToString();
                    if (estring.Contains('\n') || estring.Contains(Environment.NewLine))
                    {
                        // Console.WriteLine("Record \"{0}\" entry \"{1}\" contains newline(s), fixing.", strtable[rfid], strtable[etr.dstrid]);
                        Logger.Log.LogInformation("Record \"{0}\" entry \"{1}\" contains newline(s), fixing.", strtable[rfid], strtable[etr.dstrid]);
                        estring = System.Text.RegularExpressions.Regex.Replace(estring, @"\r\n?|\n", "");
                    }
                    sr.WriteLine(estring);
                }
            }
        }

        public void WriteToStream(Stream rtstream)
        {
            using (BinaryWriter bw = new BinaryWriter(rtstream, Encoding.ASCII, true))
            {
                WriteRecord(bw, rdOffset);
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

    public enum ARZEntryType : ushort { Int=0, Real=1, String=2, Bool=3 };

    public class ARZEntryComparer : IComparer<ARZEntry>
    {
        public int Compare(ARZEntry first, ARZEntry second) 
        {
            /*
            if (first.Name == "templateName" || second.Name == "templateName") // Pop templateName to the top, ugly hack
                if (first.Name == second.Name)
                    return 0;
                else
                    return first.Name == "templateName" ? -1 : 1;
            else
            */
              return String.CompareOrdinal(first.Name, second.Name);
        }
    }


    public class ARZEntry
    {
        public ARZEntryType dtype;
        public ushort dcount;
        public int dstrid;
        public int[] values;
        public bool changed = false; // TODO: overhead
        public bool isarray = false;
        public bool isfile = false;
        private ARZRecord parent = null;
        // private ARZStrings strtable = null;
        // static SortedList<string, int> strsearchlist = null;
        public ARZStrings StrTable { get { return parent?.strtable; } }
        public string Name { get { return StrTable[dstrid]; } }
        // public static SortedList<string, int> StrSearchList { get { if (strsearchlist == null) strsearchlist = Program.strsearchlist; return strsearchlist; } set { strsearchlist = value; } }

        public ARZEntry(ARZEntry tocopy, ARZRecord aparent)
        {
            parent = aparent;
            dstrid = StrTable.AddString(tocopy.Name);
            dtype = tocopy.dtype;
            dcount = tocopy.dcount;
            values = (int[])tocopy.values.Clone();
            if (dtype == ARZEntryType.String)
                for (int i = 0; i < values.Length; i++) {
                    values[i] = StrTable.AddString(tocopy.AsString(i));
                }
        }

        public ARZEntry(BinaryReader edata, ARZRecord aparent)
        {
            parent = aparent;            
            ReadBytes(edata);
        }

        public ARZEntry(string entryname, TemplateNode tpl, string recname, ARZRecord aparent)
        {
            string vtype = null;
            parent = aparent;

            try
            {
                vtype = tpl.values["type"];
            }
            catch (KeyNotFoundException e)
            {
                // Console.WriteLine("ERROR: Template {0} does not contain value type for entry {1}! I'm not guessing it.", tpl.GetTemplateFile(), entryname);
                Logger.Log.LogError("Template {0} does not contain value type for entry {1}! I'm not guessing it.", tpl.GetTemplateFile(), entryname);
                throw e; // rethrow
            }

            isarray = tpl.values.ContainsKey("class") && tpl.values["class"] == "array"; // This is an array act accordingly when packing strings
                        
            if (vtype.StartsWith("file_"))
            {
                vtype = "file"; // Make it string
                isfile = true;
            }

            switch (vtype)
            {
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
                case "file":
                case "equation":
                    dtype = ARZEntryType.String; // string type
                    break;
                case "real":
                    dtype = ARZEntryType.Real;
                    break;
                case "bool":
                    dtype = ARZEntryType.Bool;
                    break;
                case "int":
                    dtype = ARZEntryType.Int;
                    break;
                default:
                    // Console.WriteLine("ERROR: Template {0} has unknown type {1} for entry {1}", tpl.GetTemplateFile(), tpl.values["type"], entryname);
                    Logger.Log.LogError("Template {0} has unknown type {1} for entry {1}", tpl.GetTemplateFile(), tpl.values["type"], entryname);
                    throw new Exception("Unknown variable type");
                    // break;
            }

            values = new int[0];
            dstrid = StrTable.AddString(entryname);
            /*
            if (!NewAssign(estr, StrTable, defaultsto))
                throw new Exception(string.Format("Error assigning entry {0}", entryname));
                */

        }

        public void ReadBytes(BinaryReader edata)
        {
            // header
            dtype = (ARZEntryType)edata.ReadUInt16();
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
            edata.Write((ushort)dtype);
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

        public string AsString(int eid)
        {
            return AsStringEx(eid, StrTable);
        }

        public string AsStringEx(int eid, ARZStrings strtable)
        {
            return strtable[values[eid]];
        }

        public bool TryAssign(string entryname, string valuestr, ARZStrings strtable, string defaultsto = "") {
            // This is artifact of old assign function:
            /* string[] estrs = fromstr.Split(',');
             if (estrs.Length > 3 || estrs.Length == 1)
             if (estrs.Length != 3)
             {
                Console.WriteLine("Record \"{0}\" - Malformed assignment string \"{1}\"", parent.Name, fromstr);
                // Console.ReadKey(true);
                return false;
            }
            string entryname = estrs[0];
            */
            // int entryid = StrSearchList[entryname];
            if (strtable[dstrid] != entryname)
            {
                // Console.WriteLine("Cannot assign \"{0}\" to \"{1}\" field (entry names differ).", entryname, strtable[dstrid]);
                Logger.Log.LogWarning("Cannot assign \"{0}\" to \"{1}\" field (entry names differ).", entryname, strtable[dstrid]);
                return false;
            }
            if (string.IsNullOrWhiteSpace(valuestr))
            {
                valuestr = defaultsto;
                // Console.WriteLine("{0} Defaults To {1}", entryname, estrs[1]);                
                // TODO: Ignore right now, may be implement defaulting later
                return false;
            }

            if (isfile) valuestr = valuestr.Replace(Path.DirectorySeparatorChar, '/').ToLower();

            /* Not sure how to behave in this situation, trim all trailing ???, 
             * most compatible seems to be packing ; together to one variable and let parsing fail/default.
            if (dtype != ARZEntryType.String && isarray) // Ignore last array element entry if has trailing ;
                valuestr = valuestr.TrimEnd(';');
            */
            string[] strs = valuestr.Split(';');
            if (!isarray )
            {
                if (strs.Length > 1)
                {
                    if (dtype == ARZEntryType.String)
                    {
                        strs = new string[1] { valuestr };
                    }
                    else
                    {
                        strs = new string[1] { strs[0] };
                    }
                }
            }
            else if (valuestr.Contains(";;"))
            {
                // This is when it get's weird:
                // try compacting multiple empty strings to ;;                
                List<string> cstrs = new List<string>();
                string accum = "";
                for (int i = 0; i < strs.Length; i++) // 
                {
                    if (strs[i] == "")
                    {
                        if (i + 1 < strs.Length && strs[i + 1] == "") accum += ";"; // Ignore last ; as it is a separator
                    }
                    else
                    {
                        if (accum != "")
                        {
                            // if (dtype != ARZEntryType.Real)
                            cstrs.Add(accum);
                            accum = "";
                        }
                        //if (dtype == ARZEntryType.Real && !string.IsNullOrWhiteSpace(strs[i]))
                        cstrs.Add(strs[i]);
                    }
                }
                // if (accum != "" && dtype != ARZEntryType.Real)
                if (accum != "")
                        cstrs.Add(accum);
                strs = cstrs.ToArray<string>();
            }

            float fval = (float)0.0;
            int[] nvalues = new int[strs.Length];

            for (int i = 0; i < strs.Length; i++)
            {               
                switch (dtype)
                {
                    case ARZEntryType.Int: // TODO: Move entry types to static Consts for clarity
                        if (!int.TryParse(strs[i], out nvalues[i]))
                        {
                            if (float.TryParse(strs[i], out fval))
                            {
                                nvalues[i] = (int)fval;
                                // Console.WriteLine("Int value represented as float {0} in {1}, truncating", fval, parent.Name); // DEBUG
                                Logger.Log.LogDebug("Int value represented as float \"{0:F}\" in {1}/{2}, truncating", fval, parent.Name, entryname);
                            } else
                            if (strs[i].StartsWith("0x"))
                            {
                                try
                                {
                                    nvalues[i] = Convert.ToInt32(strs[i].Substring(2), 16);
                                }
                                catch
                                {
                                    // Console.WriteLine("Could not parse Hex number {0}", strs[i]); // DEBUG
                                    Logger.Log.LogDebug("Could not parse Hex number \"{0}\" in {1}/{2}", strs[i], parent.Name, entryname);
                                    nvalues[i] = 0;
                                }
                            }
                            else
                            {
                                // DEBUG:
                                // Console.WriteLine("Record {3} Entry {0} Error parsing integer value #{1}=\"{2}\", Defaulting to 0", Name, i, strs[i], parent.Name);
                                // return false;
                                Logger.Log.LogDebug("Error parsing integer value \"{0}\" in {1}/{2}", strs[i], parent.Name, entryname);
                                nvalues[i] = 0; // Set default
                            }
                        }
                        break;
                    case ARZEntryType.Real:
                        if (!float.TryParse(strs[i], out fval))
                        {
                            // Console.WriteLine("Error parsing float value #{0}=\"{1}\", Defaulting to 0.0", i, strs[i]); // DEBUG
                            // return false;
                            Logger.Log.LogDebug("Error parsing float value \"{0}\" in {1}/{2}, Defaulting to 0.0", strs[i], parent.Name, entryname);
                            nvalues[i] = BitConverter.ToInt32(BitConverter.GetBytes(0.0), 0);
                        }
                        nvalues[i] = BitConverter.ToInt32(BitConverter.GetBytes(fval), 0);
                        break;
                    case ARZEntryType.String: // String
                        { 
                            nvalues[i] = strtable.AddString(strs[i]);
                        }
                        break;
                    case ARZEntryType.Bool:
                        if (!int.TryParse(strs[i], out nvalues[i]) || nvalues[i] > 1)
                        {
                            // Console.WriteLine("Error parsing boolean value #{0}=\"{1}\", Defaulting to False", i, strs[i]); // DEBUG
                            Logger.Log.LogDebug("Error parsing boolean value \"{0}\" in {1}/{2}, Defaulting to False", strs[i], parent.Name, entryname);
                            nvalues[i] = 0;
                            // return false;
                        }
                        break;
                    default:
                        // Console.WriteLine("Unknown data type in database"); // TODO: make more informative
                        Logger.Log.LogWarning("Unknown data type {2} for entry {0}/{1}", parent.Name, entryname, dtype);
                        return false;
                }
            }
            values = nvalues;
            dcount = (ushort)values.Length;
            changed = true;
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
                    case ARZEntryType.Int:
                    case ARZEntryType.Bool:
                    default:
                        sb.Append(value); // values are signed!
                        break;
                    case ARZEntryType.Real:
                        sb.AppendFormat("{0:0.000000}", BitConverter.ToSingle(BitConverter.GetBytes(value), 0));
                        break;
                    case ARZEntryType.String:
                        sb.Append(StrTable[value]);
                        break;
                }
                firstentry = false;
            }
            sb.Append(',');
            return sb.ToString();
        }
    }

    public class ARZHeader
    {
        public const int HEADER_SIZE = 24;
        public short Unknown = 0x02; // Magick
        public short Version = 0x03;
        public int RecordTableStart;
        public int RecordTableSize;
        public int RecordTableEntries;
        public int StringTableStart;
        public int StringTableSize;
        public ARZHeader()
        {
        }

        public ARZHeader(Stream astream)
        {
            using (BinaryReader br = new BinaryReader(astream, Encoding.ASCII, true))
                ReadBytes(br);
        }

        public void WriteToStream(Stream astream) {
            using (BinaryWriter bw = new BinaryWriter(astream, Encoding.ASCII, true))
            {
                bw.Write(Unknown);
                bw.Write(Version);
                bw.Write(RecordTableStart);
                bw.Write(RecordTableSize);
                bw.Write(RecordTableEntries);
                bw.Write(StringTableStart);
                bw.Write(StringTableSize);
            }
        }

        public void ReadBytes(BinaryReader bytes)
        {
            Unknown = bytes.ReadInt16();
            Version = bytes.ReadInt16();
            RecordTableStart = bytes.ReadInt32();
            RecordTableSize = bytes.ReadInt32();
            RecordTableEntries = bytes.ReadInt32();
            StringTableStart = bytes.ReadInt32();
            StringTableSize = bytes.ReadInt32();
        }

    }

}
