package com.novelviewer.app;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.database.Cursor;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.util.Base64;
import android.view.KeyEvent;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.Charset;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

// 소설 뷰어 앱. 뷰어 화면(index.html)은 앱 안에 들어 있고,
// PC 소설은 PC에서 돌아가는 server.js 에 물어보고, 폰 소설은 폰의 파일을 직접 읽는다.
public class MainActivity extends Activity {
    // 뷰어 화면의 주소. 실제 인터넷 주소가 아니라 앱이 직접 응답한다(아래 shouldInterceptRequest).
    private static final String HOST = "app.novelviewer";
    private static final String HOME = "http://" + HOST + "/";
    private static final String SETUP = "file:///android_asset/setup.html";
    private static final int PICK_FILE = 1;

    private WebView web;
    private SharedPreferences prefs;
    private volatile boolean volumeKeys = true;
    private boolean barsShown = true;

    @Override
    protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        // 읽는 동안 화면이 꺼지지 않게 한다.
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        // 화면 전체(상태바·카메라 구멍 영역 포함)를 쓴다. 상태바를 숨기고 보일 때 글자가 들썩이지 않게 하려는 것.
        if (Build.VERSION.SDK_INT >= 28) {
            getWindow().getAttributes().layoutInDisplayCutoutMode =
                    WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
        }
        if (Build.VERSION.SDK_INT >= 30) getWindow().setDecorFitsSystemWindows(false);
        getWindow().setStatusBarColor(Color.argb(0x88, 0, 0, 0));
        getWindow().setNavigationBarColor(Color.argb(0x88, 0, 0, 0));

        prefs = getSharedPreferences("novelviewer", MODE_PRIVATE);
        volumeKeys = prefs.getBoolean("volumeKeys", true);

        web = new WebView(this);
        web.setBackgroundColor(Color.rgb(0xfb, 0xfa, 0xf7));
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setCacheMode(WebSettings.LOAD_NO_CACHE);
        s.setTextZoom(100);
        web.setLongClickable(false);
        web.setOnLongClickListener(v -> true);
        web.addJavascriptInterface(new Bridge(), "Android");
        web.setWebViewClient(new WebViewClient() {
            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest req) {
                Uri u = req.getUrl();
                if (!HOST.equals(u.getHost())) return null;
                String p = u.getPath() == null ? "/" : u.getPath();
                if (p.equals("/") || p.equals("/index.html")) return asset("index.html", "text/html");
                if (p.startsWith("/api/")) return proxy(u);
                if (p.startsWith("/gh/")) return github(p, u);
                return text(404, "Not found");
            }
        });
        setContentView(web);
        setBars(true);

        // 처음 실행이면 PC 주소부터 묻는다(건너뛰고 폰 파일만 볼 수도 있다).
        if (!prefs.contains("server")) openSetup(false);
        else web.loadUrl(HOME);

        // 켤 때마다 새 버전이 있는지 조용히 확인한다(있을 때만 물어본다).
        updater = new Updater(this, msg -> runOnUiThread(() ->
                web.evaluateJavascript("window.toast && toast(" + JSONObject.quote(msg) + ")", null)));
        updater.check(false);
    }

    private Updater updater;

    // ---- 뷰어 화면이 부르는 /api/... 를 PC 서버로 넘긴다 ----
    private WebResourceResponse proxy(Uri u) {
        String server = server();
        if (server.isEmpty()) return text(503, "PC 주소가 없습니다.");
        HttpURLConnection c = null;
        try {
            String base = server.startsWith("http://") || server.startsWith("https://") ? server : "http://" + server;
            c = (HttpURLConnection) new URL(base + u.getEncodedPath() + (u.getEncodedQuery() == null ? "" : "?" + u.getEncodedQuery())).openConnection();
            c.setConnectTimeout(4000);
            c.setReadTimeout(15000);
            int code = c.getResponseCode();
            InputStream in = code < 400 ? c.getInputStream() : c.getErrorStream();
            byte[] body = in == null ? new byte[0] : readAll(in);
            String type = c.getContentType() == null ? "application/json" : c.getContentType().split(";")[0];
            return new WebResourceResponse(type, "utf-8", code, code < 400 ? "OK" : "Error",
                    Collections.singletonMap("Cache-Control", "no-store"), new ByteArrayInputStream(body));
        } catch (IOException e) {
            return text(502, "PC에 연결할 수 없습니다.");
        } finally {
            if (c != null) c.disconnect();
        }
    }

    // ---- GitHub 저장소의 소설 ----
    // 뷰어 화면이 부르는 /gh/browse·file·stat 을 GitHub API 로 바꿔 묻고, PC 서버와 같은 모양의 JSON 으로 돌려준다.
    // 토큰은 앱에만 저장하고 화면(JS)에는 넘기지 않는다.
    private WebResourceResponse github(String p, Uri u) {
        String repo = prefs.getString("ghRepo", ""), token = prefs.getString("ghToken", "");
        if (repo.isEmpty() || token.isEmpty()) return text(428, "GitHub 저장소가 설정되지 않았습니다.");
        try {
            JSONObject out = new JSONObject();
            if (p.equals("/gh/browse")) {
                // 처음 보는 폴더는 설정한 폴더(예: abi). 그보다 위로는 올라가지 않는다.
                String root = prefs.getString("ghRoot", "");
                String dir = trimSlash(u.getQueryParameter("dir"));
                if (dir.isEmpty()) dir = root;
                JSONArray list = new JSONArray(new String(ghGet(repo, token, dir), StandardCharsets.UTF_8));
                List<JSONObject> entries = new ArrayList<>();
                for (int i = 0; i < list.length(); i++) {
                    JSONObject e = list.getJSONObject(i);
                    String name = e.getString("name"), type = e.getString("type");
                    if (name.startsWith(".")) continue;
                    boolean isDir = type.equals("dir");
                    if (!isDir && !(type.equals("file") && name.toLowerCase(Locale.ROOT).matches(".*\\.(txt|md|text)$"))) continue;
                    JSONObject o = new JSONObject();
                    o.put("name", name).put("path", e.getString("path")).put("type", isDir ? "dir" : "file").put("size", e.optLong("size"));
                    entries.add(o);
                }
                // 폴더 먼저, 이름은 번호 순(2 다음에 10)
                Collections.sort(entries, (a, b) -> {
                    boolean da = a.optString("type").equals("dir"), db = b.optString("type").equals("dir");
                    return da != db ? (da ? -1 : 1) : natural(a.optString("name"), b.optString("name"));
                });
                int cut = dir.lastIndexOf('/');
                out.put("dir", dir.equals(root) ? "" : dir)
                        .put("parent", dir.equals(root) ? JSONObject.NULL : cut < 0 ? "" : dir.substring(0, cut))
                        .put("entries", new JSONArray(entries));
            } else if (p.equals("/gh/file") || p.equals("/gh/stat")) {
                String path = trimSlash(u.getQueryParameter("path"));
                JSONObject f = new JSONObject(new String(ghGet(repo, token, path), StandardCharsets.UTF_8));
                // 파일이 바뀌었는지는 sha 로 판단한다(GitHub 은 파일 저장 시각을 주지 않는다).
                out.put("mtime", f.getString("sha"));
                if (p.equals("/gh/file")) {
                    String b64 = f.optString("content", "");
                    // 1MB 가 넘는 파일은 content 가 비어 오므로 원본을 따로 받는다.
                    byte[] bytes = b64.isEmpty() ? ghGetRaw(repo, token, path) : Base64.decode(b64, Base64.DEFAULT);
                    out.put("path", path).put("name", f.getString("name")).put("text", decodeText(bytes));
                }
            } else return text(404, "Not found");
            return new WebResourceResponse("application/json", "utf-8", 200, "OK",
                    Collections.singletonMap("Cache-Control", "no-store"),
                    new ByteArrayInputStream(out.toString().getBytes(StandardCharsets.UTF_8)));
        } catch (GhError e) {
            return text(e.code, e.getMessage());
        } catch (Exception e) {
            return text(502, "GitHub에 연결할 수 없습니다.");
        }
    }

    private static class GhError extends IOException {
        final int code;
        GhError(int code, String msg) { super(msg); this.code = code; }
    }

    private byte[] ghGet(String repo, String token, String path) throws IOException { return ghRequest(repo, token, path, "application/vnd.github+json"); }
    private byte[] ghGetRaw(String repo, String token, String path) throws IOException { return ghRequest(repo, token, path, "application/vnd.github.raw"); }

    private byte[] ghRequest(String repo, String token, String path, String accept) throws IOException {
        StringBuilder url = new StringBuilder("https://api.github.com/repos/").append(repo).append("/contents");
        for (String seg : path.split("/")) if (!seg.isEmpty()) url.append('/').append(Uri.encode(seg));
        String ref = prefs.getString("ghRef", "");
        if (!ref.isEmpty()) url.append("?ref=").append(Uri.encode(ref));
        HttpURLConnection c = (HttpURLConnection) new URL(url.toString()).openConnection();
        try {
            c.setConnectTimeout(8000);
            c.setReadTimeout(20000);
            c.setUseCaches(false);
            c.setRequestProperty("Authorization", "Bearer " + token);
            c.setRequestProperty("Accept", accept);
            c.setRequestProperty("X-GitHub-Api-Version", "2022-11-28");
            c.setRequestProperty("User-Agent", "NovelViewer");
            int code = c.getResponseCode();
            if (code == 401) throw new GhError(401, "GitHub 토큰이 맞지 않습니다.");
            if (code == 403 || code == 404) throw new GhError(code, "저장소나 파일을 찾을 수 없습니다. 저장소 이름과 토큰 권한을 확인하세요.");
            if (code >= 400) throw new GhError(502, "GitHub 오류 " + code);
            return readAll(c.getInputStream());
        } finally {
            c.disconnect();
        }
    }

    private static String trimSlash(String s) { return s == null ? "" : s.replaceAll("^/+|/+$", ""); }

    // 글자 인코딩: UTF-8(BOM 포함), UTF-16, 그 외에는 CP949(메모장 ANSI)
    private static String decodeText(byte[] b) {
        if (b.length >= 3 && (b[0] & 0xff) == 0xef && (b[1] & 0xff) == 0xbb && (b[2] & 0xff) == 0xbf)
            return new String(b, 3, b.length - 3, StandardCharsets.UTF_8);
        if (b.length >= 2 && (b[0] & 0xff) == 0xff && (b[1] & 0xff) == 0xfe) return new String(b, 2, b.length - 2, StandardCharsets.UTF_16LE);
        if (b.length >= 2 && (b[0] & 0xff) == 0xfe && (b[1] & 0xff) == 0xff) return new String(b, 2, b.length - 2, StandardCharsets.UTF_16BE);
        try {
            return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(b)).toString();
        } catch (CharacterCodingException e) {
            return new String(b, Charset.forName("x-windows-949"));
        }
    }

    // 숫자는 크기로 비교하는 이름 정렬(1, 2, 10 순)
    private static int natural(String a, String b) {
        int i = 0, j = 0;
        while (i < a.length() && j < b.length()) {
            char x = a.charAt(i), y = b.charAt(j);
            if (Character.isDigit(x) && Character.isDigit(y)) {
                int si = i, sj = j;
                while (i < a.length() && Character.isDigit(a.charAt(i))) i++;
                while (j < b.length() && Character.isDigit(b.charAt(j))) j++;
                String na = a.substring(si, i).replaceFirst("^0+(?=.)", ""), nb = b.substring(sj, j).replaceFirst("^0+(?=.)", "");
                if (na.length() != nb.length()) return na.length() - nb.length();
                int c = na.compareTo(nb);
                if (c != 0) return c;
            } else {
                if (x != y) return Character.compare(x, y);
                i++;
                j++;
            }
        }
        return (a.length() - i) - (b.length() - j);
    }

    private WebResourceResponse asset(String name, String type) {
        try {
            return new WebResourceResponse(type, "utf-8", getAssets().open(name));
        } catch (IOException e) {
            return text(404, "Not found");
        }
    }

    private static WebResourceResponse text(int code, String msg) {
        return new WebResourceResponse("text/plain", "utf-8", code, "Error",
                Collections.singletonMap("Cache-Control", "no-store"),
                new ByteArrayInputStream(msg.getBytes(StandardCharsets.UTF_8)));
    }

    private static byte[] readAll(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[16384];
        for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
        in.close();
        return out.toByteArray();
    }

    // ---- 상태바·내비게이션 바(화면 위아래 막대) ----
    @SuppressWarnings("deprecation")
    private void setBars(boolean show) {
        barsShown = show;
        if (Build.VERSION.SDK_INT >= 30) {
            WindowInsetsController c = getWindow().getInsetsController();
            if (c == null) return;
            c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            if (show) c.show(WindowInsets.Type.systemBars());
            else c.hide(WindowInsets.Type.systemBars());
        } else {
            int f = View.SYSTEM_UI_FLAG_LAYOUT_STABLE | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION;
            if (!show) f |= View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY;
            getWindow().getDecorView().setSystemUiVisibility(f);
        }
    }

    // 다른 앱에 다녀오면 숨김 상태가 풀릴 수 있어서 다시 맞춘다.
    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) setBars(barsShown);
    }

    private String server() { return prefs.getString("server", ""); }

    private void openSetup(boolean failed) {
        setBars(true);
        web.loadUrl(SETUP + "?server=" + Uri.encode(server()) + (failed ? "&failed=1" : ""));
    }

    // ---- 폰에 저장된 txt 파일 고르기 ----
    @Override
    protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != PICK_FILE || result != RESULT_OK || data == null || data.getData() == null) return;
        Uri uri = data.getData();
        // 앱을 다시 켜도 최근 목록에서 바로 열 수 있게 읽기 권한을 계속 유지한다.
        try {
            getContentResolver().takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
        } catch (SecurityException ignored) {
        }
        String name = "소설.txt";
        try (Cursor cur = getContentResolver().query(uri, new String[]{OpenableColumns.DISPLAY_NAME}, null, null, null)) {
            if (cur != null && cur.moveToFirst() && cur.getString(0) != null) name = cur.getString(0);
        } catch (Exception ignored) {
        }
        web.evaluateJavascript("onPhoneFile(" + JSONObject.quote(uri.toString()) + "," + JSONObject.quote(name) + ")", null);
    }

    // 웹 화면(index.html, setup.html)에서 부르는 함수들.
    private class Bridge {
        @JavascriptInterface
        public String getServer() { return server(); }

        @JavascriptInterface
        public void connect(String address) {
            String a = address.trim().replaceAll("/+$", "");
            if (!a.isEmpty() && !a.contains(":")) a += ":8080";
            prefs.edit().putString("server", a).apply();
            runOnUiThread(() -> web.loadUrl(HOME));
        }

        // PC 없이 폰 파일만 볼 때
        @JavascriptInterface
        public void skipSetup() {
            if (!prefs.contains("server")) prefs.edit().putString("server", "").apply();
            runOnUiThread(() -> web.loadUrl(HOME));
        }

        @JavascriptInterface
        public void openSetup() { runOnUiThread(() -> MainActivity.this.openSetup(false)); }

        // GitHub 저장소 이름(예: 아이디/저장소). 토큰이 없으면 빈 문자열.
        @JavascriptInterface
        public String getGithub() {
            if (prefs.getString("ghToken", "").isEmpty()) return "";
            String root = prefs.getString("ghRoot", "");
            return prefs.getString("ghRepo", "") + (root.isEmpty() ? "" : "/" + root);
        }

        // 저장소 주소를 받는다: "아이디/저장소", https://github.com/아이디/저장소,
        // 폴더 주소 https://github.com/아이디/저장소/tree/브랜치/폴더 (그 폴더부터 보여준다). 토큰을 비우면 기존 토큰을 그대로 쓴다.
        @JavascriptInterface
        public void setGithub(String repo, String token) {
            String[] s = repo.trim().replaceFirst("^https?://github\\.com/", "").replaceAll("\\.git$|/+$", "").split("/");
            String r = s.length >= 2 ? s[0] + "/" + s[1] : s[0], ref = "", root = "";
            if (s.length >= 4 && s[2].equals("tree")) {
                ref = s[3];
                StringBuilder b = new StringBuilder();
                for (int i = 4; i < s.length; i++) b.append(b.length() > 0 ? "/" : "").append(Uri.decode(s[i]));
                root = b.toString();
            }
            SharedPreferences.Editor e = prefs.edit().putString("ghRepo", r).putString("ghRef", ref).putString("ghRoot", root);
            if (!token.trim().isEmpty()) e.putString("ghToken", token.trim());
            e.apply();
        }

        @JavascriptInterface
        public void clearGithub() { prefs.edit().remove("ghRepo").remove("ghToken").remove("ghRef").remove("ghRoot").apply(); }

        @JavascriptInterface
        public void pickFile() {
            Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            i.addCategory(Intent.CATEGORY_OPENABLE);
            i.setType("*/*");
            i.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{"text/*", "application/octet-stream"});
            runOnUiThread(() -> startActivityForResult(i, PICK_FILE));
        }

        // 폰 파일 내용을 base64 로 돌려준다(글자 인코딩은 화면 쪽에서 판단). 못 읽으면 null.
        @JavascriptInterface
        public String readFile(String uri) {
            try (InputStream in = getContentResolver().openInputStream(Uri.parse(uri))) {
                if (in == null) return null;
                return Base64.encodeToString(readAll(in), Base64.NO_WRAP);
            } catch (Exception e) {
                return null;
            }
        }

        // 책갈피·읽던 위치·설정은 앱에 저장한다. PC 주소(IP)가 바뀌어도 사라지지 않는다.
        @JavascriptInterface
        public String get(String key) { return prefs.getString("store." + key, null); }

        @JavascriptInterface
        public void set(String key, String value) { prefs.edit().putString("store." + key, value).apply(); }

        // 메뉴를 보일 때 화면 위아래 막대도 같이 보이고, 메뉴를 숨기면 같이 숨긴다.
        @JavascriptInterface
        public void setMenu(boolean show) { runOnUiThread(() -> setBars(show)); }

        // 상태바 높이(CSS px). 메뉴가 상태바에 가리지 않게, 글이 카메라 구멍에 걸리지 않게 띄운다.
        @JavascriptInterface
        public float statusBarHeight() {
            int id = getResources().getIdentifier("status_bar_height", "dimen", "android");
            float px = id > 0 ? getResources().getDimensionPixelSize(id) : 0;
            return px / getResources().getDisplayMetrics().density;
        }

        // 아래 내비게이션 막대 높이(CSS px). 페이지 이동 바가 막대에 가리지 않게 띄운다.
        @JavascriptInterface
        public float navBarHeight() {
            int id = getResources().getIdentifier("navigation_bar_height", "dimen", "android");
            float px = id > 0 ? getResources().getDimensionPixelSize(id) : 0;
            return px / getResources().getDisplayMetrics().density;
        }

        @JavascriptInterface
        public String appVersion() { return Updater.currentVersion(MainActivity.this); }

        @JavascriptInterface
        public void checkUpdate(boolean manual) { updater.check(manual); }

        @JavascriptInterface
        public boolean getVolumeKeys() { return volumeKeys; }

        @JavascriptInterface
        public void setVolumeKeys(boolean on) {
            volumeKeys = on;
            prefs.edit().putBoolean("volumeKeys", on).apply();
        }
    }

    // 볼륨 키로 페이지 넘기기: 아래(−) = 다음 페이지, 위(+) = 이전 페이지.
    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
        int code = e.getKeyCode();
        if (volumeKeys && (code == KeyEvent.KEYCODE_VOLUME_DOWN || code == KeyEvent.KEYCODE_VOLUME_UP)
                && web.getUrl() != null && web.getUrl().startsWith(HOME)) {
            if (e.getAction() == KeyEvent.ACTION_DOWN) {
                int dir = code == KeyEvent.KEYCODE_VOLUME_DOWN ? 1 : -1;
                web.evaluateJavascript("window.turnPage && turnPage(" + dir + ")", null);
            }
            return true;
        }
        return super.dispatchKeyEvent(e);
    }

    // 뒤로 가기: 열린 목록·팝업이 있으면 닫고, 없으면 앱을 끝낸다.
    @Override
    @SuppressWarnings("deprecation")
    public void onBackPressed() {
        if (web.getUrl() != null && web.getUrl().startsWith(SETUP) && prefs.contains("server")) {
            web.loadUrl(HOME);
            return;
        }
        web.evaluateJavascript("window.appBack ? appBack() : false", result -> {
            if (!"true".equals(result)) finish();
        });
    }
}
