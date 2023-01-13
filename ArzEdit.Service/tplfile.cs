using Microsoft.Extensions.Logging;

namespace ArzEdit.Service;

// Template Objects
public class TemplateNode
{
    public static readonly TemplateNode TemplateNameVar;
    public string TemplateFile;
    public TemplateNode parent;
    public string kind = "";
    public Dictionary<string, string> values = new Dictionary<string, string>();
    public SortedDictionary<string, TemplateNode> varsearch;
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
        var i = parsestart;
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
                var sval = parsestrings[i].Split('=');
                var akey = (sval[0].Trim());
                var aval = (sval[1].Trim().Trim('"'));
                values[akey] = aval;
            }
            else
            { // subitem
                var sub = new TemplateNode(this);
                i = sub.ParseNode(parsestrings, i);
                subitems.Add(sub);
            }
            i++;
        }
        return i;
    }

    public List<TemplateNode> findValue(string aval)
    {
        var res = new List<TemplateNode>();
        if (values.ContainsValue(aval)) res.Add(this);
        foreach (var sub in subitems)
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
        foreach (var sub in subitems)
        {
            res = sub.FindVariable(aname);
            if (res != null)
            {
                if (parent == null) varsearch.Add(aname, res);
                return res;
            }
        }
        // No entry in subitems, check includes
        foreach (var incl in includes)
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
        foreach (var sub in subitems)
        {
            if (sub.kind == "variable" && sub.values.ContainsKey("type") && sub.values["type"] == "include")
            {
                var incstr = sub.values.ContainsKey("value") ? sub.values["value"] : "";
                if (incstr == "")
                    incstr = sub.values.ContainsKey("defaultValue") ? sub.values["defaultValue"] : "";
                incstr = incstr.ToLower().Replace("%template_dir%", "").Replace(Path.DirectorySeparatorChar, '/');
                if (incstr.StartsWith("/")) incstr = incstr.Substring(1);
                if (alltempl.ContainsKey(incstr))
                {
                    // Console.WriteLine("Include {0}", incstr);
                    // Check for cycles
                    var itemplate = alltempl[incstr];
                    // DEBUG:
                    if (itemplate == this || includes.Contains(itemplate))
                        Logger.Log.LogWarning("WARNING: When parsing template {0} include \"{1}\" found out it's already included by another file, include might be cyclic.", GetTemplateFile(), incstr);
                    // Console.WriteLine("WARNING: When parsing template {0} include \"{1}\" found out it's already included by another file, include might be cyclic.", GetTemplateFile(), incstr);
                    includes.Add(itemplate);
                }
                else
                {
                    var tproot = this;
                    while (tproot.parent != null) tproot = tproot.parent;
                    var intemplate = alltempl.First(t => t.Value == tproot).Key;
                    // Console.WriteLine("Cannot find include {0} referenced in {1}", incstr, intemplate); // Debug
                    Logger.Log.LogInformation("Cannot find include {0} referenced in {1}", incstr, intemplate);
                }
            }
            else if (sub.kind == "group")
            {
                sub.FillIncludes(alltempl);
            }
        }
    }
}