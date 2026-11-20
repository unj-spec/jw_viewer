package com.novelviewer.app;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.DownloadManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.Settings;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

// 자동 업데이트: GitHub 저장소(unj-spec/jw_viewer)의 최신 릴리스를 보고,
// 지금 앱보다 새 버전이면 물어본 뒤 APK 를 받아 설치 화면을 띄운다.
class Updater {
    static final String REPO = "unj-spec/jw_viewer";
    static final String APK_NAME = "jw_viewer.apk";

    interface Say { void say(String msg); }

    private final Activity act;
    private final Say say;
    private long downloadId = -1;

    Updater(Activity act, Say say) { this.act = act; this.say = say; }

    static String currentVersion(Context c) {
        try {
            return c.getPackageManager().getPackageInfo(c.getPackageName(), 0).versionName;
        } catch (Exception e) {
            return "0";
        }
    }

    // manual: 사용자가 직접 눌렀을 때만 "최신 버전입니다"·오류를 알린다.
    void check(boolean manual) {
        new Thread(() -> {
            try {
                JSONObject rel = new JSONObject(get("https://api.github.com/repos/" + REPO + "/releases/latest"));
                String latest = rel.getString("tag_name").replaceFirst("^v", "");
                String url = null;
                JSONArray assets = rel.getJSONArray("assets");
                for (int i = 0; i < assets.length(); i++) {
                    JSONObject a = assets.getJSONObject(i);
                    if (APK_NAME.equals(a.getString("name"))) url = a.getString("browser_download_url");
                }
                String cur = currentVersion(act);
                if (url == null || compare(latest, cur) <= 0) {
                    if (manual) say.say("최신 버전입니다 (" + cur + ")");
                    return;
                }
                final String u = url;
                String notes = rel.optString("body", "").trim();
                act.runOnUiThread(() -> new AlertDialog.Builder(act)
                        .setTitle("새 버전 " + latest)
                        .setMessage("지금 버전은 " + cur + " 입니다. 업데이트할까요?" + (notes.isEmpty() ? "" : "\n\n" + notes))
                        .setPositiveButton("업데이트", (d, w) -> download(u, latest))
                        .setNegativeButton("나중에", null)
                        .show());
            } catch (Exception e) {
                if (manual) say.say("업데이트를 확인할 수 없습니다. 인터넷 연결을 확인하세요.");
            }
        }).start();
    }

    private void download(String url, String version) {
        // 다른 앱에서 받은 APK 설치를 허용해야 설치 화면이 뜬다. 처음 한 번 설정으로 보낸다.
        if (Build.VERSION.SDK_INT >= 26 && !act.getPackageManager().canRequestPackageInstalls()) {
            say.say("'이 출처 허용'을 켠 뒤 뒤로 와서 업데이트 확인을 다시 누르세요");
            act.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + act.getPackageName())));
            return;
        }
        File dir = act.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS);
        File old = new File(dir, APK_NAME);
        if (old.exists()) old.delete();
        DownloadManager dm = (DownloadManager) act.getSystemService(Context.DOWNLOAD_SERVICE);
        DownloadManager.Request r = new DownloadManager.Request(Uri.parse(url))
                .setTitle("소설 뷰어 " + version)
                .setDescription("업데이트 받는 중")
                .setMimeType("application/vnd.android.package-archive")
                .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE)
                .setDestinationInExternalFilesDir(act, Environment.DIRECTORY_DOWNLOADS, APK_NAME);
        BroadcastReceiver done = new BroadcastReceiver() {
            @Override
            public void onReceive(Context c, Intent i) {
                if (i.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -2) != downloadId) return;
                act.unregisterReceiver(this);
                Uri apk = dm.getUriForDownloadedFile(downloadId);
                if (apk == null) { say.say("업데이트 파일을 받지 못했습니다"); return; }
                Intent install = new Intent(Intent.ACTION_VIEW)
                        .setDataAndType(apk, "application/vnd.android.package-archive")
                        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
                act.startActivity(install);
            }
        };
        IntentFilter f = new IntentFilter(DownloadManager.ACTION_DOWNLOAD_COMPLETE);
        if (Build.VERSION.SDK_INT >= 33) act.registerReceiver(done, f, Context.RECEIVER_EXPORTED);
        else act.registerReceiver(done, f);
        downloadId = dm.enqueue(r);
        say.say("업데이트를 받는 중입니다");
    }

    private static String get(String url) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        try {
            c.setConnectTimeout(8000);
            c.setReadTimeout(15000);
            c.setRequestProperty("Accept", "application/vnd.github+json");
            c.setRequestProperty("User-Agent", "jw_viewer");
            if (c.getResponseCode() != 200) throw new Exception("HTTP " + c.getResponseCode());
            InputStream in = c.getInputStream();
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
            in.close();
            return new String(out.toByteArray(), StandardCharsets.UTF_8);
        } finally {
            c.disconnect();
        }
    }

    // "1.2.10" 과 "1.2.9" 처럼 점으로 나눈 숫자를 차례로 비교한다.
    static int compare(String a, String b) {
        String[] x = a.split("\\."), y = b.split("\\.");
        for (int i = 0; i < Math.max(x.length, y.length); i++) {
            int p = i < x.length ? num(x[i]) : 0, q = i < y.length ? num(y[i]) : 0;
            if (p != q) return p < q ? -1 : 1;
        }
        return 0;
    }

    private static int num(String s) {
        try { return Integer.parseInt(s.replaceAll("\\D.*$", "")); } catch (Exception e) { return 0; }
    }
}
