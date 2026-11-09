/**
 * Quét Google Drive lấy số Creative lớn nhất của từng game → ghi vào sheet "Drive Numbers".
 * Dashboard (tab Đánh số) đọc sheet này (Publish to web dạng CSV) để biết số cuối đã có video trên Drive.
 *
 * Cách cài (chỉ làm 1 lần, chạy bằng tài khoản Google của anh — không cần link AI vào Drive):
 *   1. Tạo 1 Google Sheet mới (vd "Creative numbers – Drive").
 *   2. Extensions → Apps Script → xoá code mẫu, dán toàn bộ file này → Save.
 *   3. Chọn hàm installTrigger → Run → cho phép quyền Drive + Sheets (lần đầu).
 *      Hàm này chạy updateDriveNumbers ngay 1 lần và đặt lịch tự chạy mỗi giờ.
 *   4. File → Share → Publish to web → chọn sheet "Drive Numbers", định dạng CSV → Publish → copy link
 *      gửi cho em để gắn vào dashboard (DRIVE_SRC trong index.html).
 *
 * Quy ước tên file: video90582_linkedcubic_en_24s_1080x1920_cyan_051126.mp4 → số Creative = 90582.
 * Thư mục: <ROOT>/<Tên game>/VN 제작/{1.16x9, 2.9x16, 3.1x1, Social Media, ...}
 * Video nằm trong thư mục có chữ "Social" được tính vào dãy Social riêng. Thư mục "TEST" bị bỏ qua.
 */
const ROOT_IDS = ['1HD_G0IuI2ZySmDJ5YgbfsqqblnxKAvyi']; // thư mục chứa các game (thêm id khác nếu có)
const SUB_NAME = 'VN 제작';                             // để '' nếu muốn quét toàn bộ thư mục game
const SHEET_NAME = 'Drive Numbers';
const NUM_RE = /^video(\d{5,})/i;
const SKIP_RE = /^(test|please delete)$/i;

function updateDriveNumbers() {
  const acc = {};
  ROOT_IDS.forEach(id => {
    const games = DriveApp.getFolderById(id).getFolders();
    while (games.hasNext()) {
      const g = games.next(), name = g.getName();
      if (SUB_NAME) {
        const subs = g.getFoldersByName(SUB_NAME);
        while (subs.hasNext()) scan_(subs.next(), name, false, acc);
      } else scan_(g, name, false, acc);
    }
  });
  const now = Utilities.formatDate(new Date(), 'Asia/Ho_Chi_Minh', 'yyyy-MM-dd HH:mm');
  const rows = Object.values(acc)
    .sort((a, b) => a.game.localeCompare(b.game) || a.type.localeCompare(b.type))
    .map(r => [r.game, r.type, r.max, r.files, r.file, now]);
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const sh = ss.getSheetByName(SHEET_NAME) || ss.insertSheet(SHEET_NAME);
  sh.clearContents();
  sh.getRange(1, 1, 1, 6).setValues([['Game', 'Type', 'Max', 'Files', 'Latest file', 'Updated']]);
  if (rows.length) sh.getRange(2, 1, rows.length, 6).setValues(rows);
}

function scan_(folder, game, social, acc) {
  const files = folder.getFiles();
  while (files.hasNext()) {
    const f = files.next(), m = NUM_RE.exec(f.getName());
    if (!m) continue;
    const n = Number(m[1]), type = social ? 'Social' : 'UA', k = game + '|' + type;
    const r = acc[k] || (acc[k] = { game, type, max: 0, files: 0, file: '' });
    r.files++;
    if (n > r.max) { r.max = n; r.file = f.getName(); }
  }
  const subs = folder.getFolders();
  while (subs.hasNext()) {
    const s = subs.next(), nm = s.getName().trim();
    if (SKIP_RE.test(nm)) continue;
    scan_(s, game, social || /social/i.test(nm), acc);
  }
}

function installTrigger() {
  ScriptApp.getProjectTriggers()
    .filter(t => t.getHandlerFunction() === 'updateDriveNumbers')
    .forEach(t => ScriptApp.deleteTrigger(t));
  ScriptApp.newTrigger('updateDriveNumbers').timeBased().everyHours(1).create();
  updateDriveNumbers();
}
