// 자동 업데이트: GitHub 저장소(unj-spec/jw_viewer)의 최신 릴리스를 보고,
// 지금 프로그램보다 새 버전이면 물어본 뒤 설치 파일을 받아 조용히 설치하고 다시 켠다.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NovelViewer
{
    static class Updater
    {
        const string Repo = "unj-spec/jw_viewer";
        const string SetupName = "jw_viewer_pc_setup.exe";

        public static string Current { get { return AppInfo.Version; } }

        class Release { public string Version, Url, Notes; }

        static Release Latest()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
            req.UserAgent = "jw_viewer";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000;
            string json;
            using (var res = req.GetResponse())
            using (var r = new StreamReader(res.GetResponseStream())) json = r.ReadToEnd();
            var o = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            var rel = new Release { Version = ((string)o["tag_name"]).TrimStart('v'), Notes = o.ContainsKey("body") ? (o["body"] as string ?? "").Trim() : "" };
            foreach (Dictionary<string, object> a in (IEnumerable)o["assets"])
                if ((string)a["name"] == SetupName) rel.Url = (string)a["browser_download_url"];
            return rel;
        }

        // manual: 사용자가 직접 눌렀을 때만 "최신 버전입니다"·오류를 알린다.
        public static async Task Check(Form owner, bool manual, Action<string> toast)
        {
            Release rel;
            try { rel = await Task.Run(() => Latest()); }
            catch
            {
                if (manual) toast("업데이트를 확인할 수 없습니다. 인터넷 연결을 확인하세요.");
                return;
            }
            if (rel.Url == null || Compare(rel.Version, Current) <= 0)
            {
                if (manual) toast("최신 버전입니다 (" + Current + ")");
                return;
            }
            string msg = "새 버전 " + rel.Version + " 이 나왔습니다. (지금 " + Current + ")\n업데이트할까요? 설치가 끝나면 다시 켜집니다." +
                (rel.Notes.Length > 0 ? "\n\n" + rel.Notes : "");
            if (MessageBox.Show(owner, msg, "소설 뷰어 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;

            toast("업데이트를 받는 중입니다…");
            string file = Path.Combine(Path.GetTempPath(), SetupName);
            try
            {
                await Task.Run(() =>
                {
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "jw_viewer";
                        wc.DownloadFile(rel.Url, file);
                    }
                });
            }
            catch
            {
                MessageBox.Show(owner, "업데이트 파일을 받지 못했습니다. 잠시 뒤 다시 해 보세요.", "소설 뷰어", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // 설치 파일이 이 프로그램을 닫고 새 버전을 깐 뒤 다시 켠다.
            Process.Start(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART");
            Application.Exit();
        }

        // "1.2.10" 과 "1.2.9" 처럼 점으로 나눈 숫자를 차례로 비교한다.
        public static int Compare(string a, string b)
        {
            string[] x = a.Split('.'), y = b.Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? Num(x[i]) : 0, q = i < y.Length ? Num(y[i]) : 0;
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }

        static int Num(string s)
        {
            int n = 0;
            foreach (char c in s) { if (c < '0' || c > '9') break; n = n * 10 + (c - '0'); }
            return n;
        }
    }
}
