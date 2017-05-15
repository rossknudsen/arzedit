using System;
using System.IO;
using System.Collections.Generic;
using LZ4;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace arzedit
{
    class Program
    {
        static string ArzFile = "database.arz";
        static byte[] mdata = null;
        static byte[] footer = new byte[16];
        public static List<string> strtable = null;
        // public static SortedList<string, int> strsearchlist = null;
        static List<ARZRecord> rectable = null;
        static SortedList<string, int> recsearchlist = null;
        static void Main(string[] args)
        {
            // Read file
            mdata = File.ReadAllBytes(ArzFile);
            // TODO: Move this to separate function
            using (MemoryStream memory = new MemoryStream(mdata)) {
                using (BinaryReader reader = new BinaryReader(memory))
                {
                    memory.Seek(0, SeekOrigin.Begin);
                    ARZHeader header = new ARZHeader(reader);
                    // header.ReadBytes(reader);
                    Console.WriteLine("Unknown: {0}; Version: {1}; RecordTableStart: {2}; RecordTableSize: {3}; RecordTableEntries: {4}; StringTableStart: {5}; StringTableSize: {6};",
                        header.Unknown, header.Version, header.RecordTableStart, header.RecordTableSize, header.RecordTableEntries, header.StringTableStart, header.StringTableSize);
                    
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
                    // int zeros = 0, threes = 0, ones = 0, twos = 0, others = 0, tnonzeros = 0, zzeros = 0;
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
                        /*
                        foreach (ARZEntry etr in rec.entries) {
                            switch (etr.dtype)
                            {
                                case 0: // Integer
                                    zeros++;
                                    if (etr.values[0] < 2) { zzeros++;
                                        if (zzeros < 5)
                                        {
                                            Console.Write("ZERO - ");
                                            WriteEntry(etr);
                                        }
                                    }
                                    
                                    break;
                                case 1: // Float
                                    ones++;
                                    break;
                                case 2: // String
                                    twos++;
                                    break;
                                case 3: // Boolean, not sure how to determine type properly from text file
                                    threes++;
                                    if (etr.values[0] > 1)
                                    {
                                        tnonzeros++;
                                        if (tnonzeros < 5)
                                        {
                                            Console.Write("THREE - ");
                                            WriteEntry(etr);
                                        }
                                    }
                                    break;
                                default:
                                    others++;
                                    break;
                            }
                        }
                        */
                    }
                    //Console.WriteLine("Zeros: {0} (Zeros: {6}); Ones: {1}; Twos: {2}; Threes: {3}(non zeros {5}); Others: {4}", zeros, ones, twos, threes, others, tnonzeros, zzeros);
                    // Dump first record for debugging
                    // Find record by name
                    // int strindex = ;                    
                    /*
                    Console.WriteLine("File: {0}", strtable[first.rfid]);
                    foreach (ARZEntry etr in first.entries) {
                        WriteEntry(etr);
                    }
                    */
                    // TODO: Check how record type name is determined, also what entry value types are used throughout database
                }
            }
            // Find a record:
            // ARZRecord first = rectable[recsearchlist[strsearchlist["records/watertype/noisetextures/smoothwaves.dbr"]]];
            List<string> userecords = new List<string>(new string[] { "records/game/gameengine.dbr" });
            recsearchlist = FindRecords(userecords);
            ARZRecord first = rectable[recsearchlist["records/game/gameengine.dbr"]];
            // int itemstrid = strsearchlist["playerDevotionCap"];
            ARZEntry item = first.entries.Find(e => strtable[e.dstrid] == "playerDevotionCap");
            Console.WriteLine("Found:");
            Console.WriteLine(item);
            // WriteEntry(item);
            Console.WriteLine("File: {0}", strtable[first.rfid]);
            foreach (ARZEntry etr in first.entries)
            {
                Console.WriteLine(etr);
            }
            // Modify a record:
            item.TryAssign("playerDevotionCap,55,");
            if (item.changed)
                first.PackData();
            
            SaveData("database2.arz");

            Console.WriteLine("Done ...");
            Console.ReadKey(true);
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
            using (MemoryStream mstable = new MemoryStream(strtablesize)) {
                using (BinaryWriter brdata = new BinaryWriter(mrdata))
                using (BinaryWriter brtable = new BinaryWriter(mrtable))
                using (BinaryWriter bstable = new BinaryWriter(mstable)) {
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
                        bf.Write((ushort)0x02);
                        bf.Write((ushort)0x03);
                        // Write header values
                        bf.Write(rtablestart);
                        bf.Write(rtablesize);
                        bf.Write(rtableentr);
                        bf.Write(stablestart);
                        bf.Write(stablesize);
                        // Write data
                        bf.Write(mrdata.GetBuffer()); // Record data
                        bf.Write(mrtable.GetBuffer()); // Record table
                        bf.Write(mstable.GetBuffer()); // String table
                        // TODO: Write footer
                        bf.Write(footer);
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
            }
        }        

        static List<string> ReadStringTable(BinaryReader br, uint size)
        {
            List<string> slist = new List<string>();
            slist.Capacity = 0;
            int pos = 0;
            while (pos < size) {
                int count = br.ReadInt32(); pos += 4; // Read Count
                Console.WriteLine("Block at pos: {0}; Count: {1}", pos, count);
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
            string[] strs = fromstr.Split(',');
            if (strs.Length < 2)
                return false;
            string entryname = strs[0];
            // int entryid = StrSearchList[entryname];
            if (StrTable[dstrid] != entryname)
            {
                Console.WriteLine("Cannot assign \"{0}\" to \"{1}\" field (entry names differ).", entryname, StrTable[dstrid]);
                return false;
            }
            int[] nvalues = new int[values.Length];
            float fval = (float)0.0;
            if (strs.Length - 2 != values.Length)
            {
                Console.WriteLine("Array size mismatch: assigning {0} values to {1} array size", strs.Length - 2, values.Length);
                return false;
            }
            for (int i = 1; i < strs.Length - 1; i++) {
                switch (dtype)
                {
                    case 0:
                        if (!int.TryParse(strs[i], out nvalues[i - 1]))
                        {
                            Console.WriteLine("Error parsing integer value #{0}=\"{1}\"", i - 1, strs[i]);
                            return false;
                        }
                        break;
                    case 1:
                        if (!float.TryParse(strs[i], out fval))
                        {
                            Console.WriteLine("Error parsing float value #{0}=\"{1}\"", i - 1, strs[i]);
                            return false;
                        }
                        nvalues[i - 1] = BitConverter.ToInt32(BitConverter.GetBytes(fval), 0);
                        break;
                    case 2:                        
                        // String, changing string values not implemented keep if same
                        if (strs[i] != StrTable[values[i - 1]])
                        {
                            Console.WriteLine("Error: Changing string \"{0}\" to \"{1}\" - Modifying strings is not implemented", StrTable[values[i - 1]], strs[i]);
                            return false;
                        }
                        // nvalues[i - 1] = StrSearchList[strs[i]]; // Raises exception if not found in list, TODO: try out and catch
                        nvalues[i - 1] = values[i - 1];
                        break;
                    case 3:
                        if (!int.TryParse(strs[i], out nvalues[i - 1]) || nvalues[i - 1] > 1)
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
            if (!values.SequenceEqual(nvalues))
            {
                values = nvalues;
                changed = true;
            }
            else {
                Console.WriteLine("Entry is the same as original, nothing to do.");
            }
            return true;
        }

        public override string ToString()
        {
            if (StrTable == null) return "";
            StringBuilder sb = new StringBuilder();
            sb.Append(StrTable[dstrid]).Append(',');
            foreach (int value in values)
            {
                switch (dtype)
                {
                    case 0:
                    case 3:
                    default:
                        sb.Append((uint)value);
                        break;
                    case 1:
                        sb.AppendFormat("{0:0.000000}", BitConverter.ToSingle(BitConverter.GetBytes(value), 0));
                        break;
                    case 2:
                        sb.Append(strtable[value]);
                        // Console.Write(strtable[value]);
                        break;
                }
                sb.Append(",");
            }
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
