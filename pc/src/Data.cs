// 소설 뷰어 PC 버전 — 저장하는 설정과 소설을 읽어 오는 곳(PC 폴더, GitHub).
// 모바일 앱과는 따로 관리한다. 서버 없이 이 프로그램이 직접 파일과 GitHub 을 읽는다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace NovelViewer
{
    public class Settings
    {
        public int FontSize = 18;
        public double LineHeight = 1.9;
        public int SideMargin = 48;   // 양옆 여백(px) — 글 폭은 창 폭에 맞춰 바뀐다
        public int TopMargin = 32;    // 위아래 여백(px)
        public string Theme = "light";
        public bool Auto = true;
        public string Mode = "page";
        public bool Nav = true;
    }

    public class RecentItem { public string Id; public string Name; public long Time; }
    public class Mark { public int P; public string Text; public string Pct; public long Time; }

    // %APPDATA%\NovelViewer\pc.json 에 저장한다.
    public class Data
    {
        public Settings Settings = new Settings();
        public List<RecentItem> Recent = new List<RecentItem>();
        public Dictionary<string, int> Pos = new Dictionary<string, int>();
        public Dictionary<string, List<Mark>> Marks = new Dictionary<string, List<Mark>>();
        public List<string> Folders = new List<string>();
        public string Current;
        public string NavTab = "recent";
        public string PcDir = "";
        public string GhDir = "";
        public string GhRepo = "";
        public string GhRef = "";
        public string GhRoot = "";
        public string GhToken = "";   // 윈도우 사용자 계정으로 암호화한 값
        public int WinX, WinY, WinW, WinH;
        public bool WinMax;

        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovelViewer");
        static string FilePath { get { return Path.Combine(Dir, "pc.json"); } }

        public static Data Load()
        {
            Data d = null;
            try { d = new JavaScriptSerializer().Deserialize<Data>(File.ReadAllText(FilePath, Encoding.UTF8)); } catch { }
            if (d == null)
            {
                d = new Data();
                // 처음 켤 때: 예전에 쓰던 폴더 목록(folders.txt)이 있으면 가져온다.
                try
                {
                    foreach (var l in File.ReadAllLines(Path.Combine(Dir, "folders.txt"), Encoding.UTF8))
                        if (l.Trim().Length > 0 && Directory.Exists(l.Trim())) d.Folders.Add(l.Trim());
                }
                catch { }
            }
            if (d.Settings == null) d.Settings = new Settings();
            if (d.Recent == null) d.Recent = new List<RecentItem>();
            if (d.Pos == null) d.Pos = new Dictionary<string, int>();
            if (d.Marks == null) d.Marks = new Dictionary<string, List<Mark>>();
            if (d.Folders == null) d.Folders = new List<string>();
            return d;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath + ".tmp", new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(FilePath + ".tmp", FilePath, null);
                else File.Move(FilePath + ".tmp", FilePath);
            }
            catch { }
        }

        [ScriptIgnore]
        public string Token
        {
            get
            {
                if (string.IsNullOrEmpty(GhToken)) return "";
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(GhToken), null, DataProtectionScope.CurrentUser)); }
                catch { return ""; }
            }
            set
            {
                GhToken = string.IsNullOrEmpty(value) ? "" :
                    Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            }
        }

        // 화면에 보이는 저장소 이름(예: unj-spec/nov/abi). 토큰이 없으면 빈 값.
        [ScriptIgnore]
        public string GhName { get { return Token.Length == 0 ? "" : GhRepo + (GhRoot.Length > 0 ? "/" + GhRoot : ""); } }

        // 저장소 주소: "아이디/저장소", https://github.com/아이디/저장소, 폴더 주소 .../tree/브랜치/폴더 (그 폴더부터 보여준다)
        public void SetGithub(string repo, string token)
        {
            string r = repo.Trim();
            r = System.Text.RegularExpressions.Regex.Replace(r, "^https?://github\\.com/", "");
            r = System.Text.RegularExpressions.Regex.Replace(r, "\\.git$|/+$", "");
            string[] s = r.Split('/');
            GhRepo = s.Length >= 2 ? s[0] + "/" + s[1] : s[0];
            GhRef = ""; GhRoot = "";
            if (s.Length >= 4 && s[2] == "tree")
            {
                GhRef = s[3];
                GhRoot = string.Join("/", s.Skip(4).Select(x => Uri.UnescapeDataString(x)));
            }
            if (token.Trim().Length > 0) Token = token.Trim();
        }
    }

    public class Entry { public string Name; public string Path; public bool IsDir; public long Size; public DateTime? Mtime; }
    public class Listing { public string Dir; public string Parent; public List<Entry> Entries = new List<Entry>(); }
    public class FileData { public string Name; public string Text; public string Mtime; }

    public class SourceError : Exception { public SourceError(string m) : base(m) { } }

    public static class Sources
    {
        static readonly string[] TextExt = { ".txt", ".md", ".text" };
        public static bool IsText(string name) { return TextExt.Contains(Path.GetExtension(name).ToLowerInvariant()); }

        public static string SrcOf(string id) { return id.StartsWith("gh:") ? "gh" : "pc"; }

        // ---- 글자 인코딩: UTF-8(BOM 포함), UTF-16, 그 외에는 CP949(메모장 ANSI) ----
        public static string Decode(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xef && b[1] == 0xbb && b[2] == 0xbf) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            if (b.Length >= 2 && b[0] == 0xff && b[1] == 0xfe) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xfe && b[1] == 0xff) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            try { return new UTF8Encoding(false, true).GetString(b); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(949).GetString(b); }
        }

        // 숫자는 크기로 비교하는 이름 정렬(1, 2, 10 순)
        public static int Natural(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char x = a[i], y = b[j];
                if (char.IsDigit(x) && char.IsDigit(y))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length - nb.Length;
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    if (x != y) return x.CompareTo(y);
                    i++; j++;
                }
            }
            return (a.Length - i) - (b.Length - j);
        }

        static void Sort(List<Entry> list)
        {
            list.Sort((a, b) => a.IsDir != b.IsDir ? (a.IsDir ? -1 : 1) : Natural(a.Name, b.Name));
        }

        // ---- PC 폴더 ----
        // dir 이 비어 있으면 등록한 폴더 목록
        public static Listing BrowsePc(Data data, string dir)
        {
            var l = new Listing();
            if (string.IsNullOrEmpty(dir))
            {
                l.Dir = ""; l.Parent = null;
                foreach (var f in data.Folders) l.Entries.Add(new Entry { Name = f, Path = f, IsDir = true });
                return l;
            }
            if (!Directory.Exists(dir)) throw new SourceError("폴더를 열 수 없습니다.");
            foreach (var d in Directory.GetDirectories(dir))
            {
                string n = Path.GetFileName(d);
                if (n.StartsWith(".") || n.StartsWith("$")) continue;
                l.Entries.Add(new Entry { Name = n, Path = d, IsDir = true });
            }
            foreach (var f in Directory.GetFiles(dir))
            {
                string n = Path.GetFileName(f);
                if (n.StartsWith(".") || !IsText(n)) continue;
                var fi = new FileInfo(f);
                l.Entries.Add(new Entry { Name = n, Path = f, Size = fi.Length, Mtime = fi.LastWriteTime });
            }
            Sort(l.Entries);
            bool isRoot = data.Folders.Any(f => string.Equals(Path.GetFullPath(f).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            l.Dir = dir;
            l.Parent = isRoot ? "" : Path.GetDirectoryName(dir);
            return l;
        }

        public static FileData ReadPc(string path)
        {
            if (!File.Exists(path)) throw new SourceError("파일을 찾을 수 없습니다.");
            return new FileData { Name = Path.GetFileName(path), Text = Decode(File.ReadAllBytes(path)), Mtime = File.GetLastWriteTimeUtc(path).Ticks.ToString() };
        }

        public static string StatPc(string path)
        {
            if (!File.Exists(path)) throw new SourceError("파일을 찾을 수 없습니다.");
            return File.GetLastWriteTimeUtc(path).Ticks.ToString();
        }

        // ---- GitHub ----
        static byte[] GhRequest(Data d, string path, bool raw)
        {
            string token = d.Token;
            if (d.GhRepo.Length == 0 || token.Length == 0) throw new SourceError("GitHub 저장소가 설정되지 않았습니다.");
            var url = new StringBuilder("https://api.github.com/repos/").Append(d.GhRepo).Append("/contents");
            foreach (var seg in path.Split('/')) if (seg.Length > 0) url.Append('/').Append(Uri.EscapeDataString(seg));
            if (d.GhRef.Length > 0) url.Append("?ref=").Append(Uri.EscapeDataString(d.GhRef));
            var req = (HttpWebRequest)WebRequest.Create(url.ToString());
            req.Timeout = 20000;
            req.Headers["Authorization"] = "Bearer " + token;
            req.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            req.Accept = raw ? "application/vnd.github.raw" : "application/vnd.github+json";
            req.UserAgent = "NovelViewer";
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            try
            {
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var s = res.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (WebException e)
            {
                var r = e.Response as HttpWebResponse;
                if (r == null) throw new SourceError("GitHub에 연결할 수 없습니다. 인터넷 연결을 확인하세요.");
                int code = (int)r.StatusCode;
                if (code == 401) throw new SourceError("GitHub 토큰이 맞지 않습니다.");
                if (code == 403 || code == 404) throw new SourceError("저장소나 파일을 찾을 수 없습니다. 저장소 이름과 토큰 권한을 확인하세요.");
                throw new SourceError("GitHub 오류 " + code);
            }
        }

        static object GhJson(Data d, string path)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return js.DeserializeObject(Encoding.UTF8.GetString(GhRequest(d, path, false)));
        }

        // 처음 보는 폴더는 설정한 폴더(예: abi). 그보다 위로는 올라가지 않는다.
        public static Listing BrowseGh(Data d, string dir)
        {
            string root = d.GhRoot ?? "";
            dir = (dir ?? "").Trim('/');
            if (dir.Length == 0) dir = root;
            var arr = GhJson(d, dir) as object[];
            if (arr == null) throw new SourceError("폴더를 열 수 없습니다.");
            var l = new Listing();
            foreach (Dictionary<string, object> e in arr)
            {
                string name = (string)e["name"], type = (string)e["type"];
                if (name.StartsWith(".")) continue;
                bool isDir = type == "dir";
                if (!isDir && !(type == "file" && IsText(name))) continue;
                l.Entries.Add(new Entry { Name = name, Path = (string)e["path"], IsDir = isDir, Size = Convert.ToInt64(e["size"]) });
            }
            Sort(l.Entries);
            int cut = dir.LastIndexOf('/');
            l.Dir = dir == root ? "" : dir;
            l.Parent = dir == root ? null : cut < 0 ? "" : dir.Substring(0, cut);
            return l;
        }

        // 파일이 바뀌었는지는 sha 로 판단한다(GitHub 은 파일 저장 시각을 주지 않는다).
        public static FileData ReadGh(Data d, string path)
        {
            var f = GhJson(d, path) as Dictionary<string, object>;
            if (f == null) throw new SourceError("파일을 찾을 수 없습니다.");
            string b64 = f.ContainsKey("content") ? (f["content"] as string ?? "") : "";
            // 1MB 가 넘는 파일은 content 가 비어 오므로 원본을 따로 받는다.
            byte[] bytes = b64.Length > 0 ? Convert.FromBase64String(b64.Replace("\n", "").Replace("\r", "")) : GhRequest(d, path, true);
            return new FileData { Name = (string)f["name"], Text = Decode(bytes), Mtime = (string)f["sha"] };
        }

        public static string StatGh(Data d, string path)
        {
            var f = GhJson(d, path) as Dictionary<string, object>;
            if (f == null) throw new SourceError("파일을 찾을 수 없습니다.");
            return (string)f["sha"];
        }

        // ---- 공통 ----
        public static FileData Read(Data d, string id)
        {
            return SrcOf(id) == "gh" ? ReadGh(d, id.Substring(3)) : ReadPc(id.Substring(3));
        }

        public static string Stat(Data d, string id)
        {
            return SrcOf(id) == "gh" ? StatGh(d, id.Substring(3)) : StatPc(id.Substring(3));
        }

        // 같은 폴더의 txt 목록(다음 화·이전 화를 찾는 데 쓴다)
        public static List<string> Siblings(Data d, string id)
        {
            var ids = new List<string>();
            string src = SrcOf(id), p = id.Substring(3);
            if (src == "pc")
            {
                string dir = Path.GetDirectoryName(p);
                var files = Directory.GetFiles(dir).Where(f => IsText(f) && !Path.GetFileName(f).StartsWith(".")).ToList();
                files.Sort((a, b) => Natural(Path.GetFileName(a), Path.GetFileName(b)));
                foreach (var f in files) ids.Add("pc:" + f);
            }
            else
            {
                int cut = p.LastIndexOf('/');
                var l = BrowseGh(d, cut < 0 ? "" : p.Substring(0, cut));
                foreach (var e in l.Entries) if (!e.IsDir) ids.Add("gh:" + e.Path);
            }
            return ids;
        }
    }
}
