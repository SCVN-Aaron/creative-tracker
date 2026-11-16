# Việc đầu tuần (sáng thứ 2) — Creative Tracker

Lịch tự động "Quét Drive thứ 2 hằng tuần" chạy 7h43 sáng thứ 2 (giờ Việt Nam) và làm theo file này,
để 8h30 anh Aaron bắt đầu đánh số với số mới nhất. Anh đã cho phép tự tạo PR và merge phần dữ liệu.

## Làm tự động

1. **Quét Google Drive** (mọi game, thư mục video "VN 제작" / "VN영상" / …) theo `tools/drive_scan_brief.md`
   bằng một agent nền; rồi `python3 -I tools/drive_summary.py <scratchpad>/drive_files.csv data`.
   - Dừng, không merge, nếu không vào được Drive hoặc số video giảm bất thường (mất cả một game).
2. **Kiểm tra số Creative** của tuần trước và tuần này (UA trong `data/latest.csv` + AI trong `data/ai_numbers.csv`)
   so với số cuối trên Drive:
   - dãy số trùng nhau trong cùng game (ví dụ Dragon Burst 5th ghi lại 90029~90036 của 4th);
   - chỗ hở số (ví dụ Downhill thiếu 90053);
   - task chưa có số hoặc chưa có số video;
   - file trên Drive đặt sai tên creator / trùng số / tên `_del`.
3. **Kiểm tra dashboard** chạy không lỗi (Creators, PMs, Performance — gồm các tab con Week summary, Numbering, Produced).
4. **Commit + push** lên branch làm việc, rồi **PR riêng từ main chỉ chứa `data/`** (không đưa `crebo/` lên main), merge.
5. **Báo anh** bằng tiếng Việt, ngắn gọn, theo thứ tự:
   1. Link PR đã merge; tổng video Drive, video mới theo game.
   2. **Tổng hợp tuần trước** (4 game test: XP Hero, Bulldozer Master, Pizza Ready, Snake Clash; gộp UA + AI),
      đúng mẫu để dán: `Bulldozer Master / 18th set of 22 items (90300~90321)` (+ dòng Viral Marketing nếu có).
   3. **Số tiếp theo** của 4 game test (số cuối trên Drive/Notion + 1) để đánh số tuần này.
   4. Các lỗi số ở bước 2 cần sửa trên Notion / Drive.
   5. Người nghỉ / ngoại lệ chỉ tiêu tuần này (Sheet ngoại lệ), nếu có.
   6. **Việc cần anh gửi:** ảnh Notion UA tuần này + chốt tuần trước, ảnh Notion team AI tuần trước + tuần này,
      và file export Crebo mới (nếu lịch đồng bộ Crebo vẫn chưa chạy được).

## Không tự động được (cần anh)

- **Notion** (UA và AI): theo chính sách không nối tài khoản AI → anh gửi ảnh chụp hoặc file export, em cập nhật
  `data/latest.csv` / `data/ai_numbers.csv`, kiểm tra số rồi tạo PR.
- **Crebo (CVR)**: lịch "Crebo VN → Google Sheet sync" cần Chrome đã đăng nhập Crebo trên máy anh
  (bật "Require this computer" + extension Claude in Chrome). Khi chưa chạy được, anh export Crebo gửi em →
  `python3 -I tools/crebo_merge.py <file export> data`.
