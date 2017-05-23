using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LZ4;

namespace arzedit
{

    public class ARCHeader
    {
        public static readonly char[] ARCMAGIC = new char[4] { 'A', 'R', 'C', (char)0x00 };
        public static readonly int HEADER_SIZE = 28;
        public char[] Magic = ARCMAGIC;
        public int Version = 3;
        public int NumberOfFileEntries = 0;
        public int NumberOfDataRecords = 0;
        public int RecordTableSize = 0;
        public int StringTableSize = 0;
        public int RecordTableOffset = 0;

        public void ReadStream(Stream astream)
        {
            using (BinaryReader br = new BinaryReader(astream, System.Text.Encoding.ASCII, true))
            {
                Magic = br.ReadChars(4);
                Version = br.ReadInt32();
                NumberOfFileEntries = br.ReadInt32();
                NumberOfDataRecords = br.ReadInt32();
                RecordTableSize = br.ReadInt32();
                StringTableSize = br.ReadInt32();
                RecordTableOffset = br.ReadInt32();
            }
        }

        public void WriteStream(Stream astream)
        {
            using (BinaryWriter bw = new BinaryWriter(astream, Encoding.ASCII, true))
            {
                bw.Write(Magic);
                bw.Write(Version);
                bw.Write(NumberOfFileEntries);
                bw.Write(NumberOfDataRecords);
                bw.Write(RecordTableSize);
                bw.Write(StringTableSize);
                bw.Write(RecordTableOffset);
            }
        }

        public int GetTocOffset()
        {
            return GetStringTableOffset() + StringTableSize;
        }

        public int GetStringTableOffset()
        {
            return RecordTableOffset + RecordTableSize;
        }

        public bool CheckMagic()
        {
            if (Magic != null)
                return Magic.SequenceEqual(ARCMAGIC);
            return false;
        }

        public void PrintHeader()
        {
            Console.WriteLine("Magic: {0}", CheckMagic() ? "OK" : "Failure");
            Console.WriteLine("Version: {0}", Version);
            Console.WriteLine("Number Of File Entries: {0}", NumberOfFileEntries);
            Console.WriteLine("Number Of Data Records: {0}", NumberOfDataRecords);
            Console.WriteLine("Record Table Size: {0}", RecordTableSize);
            Console.WriteLine("String Table Size: {0}", StringTableSize);
            Console.WriteLine("Record Table Offset: 0x{0:X4}", RecordTableOffset);
        }

    }

    public class ARCStringTable {
        MemoryStream smem = null;

        public ARCStringTable()
        {

        }

        public void ReadFromStream(Stream astream, int tablesize) 
        {
            smem = new MemoryStream(tablesize);
            byte[] buff = new byte[tablesize];
            astream.Read(buff, 0, tablesize);
            smem.Write(buff, 0, tablesize);
        }

        public int WriteToStream(Stream tstream)
        {
            tstream.Write(smem.ToArray(), 0, (int)smem.Length);
            return (int)smem.Length;
        }

        public string GetStringAt(int offset, int len)
        {
            if (smem == null)
            {
                Console.WriteLine("Trying to read uninitialized stringtable!");
                return "";
            }
            
            // smem.Seek(offset, SeekOrigin.Begin);
            byte[] buff = smem.GetBuffer();
            // byte[] buf = new byte[len];
            // smem.Read(buf, 0, len);
            return Encoding.ASCII.GetString(buff, offset, len);
            /*
            using (BinaryReader br = new BinaryReader(smem))
            {
                return new string(br.ReadChars(len));
            }*/
        }

        public int Append(string astring)
        {
            if (smem == null)
                smem = new MemoryStream();
            smem.Seek(0, SeekOrigin.End);
            int pos = (int)smem.Position;
            smem.Write(Encoding.ASCII.GetBytes(astring + Char.MinValue), 0, astring.Length + 1);
            return pos;
        }
    }

    public class ARCTocEntry
    {
        public int EntryType;
        public int FileOffset;
        public int CompressedSize;
        public int DecompressedSize;
        public int DecompressedHash;
        public DateTime FileTime; // 8 Bytes/long
        public int FileParts;
        public int FirstPartIndex;
        public int StringEntryLength;
        public int StringEntryOffset;

        public void ReadStream(Stream astream)
        {
            using (BinaryReader br = new BinaryReader(astream, System.Text.Encoding.ASCII, true)) {
                EntryType = br.ReadInt32();
                FileOffset = br.ReadInt32();
                CompressedSize = br.ReadInt32();
                DecompressedSize = br.ReadInt32();
                DecompressedHash = br.ReadInt32();
                FileTime = DateTime.FromFileTimeUtc(br.ReadInt64());
                FileParts = br.ReadInt32();
                FirstPartIndex = br.ReadInt32();
                StringEntryLength = br.ReadInt32();
                StringEntryOffset = br.ReadInt32();
            }
        }

        public void WriteStream(Stream astream)
        {
            using (BinaryWriter bw = new BinaryWriter(astream, Encoding.ASCII, true))
            {
                // EntryType = br.ReadInt32();
                bw.Write(EntryType);
                // FileOffset = br.ReadInt32();
                bw.Write(FileOffset);
                // CompressedSize = br.ReadInt32();
                bw.Write(CompressedSize);
                // DecompressedSize = br.ReadInt32();
                bw.Write(DecompressedSize);
                // DecompressedHash = br.ReadInt32();
                bw.Write(DecompressedHash);
                // FileTime = DateTime.FromFileTimeUtc(br.ReadInt64());
                bw.Write(FileTime.ToFileTimeUtc());
                // FileParts = br.ReadInt32();
                bw.Write(FileParts);
                // FirstPartIndex = br.ReadInt32();
                bw.Write(FirstPartIndex);
                // StringEntryLength = br.ReadInt32();
                bw.Write(StringEntryLength);
                // StringEntryOffset = br.ReadInt32();
                bw.Write(StringEntryOffset);
            }
        }

        public void PrintTocEntry(ARCStringTable strtable = null) {
            Console.WriteLine("Entry Type: {0}", EntryType);
            Console.WriteLine("File Offset: 0x{0:X4}", FileOffset);
            Console.WriteLine("Compressed Size: {0}", CompressedSize);
            Console.WriteLine("Decompressed Size: {0}", DecompressedSize);
            Console.WriteLine("Decompressed Hash: 0x{0:X4}", DecompressedHash);
            Console.WriteLine("File Time: {0}", FileTime);
            Console.WriteLine("File Parts: {0}", FileParts);
            Console.WriteLine("First Part Index: {0}", FirstPartIndex);
            Console.WriteLine("String Entry Length: {0}", StringEntryLength);
            Console.WriteLine("String Entry Offset: 0x{0:X4}", StringEntryOffset);
            if (strtable != null) {
                Console.WriteLine("String Entry: \"{0}\"", GetEntryString(strtable));
            }
        }

        public string GetEntryString(ARCStringTable strtable = null)
        {
            return strtable != null? strtable.GetStringAt(StringEntryOffset, StringEntryLength): "";
        }

        /*        struct ARC_V3_FILE_TOC_ENTRY
                {
                    unsigned int EntryType;
                    unsigned int FileOffset;
                    unsigned int CompressedSize;
                    unsigned int DecompressedSize;
                    unsigned int DecompressedHash; // Adler32 hash of the decompressed file bytes
                    unsigned __int64    FileTime;
                    unsigned int FileParts;
                    unsigned int FirstPartIndex;
                    unsigned int StringEntryLength;
                    unsigned int StringEntryOffset;
                }
               */
    }

    public class ARCFilePart
    {
        public int PartOffset;
        public int CompressedSize;
        public int DecompressedSize;

        public void ReadStream(Stream astream)
        {
            using (BinaryReader br = new BinaryReader(astream, Encoding.ASCII, true))
            {
                PartOffset = br.ReadInt32();
                CompressedSize = br.ReadInt32();
                DecompressedSize = br.ReadInt32();
            }
        }

        public void PrintFilePart()
        {
            Console.WriteLine("Part Offset: 0x{0:X4}", PartOffset);
            Console.WriteLine("Compressed Size: {0}", CompressedSize);
            Console.WriteLine("Decompressed Size: {0}", DecompressedSize);
        }
    }

    public class ARCWriter : IDisposable
    {
        public static readonly int MAX_BLOCK_SIZE = 256 * 1024;
        ARCHeader whdr = null;
        ARCStringTable wstrs = null;
        List<ARCTocEntry> wtoc = null;
        List<ARCFilePart> wparts = null;
        Stream wstream = null;
        bool started = false;
        bool finished = false;

        public ARCWriter(Stream outstream)
        {
            wstream = outstream;

            wstrs = new ARCStringTable();
            wtoc = new List<ARCTocEntry>();
            wparts = new List<ARCFilePart>();
            BeginWrite();
        }

        public int WriteRecordTable(List<ARCFilePart> aparts, Stream astream)
        {
            using (BinaryWriter bw = new BinaryWriter(astream, Encoding.ASCII, true))
                for (int p = 0; p < aparts.Count; p++)
                {
                    bw.Write(aparts[p].PartOffset);
                    bw.Write(aparts[p].CompressedSize);
                    bw.Write(aparts[p].DecompressedSize);
                }
            return aparts.Count * 12;
        }

        public void WriteFromTocEntry(ARCFile afile, ARCTocEntry aentry)
        {
            string entryname = aentry.GetEntryString(afile.strs);
            ARCTocEntry newentry = new ARCTocEntry();
            newentry.StringEntryOffset = wstrs.Append(entryname);
            newentry.StringEntryLength = entryname.Length;
            newentry.FileOffset = (int)wstream.Position;
            newentry.FileParts = aentry.FileParts;
            newentry.FirstPartIndex = wparts.Count;
            // Write compressed parts:
            for (int p = 0; p < aentry.FileParts; p++)
            {
                ARCFilePart newpart = new ARCFilePart();
                ARCFilePart cpart = afile.parts[aentry.FirstPartIndex + p];
                newpart.CompressedSize = cpart.CompressedSize;
                newpart.DecompressedSize = cpart.DecompressedSize;
                newpart.PartOffset = (int)wstream.Position; // Remember offset
                // Get compressed part
                byte[] cbuff = new byte[cpart.CompressedSize];
                afile.fstream.Seek(cpart.PartOffset, SeekOrigin.Begin);
                afile.fstream.Read(cbuff, 0, cpart.CompressedSize);
                // Write actual part:
                wstream.Write(cbuff, 0, cpart.CompressedSize);
                wparts.Add(newpart); // Addit to written part list
            }
            // Now we'll trust original entry has all correct data
            newentry.EntryType = aentry.EntryType;
            newentry.CompressedSize = aentry.CompressedSize;
            newentry.DecompressedSize = aentry.DecompressedSize;
            newentry.DecompressedHash = aentry.DecompressedHash;
            newentry.FileTime = aentry.FileTime;
            wtoc.Add(newentry); // Add it to write TOC
        }

        public void WriteFromStream(string entryname, DateTime entrytime, Stream astream)
        {
            ARCTocEntry newentry = new ARCTocEntry();
            newentry.StringEntryOffset = wstrs.Append(entryname);
            newentry.StringEntryLength = entryname.Length;
            newentry.FileOffset = (int)wstream.Position;
            // newentry.FileParts = (int)astream.Length / MAX_BLOCK_SIZE;
            newentry.FirstPartIndex = wparts.Count;
            int read = 0, partcount = 0, csize = 0;
            byte[] buffer = new byte[MAX_BLOCK_SIZE];
            // Adler32;
            Adler32 adler = new Adler32();
            while ((read = astream.Read(buffer, 0, MAX_BLOCK_SIZE)) > 0) {
                // newblock
                adler.ComputeHash(buffer, 0, read);
                ARCFilePart newpart = new ARCFilePart();
                newpart.PartOffset = (int)wstream.Position;
                newpart.DecompressedSize = read;
                byte[] cbuffer = LZ4Codec.Encode(buffer, 0, read);
                newpart.CompressedSize = cbuffer.Length;
                wstream.Write(cbuffer, 0, cbuffer.Length);
                wparts.Add(newpart);
                csize += cbuffer.Length;
                partcount += 1;
            }
            newentry.FileParts = partcount;
            newentry.EntryType = 3; // TODO: What are other possible types
            newentry.CompressedSize = csize;
            newentry.DecompressedSize = (int)astream.Length;
            newentry.DecompressedHash = (int)adler.checksum;
            newentry.FileTime = entrytime;
            wtoc.Add(newentry);
        }

        public void BeginWrite()
        {
            // Write header
            if (!started)
            {
                whdr = new ARCHeader();
                // whdr.WriteStream(wstream); // Empty header for now
                byte[] zeroes = new byte[0x800];
                Array.Clear(zeroes, 0, 0x800);
                wstream.Write(zeroes, 0, 0x800);
                started = true;
            }
        }

        public void FinishWrite()
        {
            whdr.RecordTableOffset = (int)wstream.Position;
            whdr.RecordTableSize = WriteRecordTable(wparts, wstream); // Write part table
            whdr.NumberOfDataRecords = wparts.Count;
            whdr.StringTableSize = wstrs.WriteToStream(wstream);
            // Now write TOC table
            whdr.NumberOfFileEntries = wtoc.Count;
            for (int i = 0; i < wtoc.Count; i++)
            {
                wtoc[i].WriteStream(wstream);
            }
            // Rewrite finished header:
            wstream.Seek(0, SeekOrigin.Begin);
            whdr.WriteStream(wstream);
            wstream.Seek(0, SeekOrigin.End); // Move cursor to end
            finished = true;
        }

        public void Dispose() {
            if (started && !finished)
                FinishWrite();
        }

        ~ARCWriter() {
            Dispose();
        }

    }

    public class ARCFile
    {
        public ARCHeader hdr = null;
        public ARCStringTable strs = null;
        public List<ARCTocEntry> toc = null;
        public List<ARCFilePart> parts = null;
        public Stream fstream = null;
        public ARCFile()
        {
        }

        public void ReadStream(Stream astream)
        {
            fstream = astream;
            hdr = new ARCHeader();
            hdr.ReadStream(astream);
            hdr.PrintHeader();

            // Read part table:
            // TODO - take out creating and disposing of BinaryReader out of the loop
            parts = new List<ARCFilePart>();
            astream.Seek(hdr.RecordTableOffset, SeekOrigin.Begin);
            for (int i = 0; i < hdr.NumberOfDataRecords; i++)
            {
                ARCFilePart prec = new ARCFilePart();
                prec.ReadStream(astream);
                parts.Add(prec);
            }

            // Now read stringtable
            strs = new ARCStringTable();
            astream.Seek(hdr.GetStringTableOffset(), SeekOrigin.Begin);
            strs.ReadFromStream(astream, hdr.StringTableSize);           

            // Now read TOC:
            toc = new List<ARCTocEntry>();
            toc.Capacity = hdr.NumberOfFileEntries;
            astream.Seek(hdr.GetTocOffset(), SeekOrigin.Begin);
            for (int i = 0; i < hdr.NumberOfFileEntries; i++)
            {
                ARCTocEntry tocentry = new ARCTocEntry();
                tocentry.ReadStream(astream);
                if (tocentry.GetEntryString(strs) == "")
                    tocentry.PrintTocEntry(strs); // Debug
                toc.Add(tocentry);
            }
        }

        public void UnpackToStream(ARCTocEntry aentry, Stream tostream)
        {
            if (aentry.EntryType == 1 && aentry.CompressedSize == aentry.DecompressedSize)
            {
                fstream.Seek(aentry.FileOffset, SeekOrigin.Begin);
                CopyBytes(aentry.CompressedSize, fstream, tostream);
            } else
            {
                // Console.WriteLine("File is packed!"); // DEBUG
                for (int p = 0; p < aentry.FileParts; p++) {
                    ARCFilePart cpart = parts[aentry.FirstPartIndex + p];
                    byte[] cbuff = new byte[cpart.CompressedSize];
                    fstream.Seek(cpart.PartOffset, SeekOrigin.Begin);
                    fstream.Read(cbuff, 0, cpart.CompressedSize);
                    if (cpart.CompressedSize < cpart.DecompressedSize)
                    {
                        byte[] dbuff = LZ4Codec.Decode(cbuff, 0, cpart.CompressedSize, cpart.DecompressedSize);
                        tostream.Write(dbuff, 0, cpart.DecompressedSize);
                    } else
                    {
                        tostream.Write(cbuff, 0, cpart.DecompressedSize);
                    }
                    // byte[] dbuff = new byte[cpart.DecompressedSize];
                }
                        
            }
        }

        public void RepackToStream(Stream tstream)
        {
            // BeginWrite(tstream);
            using (ARCWriter awriter = new ARCWriter(tstream))
            {
                foreach (ARCTocEntry entry in toc)
                {
                    awriter.WriteFromTocEntry(this, entry);
                }
            }
        }

        public void RepackToFile(string filename) {
            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                RepackToStream(fs);
            }
        }


        public void UnpackToFile(ARCTocEntry aentry, string filename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                UnpackToStream(aentry, fs);
            }
        }

        public void UnpackAll(string tofolder)
        {
            foreach (ARCTocEntry aentry in toc)
            {
                string entrystr = aentry.GetEntryString(strs);
                if (aentry.EntryType != 3)
                    aentry.PrintTocEntry();
                if (string.IsNullOrEmpty(entrystr)) continue; // TODO: Important - What are these empty entries? they have some packed data, but have no blocks and type is 0
                string afilename = Path.Combine(tofolder, entrystr.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(afilename));
                // Console.WriteLine("Unpacking \"{0}\"", afilename);                
                // DEBUG:
                /*
                if (aentry.FileParts > 1)
                {
                    aentry.PrintTocEntry(strs);
                    for (int p = 0; p < aentry.FileParts; p++) {
                        Console.WriteLine("Part {0}:", p);
                        parts[aentry.FirstPartIndex + p].PrintFilePart();
                    }
                }
                */
                UnpackToFile(aentry, afilename);
            }
        }

        public static long CopyBytes(long bytesRequired, Stream inStream, Stream outStream)
        {
            long readSoFar = 0L;
            var buffer = new byte[64 * 1024];
            do
            {
                var toRead = Math.Min(bytesRequired - readSoFar, buffer.Length);
                var readNow = inStream.Read(buffer, 0, (int)toRead);
                if (readNow == 0)
                    break; // End of stream
                outStream.Write(buffer, 0, readNow);
                readSoFar += readNow;
            } while (readSoFar < bytesRequired);
            return readSoFar;
        }
    }

    // Util:
    public class Adler32 // Make this non static
    {
        public uint checksum = 1;

        public Adler32(uint initchecksum = 1)
        {
            checksum = initchecksum;
        }

        /// <summary>Performs the hash algorithm on given data array.</summary>
        /// <param name="bytesArray">Input data.</param>
        /// <param name="byteStart">The position to begin reading from.</param>
        /// <param name="bytesToRead">How many bytes in the bytesArray to read.</param>
        public uint ComputeHash(byte[] bytesArray, int byteStart, int bytesToRead)
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
}
