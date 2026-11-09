/**
 * Creative Tracker — API cho form "Nghỉ phép / ngoại lệ chỉ tiêu" trên dashboard.
 *
 * Gắn vào Google Sheet "Creative Tracker — Quota exceptions" (Sheet vẫn riêng tư, không cần Publish to web).
 *  - Dashboard ĐỌC danh sách ngoại lệ qua link Web App (GET) để tính chỉ tiêu — giống nội dung hiện trên dashboard.
 *  - Dashboard GHI (thêm / xoá dòng) qua link Web App (POST) — chỉ khi đúng mật khẩu admin.
 *    Mật khẩu được kiểm tra ở đây, trên máy chủ Google, và chỉ lưu trong Script properties (không nằm trong dashboard).
 *
 * Cài 1 lần:
 *  1. Mở Sheet → Extensions → Apps Script → xoá code mẫu, dán toàn bộ file này → Save.
 *  2. Project Settings (biểu tượng bánh răng) → Script properties → Add script property:
 *       Property = ADMIN_PASSWORD   Value = mật khẩu admin anh chọn → Save.
 *  3. Deploy → New deployment → Select type: Web app
 *       Execute as: Me      Who has access: Anyone      → Deploy → cho phép quyền (lần đầu).
 *  4. Copy "Web app URL" (…/exec) gửi cho Claude để gắn vào dashboard (hằng EXC_API trong index.html).
 *  Đổi mật khẩu: sửa ADMIN_PASSWORD trong Script properties, không cần Deploy lại.
 *  Sửa code sau này: Deploy → Manage deployments → Edit → Version: New version → Deploy (giữ nguyên URL).
 */
const HEAD = ['Week', 'Creator', 'Days off', 'Target', 'Dev/Art', 'Team change', 'Note', 'Note EN', 'Note KO'];
const MAX_FAILS = 8;          // sai mật khẩu quá số lần này trong 10 phút thì khoá tạm
const FAIL_WINDOW = 600;      // giây

function sheet_() {
  const sh = SpreadsheetApp.getActiveSpreadsheet().getSheets()[0];
  if (sh.getLastRow() === 0) sh.appendRow(HEAD);
  return sh;
}

function rows_() {
  const sh = sheet_(), n = sh.getLastRow();
  if (n < 2) return [];
  const tz = Session.getScriptTimeZone();
  return sh.getRange(2, 1, n - 1, HEAD.length).getValues().map((r, i) => {
    const o = { row: i + 2 };
    HEAD.forEach((h, j) => {
      let v = r[j];
      if (v instanceof Date) v = Utilities.formatDate(v, tz, 'yyyy-MM-dd');
      o[h] = v === null || v === undefined ? '' : String(v);
    });
    return o;
  }).filter(o => o.Week || o.Creator);
}

function csv_(rows) {
  const q = v => /[",\n]/.test(v) ? '"' + v.replace(/"/g, '""') + '"' : v;
  return [HEAD.map(q).join(',')].concat(rows.map(r => HEAD.map(h => q(r[h])).join(','))).join('\n') + '\n';
}

function json_(o) {
  return ContentService.createTextOutput(JSON.stringify(o)).setMimeType(ContentService.MimeType.JSON);
}

// GET: ?format=csv → CSV (dashboard đọc); mặc định JSON
function doGet(e) {
  const rows = rows_();
  if (e && e.parameter && e.parameter.format === 'csv')
    return ContentService.createTextOutput(csv_(rows)).setMimeType(ContentService.MimeType.CSV);
  return json_({ ok: true, rows: rows });
}

// POST (body JSON, Content-Type text/plain): {password, action:'check'|'add'|'delete', row:{...}, target:{row, Week, Creator}}
function doPost(e) {
  const cache = CacheService.getScriptCache();
  const fails = Number(cache.get('fails') || 0);
  if (fails >= MAX_FAILS) return json_({ ok: false, error: 'locked' });
  let body = {};
  try { body = JSON.parse(e.postData.contents || '{}'); } catch (err) { return json_({ ok: false, error: 'bad_request' }); }
  const pass = PropertiesService.getScriptProperties().getProperty('ADMIN_PASSWORD');
  if (!pass) return json_({ ok: false, error: 'no_password_set' });
  if (String(body.password || '') !== pass) {
    cache.put('fails', String(fails + 1), FAIL_WINDOW);
    Utilities.sleep(800);
    return json_({ ok: false, error: 'wrong_password' });
  }
  const lock = LockService.getScriptLock();
  lock.waitLock(10000);
  try {
    const sh = sheet_();
    if (body.action === 'add') {
      const r = body.row || {};
      const vals = HEAD.map(h => String(r[h] === undefined || r[h] === null ? '' : r[h]).slice(0, 300));
      if (!/^\d{4}-\d{2}-\d{2}$/.test(vals[0]) || !vals[1]) return json_({ ok: false, error: 'bad_row' });
      sh.appendRow(vals.map((v, j) => j === 0 ? v : (v && /^[-+=@]/.test(v) && isNaN(Number(v)) ? "'" + v : v)));
    } else if (body.action === 'delete') {
      const t = body.target || {}, cur = rows_().find(o => o.row === Number(t.row));
      // chỉ xoá khi dòng đó vẫn đúng là dòng dashboard đang thấy (tránh xoá nhầm khi Sheet vừa thay đổi)
      if (!cur || cur.Week !== t.Week || cur.Creator !== t.Creator) return json_({ ok: false, error: 'changed' });
      sh.deleteRow(cur.row);
    } else if (body.action !== 'check') {
      return json_({ ok: false, error: 'bad_action' });
    }
    return json_({ ok: true, rows: rows_() });
  } finally {
    lock.releaseLock();
  }
}
