using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Net;
using System.Text;
using System.Xml;


namespace PSJRiver
{
    public sealed class DriveParameters
    {
        [Parameter] public Uri ServerUri { get; set; }
        [Parameter] public string RootId { get; set; }
        [Parameter] [ValidateRange(0, 3600)] public int CacheSeconds { get; set; }
        public DriveParameters() { ServerUri = new Uri("http://localhost:52199/MCWS/v1/"); RootId = "1"; CacheSeconds = 30; }
    }

    internal sealed class Entry
    {
        internal string Id, Segment;
        internal bool Container;
        internal PSObject Value;
    }

    internal sealed class CachedEntries
    {
        internal DateTime Created = DateTime.UtcNow;
        internal List<Entry> Items;
    }

    public sealed class JRiverDrive : PSDriveInfo
    {
        internal Uri Server;
        internal string RootId;
        internal int CacheSeconds;
        internal Dictionary<string, CachedEntries> Cache = new Dictionary<string, CachedEntries>();
        internal JRiverDrive(PSDriveInfo drive, DriveParameters p) : base(drive)
        { Server = new Uri(p.ServerUri.AbsoluteUri.TrimEnd('/') + "/"); RootId = p.RootId; CacheSeconds = p.CacheSeconds; }
    }

    [CmdletProvider("JRiver", ProviderCapabilities.Credentials | ProviderCapabilities.ExpandWildcards)]
    public sealed class JRiverProvider : NavigationCmdletProvider
    {
        private JRiverDrive qualifiedDrive;
        private JRiverDrive Drive { get { return qualifiedDrive ?? (JRiverDrive)PSDriveInfo; } }
        private string Qualified(string path)
        {
            string prefix = Drive.Name + ":";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path : prefix + "\\" + Normalize(path);
        }
        protected override object NewDriveDynamicParameters() { return new DriveParameters(); }
        protected override PSDriveInfo NewDrive(PSDriveInfo drive)
        {
            try
            {
                DriveParameters p = DynamicParameters as DriveParameters ?? new DriveParameters();
                if (drive.Root != "\\" && drive.Root != "/" && drive.Root != "")
                    throw new ArgumentException("Use -Root '\' and optionally -RootId for the MCWS browse root.");
                if (!p.ServerUri.IsAbsoluteUri || (p.ServerUri.Scheme != "http" && p.ServerUri.Scheme != "https") ||
                    !String.IsNullOrEmpty(p.ServerUri.UserInfo) || !String.IsNullOrEmpty(p.ServerUri.Query) || !String.IsNullOrEmpty(p.ServerUri.Fragment))
                    throw new ArgumentException("ServerUri must be an HTTP(S) MCWS base URI without credentials, query or fragment.");
                if (p.ServerUri.Scheme == "http" && !p.ServerUri.IsLoopback)
                    throw new ArgumentException("Use HTTPS for servers outside localhost to protect credentials.");
                long id;
                if (!Int64.TryParse(p.RootId, out id) || id < -1) throw new ArgumentException("RootId must be -1 or a nonnegative browse ID.");
                JRiverDrive result = new JRiverDrive(drive, p);
                // Validate both authentication and the selected root before publishing the drive.
                ReadViews(result, result.RootId);
                return result;
            }
            catch (Exception ex) { Report(ex, drive.Name); return null; }
        }

        private void Report(Exception ex, object target)
        { WriteError(new ErrorRecord(ex, "JRiver.Navigation", ex is ItemNotFoundException ? ErrorCategory.ObjectNotFound : ErrorCategory.ReadError, target)); }

        private static string Request(JRiverDrive drive, string endpoint)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new Uri(drive.Server, endpoint));
                request.Method = "GET";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.AllowAutoRedirect = false;
                if (drive.Credential != null && drive.Credential != PSCredential.Empty)
                {
                    NetworkCredential c = drive.Credential.GetNetworkCredential();
                    request.Headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(drive.Credential.UserName + ":" + c.Password));
                }
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("MCWS returned HTTP " + (int)response.StatusCode + ".");
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                string detail = response == null ? ex.Status.ToString() : "HTTP " + (int)response.StatusCode;
                if (response != null) response.Dispose();
                throw new InvalidOperationException("MCWS request failed (" + detail + "). Check JRiver, ServerUri and -Credential.");
            }
        }

        private static XmlDocument ReadXml(string text)
        {
            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            using (XmlReader reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
            if (doc.DocumentElement == null || doc.DocumentElement.Name != "Response" || doc.DocumentElement.GetAttribute("Status") != "OK")
                throw new InvalidOperationException("MCWS returned an unsuccessful response. Recreate the drive if the browse tree has changed.");
            return doc;
        }

        private static PSObject ObjectWith(params object[] pairs)
        {
            PSObject value = new PSObject();
            for (int i = 0; i < pairs.Length; i += 2) value.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return value;
        }

        // Encode path separators, wildcard characters and the escape marker without losing original names.
        private static string Segment(string name)
        {
            StringBuilder result = new StringBuilder();
            foreach (char c in name)
            {
                if (c < 32 || "%/\\:*?[]`~".IndexOf(c) >= 0)
                    result.Append("%" + ((int)c).ToString("X2", CultureInfo.InvariantCulture));
                else result.Append(c);
            }
            string s = result.ToString();
            return s.Length == 0 ? "%00" : s == "." ? "%2E" : s == ".." ? "%2E%2E" : s;
        }

        private static List<Entry> ReadViews(JRiverDrive drive, string id)
        {
            XmlDocument doc = ReadXml(Request(drive, "Browse/Children?ErrorOnMissing=1&Skip=0&ID=" + Uri.EscapeDataString(id)));
            List<Entry> entries = new List<Entry>();
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement item in doc.SelectNodes("/Response/Item"))
            {
                string name = item.GetAttribute("Name"), segment = Segment(name);
                entries.Add(new Entry { Id = item.InnerText, Segment = segment, Container = true,
                    Value = ObjectWith("Name", name, "Id", item.InnerText, "ItemType", "View", "BrowseType", item.GetAttribute("Type")) });
                counts[segment] = counts.ContainsKey(segment) ? counts[segment] + 1 : 1;
            }
            foreach (Entry entry in entries)
            {
                if (counts[entry.Segment] > 1) entry.Segment += "~" + entry.Id;
                entry.Value.Properties.Add(new PSNoteProperty("PathName", entry.Segment));
                entry.Value.TypeNames.Insert(0, "JRiver.View");
            }
            return entries;
        }

        private List<Entry> Children(Entry parent)
        {
            if (!parent.Container) return new List<Entry>();
            CachedEntries cache;
            if (!Force && Drive.Cache.TryGetValue(parent.Id, out cache) && (DateTime.UtcNow - cache.Created).TotalSeconds < Drive.CacheSeconds) return cache.Items;
            List<Entry> entries = ReadViews(Drive, parent.Id);
            if (entries.Count == 0)
            {
                string json = Request(Drive, "Browse/Files?Action=JSON&Fields=Calculated&NoUI=1&Formatted=0&ResetCache=0&ID=" + Uri.EscapeDataString(parent.Id));
                if (json.TrimStart().StartsWith("<")) ReadXml(json);
                if (!json.TrimStart().StartsWith("[")) throw new InvalidOperationException("MCWS did not return a JSON file array.");
                using (PowerShell shell = PowerShell.Create())
                {
                    var values = shell.AddCommand("ConvertFrom-Json").AddParameter("InputObject", json).Invoke();
                    if (shell.HadErrors) throw new InvalidOperationException("Unable to parse MCWS file metadata.");
                    List<PSObject> files = new List<PSObject>();
                    foreach (PSObject output in values)
                    {
                        Array array = output.BaseObject as Array;
                        if (array == null) files.Add(output);
                        else foreach (object file in array) files.Add(PSObject.AsPSObject(file));
                    }
                    foreach (PSObject value in files)
                    {
                        string key = Convert.ToString(value.Properties["Key"].Value, CultureInfo.InvariantCulture);
                        string name = Convert.ToString(value.Properties["Name"].Value, CultureInfo.InvariantCulture);
                        PSPropertyInfo track = value.Properties["Track #"];
                        int number;
                        string prefix = track != null && Int32.TryParse(Convert.ToString(track.Value), out number) ? number.ToString("D2") + ". " : "";
                        string segment = Segment(prefix + name) + "~" + key;
                        // Keep every JRiver metadata field, including custom fields.
                        value.Properties.Add(new PSNoteProperty("PathName", segment), true);
                        value.TypeNames.Insert(0, "JRiver.Track");
                        entries.Add(new Entry { Id = key, Segment = segment, Container = false, Value = value });
                    }
                }
            }
            Drive.Cache[parent.Id] = new CachedEntries { Items = entries };
            return entries;
        }

        private static string Normalize(string path) { return (path ?? "").Replace('/', '\\').Trim('\\'); }
        private Entry Resolve(string path)
        {
            qualifiedDrive = null;
            int colon = (path ?? "").IndexOf(':');
            if (colon >= 0)
            {
                string driveName = path.Substring(0, colon);
                qualifiedDrive = SessionState.Drive.Get(driveName) as JRiverDrive;
                if (qualifiedDrive == null) throw new ItemNotFoundException("JRiver drive not found: " + driveName);
                path = path.Substring(colon + 1);
            }
            if (Drive == null) throw new ItemNotFoundException("Use a drive-qualified path such as Music:\\.");
            Entry current = new Entry { Id = Drive.RootId, Container = true, Segment = "", Value = ObjectWith("Name", Drive.Name, "Id", Drive.RootId, "ItemType", "View", "PathName", "") };
            string normalized = Normalize(path);
            if (normalized.Length == 0) return current;
            foreach (string part in normalized.Split('\\'))
            {
                Entry next = Children(current).Find(delegate(Entry entry) { return String.Equals(entry.Segment, part, StringComparison.OrdinalIgnoreCase); });
                if (next == null) throw new ItemNotFoundException("JRiver item not found: " + path);
                current = next;
            }
            return current;
        }

        protected override string[] ExpandPath(string path)
        {
            try
            {
                int colon = path.IndexOf(':');
                string root = colon < 0 ? "\\" : path.Substring(0, colon + 1) + "\\";
                string relative = Normalize(colon < 0 ? path : path.Substring(colon + 1));
                Resolve(root);
                List<string> paths = new List<string> { root };
                if (relative.Length == 0) return paths.ToArray();
                foreach (string part in relative.Split('\\'))
                {
                    List<string> matches = new List<string>();
                    bool wildcard = WildcardPattern.ContainsWildcardCharacters(part);
                    WildcardPattern pattern = new WildcardPattern(part, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
                    foreach (string parentPath in paths)
                    {
                        Entry parent = Resolve(parentPath);
                        foreach (Entry item in Children(parent))
                        {
                            string name = Convert.ToString(item.Value.Properties["Name"].Value, CultureInfo.InvariantCulture);
                            // Match display names as well as navigable segments used by tab completion.
                            bool matched = wildcard ? pattern.IsMatch(name) || pattern.IsMatch(item.Segment) :
                                String.Equals(item.Segment, WildcardPattern.Unescape(part), StringComparison.OrdinalIgnoreCase);
                            if (matched) matches.Add(MakePath(parentPath, item.Segment));
                        }
                    }
                    paths = matches;
                    if (paths.Count == 0 || Stopping) break;
                }
                return paths.ToArray();
            }
            catch (PipelineStoppedException) { throw; }
            catch (Exception ex) { Report(ex, path); return new string[0]; }
        }

        protected override bool IsValidPath(string path) { return path != null; }
        protected override bool ItemExists(string path)
        {
            try { Resolve(path); return true; }
            catch (ItemNotFoundException) { return false; }
            catch (Exception ex) { Report(ex, path); return false; }
        }
        protected override bool IsItemContainer(string path)
        {
            try { return Resolve(path).Container; }
            catch (ItemNotFoundException) { return false; }
            catch (Exception ex) { Report(ex, path); return false; }
        }
        protected override void GetItem(string path)
        {
            try { Entry item = Resolve(path); WriteItemObject(item.Value, Qualified(path), item.Container); }
            catch (PipelineStoppedException) { throw; }
            catch (Exception ex) { Report(ex, path); }
        }
        protected override void GetChildItems(string path, bool recurse) { GetChildItems(path, recurse, UInt32.MaxValue); }
        protected override void GetChildItems(string path, bool recurse, uint depth)
        {
            try { ListItems(path, Resolve(path), recurse, depth, new HashSet<string>()); }
            catch (PipelineStoppedException) { throw; }
            catch (Exception ex) { Report(ex, path); }
        }
        private void ListItems(string path, Entry parent, bool recurse, uint depth, HashSet<string> ancestors)
        {
            if (!parent.Container) { WriteItemObject(parent.Value, Qualified(path), false); return; }
            if (!ancestors.Add(parent.Id)) return;
            try
            {
                foreach (Entry item in Children(parent))
                {
                    if (Stopping) break;
                    string childPath = MakePath(path, item.Segment);
                    WriteItemObject(item.Value, Qualified(childPath), item.Container);
                    if (recurse && depth > 0 && item.Container) ListItems(childPath, item, true, depth - 1, ancestors);
                }
            }
            finally { ancestors.Remove(parent.Id); }
        }
        protected override void GetChildNames(string path, ReturnContainers returnContainers)
        {
            try { foreach (Entry item in Children(Resolve(path))) WriteItemObject(item.Segment, Qualified(MakePath(path, item.Segment)), item.Container); }
            catch (PipelineStoppedException) { throw; }
            catch (Exception ex) { Report(ex, path); }
        }
        protected override bool HasChildItems(string path)
        {
            try { return Children(Resolve(path)).Count > 0; }
            catch (Exception ex) { Report(ex, path); return false; }
        }
    }
}
