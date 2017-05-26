using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace arzedit
{
    // Template Objects
    public class TemplateNode
    {
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

        public TemplateNode(TemplateNode aparent = null, string aTemplateFile = null)
        {
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

        public int ParseNode(string[] parsestrings, int parsestart = 0)
        {
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
                }
                else
                { // subitem
                    TemplateNode sub = new TemplateNode(this);
                    i = sub.ParseNode(parsestrings, i);
                    subitems.Add(sub);
                }
                i++;
            }
            return i;
        }

        public List<TemplateNode> findValue(string aval)
        {
            List<TemplateNode> res = new List<TemplateNode>();
            if (values.ContainsValue(aval)) res.Add(this);
            foreach (TemplateNode sub in subitems)
            {
                res.AddRange(sub.findValue(aval));
            }
            return res;
        }

        public TemplateNode FindVariable(string aname)
        {
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
            foreach (TemplateNode sub in subitems)
            {
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

        public void FillIncludes(Dictionary<string, TemplateNode> alltempl)
        {
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
                        // DEBUG:
                        // if (itemplate == this || includes.Contains(itemplate))
                           // Console.WriteLine("WARNING: When parsing template {0} include \"{1}\" found out it's already included by another file, include might be cyclic.", GetTemplateFile(), incstr);
                        includes.Add(itemplate);
                    }
                    else
                    {
                        TemplateNode tproot = this;
                        while (tproot.parent != null) tproot = tproot.parent;
                        string intemplate = alltempl.First(t => t.Value == tproot).Key;
                        // Console.WriteLine("Cannot find include {0} referenced in {1}", incstr, intemplate); // Debug
                    }
                }
                else if (sub.kind == "group")
                {
                    sub.FillIncludes(alltempl);
                }
            }
        }
    }
}