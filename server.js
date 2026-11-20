// 소설 뷰어 서버 — PC의 txt 파일을 폰 앱(또는 브라우저)에 보여준다.
// 사용법: node server.js [포트]
// 보기 전용이다. folders.txt 에 적힌 폴더 안의 txt 파일만 내주고, 파일을 고치거나 지우는 기능은 없다.
const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');

const PORT = Number(process.argv[2] || process.env.PORT || 8080);
const TEXT_EXT = new Set(['.txt', '.md', '.text']);
const FOLDERS_FILE = path.join(__dirname, 'folders.txt');
const DEFAULT_FOLDER = path.join(__dirname, 'novels');

if (!fs.existsSync(DEFAULT_FOLDER)) fs.mkdirSync(DEFAULT_FOLDER, { recursive: true });

const isText = (p) => TEXT_EXT.has(path.extname(p).toLowerCase());

// 공유할 폴더 목록. 폴더추가.bat 으로 추가하거나 folders.txt 를 직접 고친다(한 줄에 하나).
function folders() {
  let lines = [];
  try { lines = fs.readFileSync(FOLDERS_FILE, 'utf8').replace(/^﻿/, '').split(/\r?\n/); } catch {}
  const list = lines.map((l) => l.trim()).filter((l) => l && !l.startsWith('#')).map((l) => path.resolve(l));
  if (!list.length) list.push(DEFAULT_FOLDER);
  return list.filter((d) => { try { return fs.statSync(d).isDirectory(); } catch { return false; } });
}

// 공유 폴더 안쪽 경로만 허용한다 (../ 로 빠져나가기 막기).
function allowed(p) {
  const full = path.resolve(p);
  const low = full.toLowerCase();
  return folders().some((d) => { const r = d.toLowerCase(); return low === r || low.startsWith(r.endsWith(path.sep) ? r : r + path.sep); })
    ? full : null;
}

// 폴더 안의 하위 폴더와 txt 파일 목록
function browse(dir) {
  const entries = [];
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    if (/^[.$]/.test(ent.name)) continue;
    const full = path.join(dir, ent.name);
    try {
      if (ent.isDirectory()) entries.push({ name: ent.name, path: full, type: 'dir' });
      else if (isText(ent.name)) {
        const st = fs.statSync(full);
        entries.push({ name: ent.name, path: full, type: 'file', size: st.size, mtime: st.mtimeMs });
      }
    } catch {}
  }
  entries.sort((a, b) => (a.type === b.type ? a.name.localeCompare(b.name, 'ko', { numeric: true }) : a.type === 'dir' ? -1 : 1));
  // 공유 폴더 맨 위에서는 "상위 폴더"가 공유 폴더 목록이다.
  const isRoot = folders().some((d) => d.toLowerCase() === dir.toLowerCase());
  return { dir, parent: isRoot ? '' : path.dirname(dir), entries };
}

// UTF-8(BOM 포함), UTF-16, 그 외에는 한국어 윈도우 기본인 CP949(EUC-KR)로 읽는다.
function decode(buf) {
  if (buf[0] === 0xef && buf[1] === 0xbb && buf[2] === 0xbf) return buf.subarray(3).toString('utf8');
  if (buf[0] === 0xff && buf[1] === 0xfe) return new TextDecoder('utf-16le').decode(buf.subarray(2));
  if (buf[0] === 0xfe && buf[1] === 0xff) return new TextDecoder('utf-16be').decode(buf.subarray(2));
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(buf);
  } catch {
    return new TextDecoder('euc-kr').decode(buf);
  }
}

// ---- HTTP ----
function send(res, code, body, type = 'text/plain; charset=utf-8') {
  res.writeHead(code, { 'Content-Type': type, 'Cache-Control': 'no-store' });
  res.end(body);
}
const sendJson = (res, obj) => send(res, 200, JSON.stringify(obj), 'application/json; charset=utf-8');

const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://x');
  const q = (k) => url.searchParams.get(k) || '';

  if (url.pathname === '/' || url.pathname === '/index.html') {
    return send(res, 200, fs.readFileSync(path.join(__dirname, 'index.html')), 'text/html; charset=utf-8');
  }

  // 폰에서 앱 설치 파일을 받는 주소: http://PC주소:8080/app
  if (url.pathname === '/app' || url.pathname === '/app.apk') {
    const apk = path.join(__dirname, '소설뷰어.apk');
    if (!fs.existsSync(apk)) return send(res, 404, '앱 파일이 없습니다. android\\build.ps1 로 먼저 빌드하세요.');
    res.writeHead(200, {
      'Content-Type': 'application/vnd.android.package-archive',
      'Content-Disposition': "attachment; filename=\"NovelViewer.apk\"; filename*=UTF-8''" + encodeURIComponent('소설뷰어.apk'),
      'Cache-Control': 'no-store',
    });
    return fs.createReadStream(apk).pipe(res);
  }

  // 폴더 둘러보기: dir 이 비어 있으면 공유 폴더 목록
  if (url.pathname === '/api/browse') {
    if (!q('dir')) {
      return sendJson(res, { dir: '', parent: null, entries: folders().map((d) => ({ name: d, path: d, type: 'dir' })) });
    }
    const dir = allowed(q('dir'));
    try {
      if (!dir) throw 0;
      return sendJson(res, browse(dir));
    } catch {
      return send(res, 404, '폴더를 열 수 없습니다.');
    }
  }

  // txt 파일 읽기
  if (url.pathname === '/api/file') {
    const full = allowed(q('path'));
    try {
      if (!full || !isText(full)) throw 0;
      const st = fs.statSync(full);
      return sendJson(res, { path: full, name: path.basename(full), text: decode(fs.readFileSync(full)), mtime: st.mtimeMs });
    } catch {
      return send(res, 404, '파일을 찾을 수 없습니다.');
    }
  }

  // 저장 시각만 알려준다. 앱이 몇 초마다 물어보고, 바뀌었으면 다시 읽는다(자동 새로고침).
  if (url.pathname === '/api/stat') {
    const full = allowed(q('path'));
    try {
      if (!full || !isText(full)) throw 0;
      return sendJson(res, { mtime: fs.statSync(full).mtimeMs });
    } catch {
      return send(res, 404, '파일을 찾을 수 없습니다.');
    }
  }

  send(res, 404, 'Not found');
});

server.listen(PORT, '0.0.0.0', () => {
  console.log('\n공유 폴더 (폴더추가.bat 으로 추가):');
  for (const d of folders()) console.log('  ' + d);
  console.log(`\n이 PC에서:   http://localhost:${PORT}`);
  for (const [name, addrs] of Object.entries(os.networkInterfaces())) {
    // WSL·Hyper-V·VirtualBox 같은 가상 네트워크는 폰에서 접속할 수 없으므로 뺀다.
    if (/vEthernet|WSL|Hyper-V|VirtualBox|VMware|Loopback/i.test(name)) continue;
    for (const a of addrs || []) {
      if (a.family === 'IPv4' && !a.internal) {
        console.log(`같은 와이파이: ${a.address}:${PORT}   (앱에 이 주소를 입력)`);
        console.log(`  앱 설치 파일: http://${a.address}:${PORT}/app   (폰 크롬에서 한 번만 열기)`);
      }
    }
  }
  console.log('\n이 창이 열려 있는 동안 같은 와이파이의 기기에서 공유 폴더의 txt 파일을 읽을 수 있습니다.');
  console.log('끄려면 이 창을 닫거나 Ctrl+C 를 누르세요.\n');
});
