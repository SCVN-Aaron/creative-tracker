# Creative Weekly Tracker — hướng dẫn

Dashboard theo tuần cho Video Ads Team, đọc số liệu từ Creative Production DB trên Notion. Các dòng thuộc đội khác trong database sẽ được bỏ qua.

## Cập nhật data thủ công (từ 08/10/2026)

Theo chính sách công ty, Claude không còn nối với Notion: routine "Update Tracking" và Creative Planner tạm tắt (`NOTION_AI=false` trong `index.html`). Data cũ trong `data/latest.csv` được giữ nguyên. Mỗi tuần cập nhật bằng một trong hai cách:

1. **File CSV:** trên Notion mở 🗓️ Creative Production DB (hoặc view 📍 Video Ads Team) → `•••` → Export → Markdown & CSV, rồi gửi file cho Claude để gộp vào `data/latest.csv` (task mới thêm vào, task cũ cập nhật, không xoá data cũ).
2. **Ảnh chụp màn hình:** chụp Table view thấy đủ các cột Period, Creator, PM, Video Count, Game Title, Iteration, Status, gửi cho Claude đọc và gộp vào. Kiểm tra lại bảng Claude đọc ra trước khi cập nhật.

Khi được phép nối lại: bật routine, đổi `NOTION_AI` thành `true`.

## Có gì trong này

| File | Việc của nó |
|---|---|
| `creative-weekly-tracker.html` | Dashboard. Mở bằng trình duyệt là chạy, không cần server. |
| `update_dashboard.py` | Đọc Notion, ghi đè khối dữ liệu trong file HTML. Chỉ dùng thư viện chuẩn Python 3.9+. |
| `config.json` | Bảng tra Period, chỉ tiêu, và các kỳ gộp được cộng thêm chỉ tiêu. |
| `ROUTINE.md` | Cách cập nhật tự động không cần token, dùng Claude Routine. |
| `.github/workflows/update-dashboard.yml` | Cách cập nhật tự động bằng GitHub Actions, cần token. |
| `data/notion-export.csv` | Bản chụp dữ liệu thô của lần cập nhật gần nhất. Dùng để đối chiếu khi số liệu trông sai. |
| `data/latest.csv` | Dữ liệu từng task mà dashboard đọc trực tiếp khi mở trang. Routine "Update Tracking" ghi đè file này hằng tuần. |
| `tools/creative-planner.html` | Mã nguồn Creative Planner — tool cho PM lên kế hoạch và ghi task vào Notion. Chạy trên claude.ai, không chạy trên GitHub Pages. |

Script chỉ thay phần giữa hai dòng `// <<< DATA_START` và `// <<< DATA_END >>>` trong file HTML. Mọi thứ khác — biểu đồ, cách tính, giao diện — bạn sửa thoải mái, chạy lại script không mất.

## Tab PM và Creative Planner

Dashboard có ba tab và một link:

- **Creator** — sản lượng từng creator so với chỉ tiêu, như trước giờ. Kỳ chưa bắt đầu (kế hoạch nhập trước) không được tính vào đây.
  Cuối tab Creator có bảng **Lịch 4-Week Loop Schedule (🇻🇳 UA)** lấy nguyên từ Google Sheet tới hết file: "Theo creator" (creator × tuần → game, ✓/⚠ so với Notion, dưới mỗi ô là PM của task trên Notion hoặc "Solo" nếu chưa gắn PM — không đoán PM) hoặc "Theo game" (game × tuần → creator, kèm số tuần).
  Nút **Tổng tháng** trên biểu đồ hiện tổng số creative cả đội mỗi tháng (kèm đường tổng chỉ tiêu) và bảng "Số creative sản xuất trong tháng" của từng creator. Tháng của một kỳ lấy theo tên Period trên Notion (ví dụ "Sep W1W2 (08/31~)" là tháng 9, "Oct W1 (09/28~)" là tháng 10), không theo ngày bắt đầu. Tuần đang làm chưa cộng vào số tháng, hết tuần mới cộng. Tháng hiện tại có thêm phần **dự kiến cả tháng theo KPI** (cột "… dự kiến" trong bảng và phần cột nhạt trên biểu đồ): chỉ là dự kiến, không dùng số thực tế và không trừ nghỉ phép — mỗi tuần của tháng × KPI từng người (8 video/tuần, Miko làm ETC 12, người mới 2 → 6 → 8). Số tuần của tháng đếm theo tuần có ngày thứ Năm nằm trong tháng.
- **PM** — tổng quan từng tuần: PM nào làm game gì, iteration nào, với creator nào, bao nhiêu creative, trạng thái ra sao. Có cả các tuần sắp tới. Bấm tên iteration để mở task trên Notion. Bên dưới là bảng "PM và các game đảm nhận" gồm hai cột: tên PM (A→Z) và các game PM đó làm; game đang đảm nhận (có task từ tuần này trở đi) có chấm xanh và đứng trước, game đã làm hiện mờ. Mở thẳng tab này bằng link có đuôi `#pm`.
- **4-Week Loop Schedule** — Google Sheet (chỉ các dòng 🇻🇳 베트남 — UA) được *Publish to web* dạng CSV, dashboard đọc trực tiếp link đó (hằng `SCHEDULE_URL` trong `index.html`), nên sửa Sheet là dashboard thấy ngay. Không đọc được link thì dùng bản lưu `data/schedule.csv`.
- **Lịch PM×Creator** — Google Sheet "Creative PM×Creator Schedule" (mỗi ô là `Game` + dòng `PM: Tên`; `No PM` / `TBD` = không PM), *Publish to web* dạng CSV, dashboard đọc trực tiếp (hằng `PMSCHED_SRC`), không đọc được thì dùng `data/pm_schedule.csv`. Dùng **cùng** 4-Week Loop: **game và (YT) theo Loop** (Loop được sửa thường xuyên hơn); PM×Creator cho **PM** khi cùng game với Loop, và lấp game cho ô Loop trống. Khác game thì bỏ PM của PM×Creator (PM lấy theo Notion, không có thì Solo). Tab PM có bảng **Lịch PM theo tuần** (giống lịch ở tab Creator): "Theo PM" (PM × tuần → game · creator, không PM thì ở hàng Creator solo) hoặc "Theo game" (game × tuần → PM và creator, kèm số tuần).
- **Đánh số** (tab mới, link `#number`) — tạo sẵn **Creative number** và **Video Count** cho task mới để copy vào Notion. Chọn tuần → "Điền theo lịch" lấy creator + game từ lịch (Loop / PM×Creator), số video theo chỉ tiêu (8, ETC 12, Hypereels 24, người mới 2 → 6 → 8). Mỗi game có một dãy số **dùng chung cho team UA và team AI** (UA điền trước, AI điền sau): số mới = số lớn nhất đã dùng của game đó trên cả hai team (`data/latest.csv` + `data/ai_numbers.csv`) + 1; các dòng cùng game nối tiếp nhau; Social/YT có dãy riêng. Bấm một số để copy, hoặc copy cả cột. Bảng "Số cuối hiện tại" sửa tay được (lưu trên trình duyệt) khi Notion có task mới hơn dashboard.
- **Số Creative từ Google Drive** — Drive là nguồn đúng nhất (tên file `video90582_…_cyan_051126.mp4`, thư mục `<game>/VN 제작/…`). Tab Đánh số lấy số cuối = lớn nhất giữa Notion (UA + AI) và Drive; nếu Drive lớn hơn Notion thì hiện cảnh báo màu cam "Drive lớn hơn Notion". Số Drive lưu ở `data/drive_numbers.csv` (Game, Type UA/Social, Max, …, Updated): khi có game mới thì nhờ Claude quét Drive và thêm dòng. Tên thư mục Drive khác tên Notion thì thêm vào `DRIVE_ALIAS` trong `index.html` (vd Arrow Flow → Arrow Fever).
- **Đã làm** (tab mới, link `#done`) — số video mỗi Creator đã làm theo file trên Google Drive: theo tuần (12 tuần gần nhất) / tháng / game, lọc Team UA / AI / tất cả. Mỗi số Creative tính 1 video dù có nhiều kích thước; chỉ tính video đã upload. Cập nhật: Claude quét Drive (các thư mục VN 제작 / VN제작 / VN영상 / UA Creative_VN / 1_영상 của từng game) ra danh sách file, rồi `python3 -I tools/drive_summary.py <drive_files.csv>` ghi `data/drive_videos.csv`, `data/drive_numbers.csv`, `data/drive_creator_month.csv`, `data/drive_creator_game.csv`; `python3 -I tools/drive_excel.py data <file.xlsx>` xuất Excel (Theo tháng / Theo tuần / Theo game / Danh sách video).
- **Tìm video Creative** (tab Đã làm, dưới bảng số lượng) — chọn game và/hoặc gõ số Creative: số đầy đủ (`90582`), khoảng (`90573~90580` hoặc `90573~80`), hoặc vài số cuối (`582`). Kết quả: creator, ngày, concept, thời lượng, nút ▶ 9x16 / 1x1 / 16x9 phát video ngay trên dashboard (khung xem trước của Google Drive — cần đăng nhập tài khoản có quyền xem thư mục game; có link "Mở trên Drive") và task Notion có số đó (team UA lẫn AI). Số đã giữ trên Notion mà chưa upload hiện "Chưa có trên Drive". Chỉ chọn game = 30 video mới nhất của game đó. Link file lấy từ cột `Links` trong `data/drive_videos.csv`.
- **Link Google Drive của các game** (tab Đã làm, dưới ô tìm video) — chọn game ở ô "Game" (giống ô Tìm video Creative) để hiện nút mở thư mục game và thư mục video (VN 제작 / VN영상 …) trên Drive, nút Copy link, số video, số Creative cuối, ngày upload gần nhất. Ô chọn chia 2 nhóm: game có video (33) và các thư mục khác trong Drive dùng chung (204 tổng); "Tất cả game có video" hiện cả bảng. Dữ liệu ở `data/drive_games.csv`, cập nhật mỗi lần quét Drive.
- **Gộp file Notion** — `python3 tools/notion_merge.py <Creative Production DB …_all.csv>`: ghi `data/latest.csv` (Video Ads, có Creative number) và `data/ai_numbers.csv` (số Creative của team AI), in ra task mới / đổi tên / trùng / đã xoá để kiểm tra.
- **Điều chỉnh chỉ tiêu cả đội** — kỳ nghỉ lễ / team building: thêm dòng `Creator = ALL`, `Team change = -2` (và lý do ở cột Note) vào Google Sheet Quota exceptions (xem mục "Nghỉ phép và ngoại lệ chỉ tiêu" bên dưới). Lý do hiện ở ô "Số lượng video tuần này" và khung "Vì sao…". Ví dụ tuần 19/10: team building nghỉ thứ 5, thứ 6 → giảm 2 video mỗi người.
- **Chỉ tiêu theo game** — tuần nào creator làm **Hypereels** thì chỉ tiêu tuần đó là **24 video** (thay cho 8; ETC 12). Xét task trên Notion trước, chưa có task thì theo lịch. Áp dụng cho chỉ tiêu tuần, cột "Chênh so chuẩn" và dự kiến tháng. Thêm game khác vào hằng `GAME_QUOTA` trong `index.html`. Nghỉ phép vẫn được ưu tiên hơn.
- **Tự cập nhật lịch** — trang tự đọc lại cả 2 Google Sheet mỗi 2 phút (và khi quay lại tab), link có tham số chống cache; chỉ vẽ lại khi Sheet thay đổi. Dưới lịch ở tab Creator và tab PM có dòng nguồn: "Google Sheet trực tiếp" hoặc "bản lưu trong repo" (khi không đọc được Sheet). Google cần tới ~5 phút để đẩy thay đổi ra link publish, và phải bật *Automatically republish when changes are made* trong Publish to web.
- **Tiêu đề phụ (YT) / (Social)** — nhận cả `(YT) Game` lẫn `(Social) Game` (nhãn đỏ YT, tím Social). Ô lịch (PM×Creator hoặc Loop) có thêm dòng `(YT) Game` (vd. `Snake Clash` + `(YT) XP Hero`) thì game chính giữ nguyên, game YT hiện kèm nhãn đỏ **YT** ở tab Creator và Lịch PM theo tuần. Task Notion có Iteration bắt đầu bằng `(YT)` cũng được gắn nhãn YT.
- **Lên kế hoạch ↗** — mở [Creative Planner](https://claude.ai/artifact/4KF9BgskfgqJvzsSCgeCsS), nơi PM chọn tuần, PM, game, số lượng, creator làm chung rồi xác nhận để tạo task (Status = To do) trong Creative Production DB.

Creative Planner phải chạy trên claude.ai vì nó ghi vào Notion bằng connector Notion của chính người đang mở, không cần integration token. GitHub Pages không có quyền đó nên dashboard chỉ link sang. Artifact đang ở chế độ riêng tư — muốn PM khác dùng thì chủ artifact chia sẻ qua menu Share, và mỗi người cần nối connector Notion trong claude.ai.

Tab PM cần `data/latest.csv` ở định dạng mới, có thêm các cột `Game Title`, `Iteration`, `Status`, `Page ID` (xem `ROUTINE.md`). File ở định dạng cũ thì tab Creator vẫn chạy bình thường, tab PM báo chưa có dữ liệu.

PM đã rời team (hằng `HIDDEN_PM` trong `index.html`, hiện là Avril) không hiện trong tab PM; số của creator ở tab Creator vẫn giữ nguyên.

Thành viên mới (kỳ đầu tiên có số nằm sau kỳ đầu của bảng): kỳ đầu tiên chỉ tiêu **2 video**, kỳ thứ hai **6**, từ kỳ thứ ba mới là 8 (hằng `RAMP` trong `index.html`). Tab Creator hiện nhãn "Mới · tuần 1/2" cạnh tên; chỉ tiêu đội, cột chênh so chuẩn và dự kiến tháng đều tính theo lộ trình này.

**Nghỉ phép và ngoại lệ chỉ tiêu — nhập ở Google Sheet** [Creative Tracker — Quota exceptions](https://docs.google.com/spreadsheets/d/1nAg1pIdvE0I-RwGsnOVPFEsQz4kxMhOg1DQKCngTEG8/edit) — Sheet **riêng tư, chỉ admin mở được** (không Publish to web).

**Form nhập trên dashboard (nút "Leave / Nghỉ phép" trên đầu trang):** nhập mật khẩu admin → thêm hoặc xoá ngoại lệ, dashboard cập nhật ngay. Form ghi vào Sheet qua Apps Script `tools/exceptions_api.gs` gắn vào Sheet: mật khẩu kiểm tra phía Google (lưu trong Script properties `ADMIN_PASSWORD`, không nằm trong dashboard), sai 8 lần trong 10 phút thì khoá tạm. Cài 1 lần theo hướng dẫn đầu file `.gs`, rồi dán link Web App (`…/exec`) vào hằng `EXC_API` trong `index.html`. Dashboard đọc ngoại lệ qua link đó (mỗi 2 phút); chưa có link hoặc không đọc được thì dùng bản lưu `data/exceptions.csv`. Đổi mật khẩu: sửa `ADMIN_PASSWORD` trong Script properties.

Mỗi dòng một ngoại lệ:

| Cột | Ý nghĩa |
|---|---|
| Week | Ngày bất kỳ trong tuần, nên gõ `2026-11-16` (cũng nhận `16/11/2026`, `16/11` hoặc tên Period trên Notion) |
| Creator | Tên creator; `ALL` = cả đội |
| Days off | Số ngày nghỉ → chỉ tiêu = chỉ tiêu thường − số ngày (8 → 7, ETC 12 → 11) |
| Target | Tuỳ chọn: ghi đè chỉ tiêu tuần đó của người đó |
| Dev/Art | Tuần đó có dev/art: số video có dev/art (vd 12, nghỉ 1 ngày thì 9) → làm đúng số đó tính là "có dev/art" |
| Team change | Chỉ dòng `ALL`: cộng/trừ mỗi người (vd `-2` khi team building), áp dụng cả ETC |
| Note / Note EN / Note KO | Lý do hiện trên dashboard theo ngôn ngữ (bỏ trống EN/KO thì dùng Note) |

Dòng nguồn dưới lịch ghi "Nghỉ phép / ngoại lệ: Google Sheet trực tiếp" (đọc qua Apps Script) hoặc "bản lưu". Tab Đánh số cũng dùng bảng này khi "Điền theo lịch" (nghỉ phép trừ số video, Dev/Art dùng số có dev/art, `ALL` cộng/trừ cả đội). Hằng `LEAVE` / `WEEK_ADJ` trong code vẫn dùng được nhưng để trống.

Các bảng rộng (lịch tab Creator, tab PM, tab Đã làm) kéo được bằng chuột: nhấn giữ trong bảng rồi kéo để xem, khỏi kéo thanh cuộn bên dưới.

Màu tag mỗi game lấy theo thứ tự option `Game Title` trên Notion (hằng `GAME_ORDER` trong `index.html`, khớp với Creative Planner). Thêm game mới trên Notion thì thêm tên vào cuối `GAME_ORDER` để màu hai nơi giống nhau.

Hai nút **☀ Sáng / ☾ Tối** cạnh ô ngôn ngữ đổi giao diện. Chưa bấm lần nào thì đi theo chế độ sáng tối của máy. Lựa chọn được nhớ trên trình duyệt đó. Màu nằm trong các biến `--...` ở đầu khối `<style>`: một bộ cho giao diện sáng, một bộ cho giao diện tối.

## Xem dashboard ở mọi nơi

Bật GitHub Pages: **Settings → Pages → Deploy from branch → main → / (root)**.

Sau vài phút dashboard nằm ở `https://<tên-bạn>.github.io/<tên-repo>/creative-weekly-tracker.html` — mở được trên điện thoại, máy tính, chia sẻ link cho cả đội, không cần cài gì. Repo private cần GitHub Pro mới bật được Pages; nếu không có thì để repo public và đừng đưa dữ liệu nhạy cảm vào (dashboard chỉ có tên thành viên và số video).

Dashboard hiển thị thời điểm cập nhật ngay dưới tiêu đề, nên người mở luôn biết số liệu mới tới đâu.

## Chọn cách cập nhật

| Cách | Cần admin Notion | Tự động | Ghi thẳng lên GitHub |
|---|---|---|---|
| A. Claude Routine | Không | Có | Có |
| B. Xuất CSV rồi chạy script | Không | Không | Thủ công |
| C. Integration token + GitHub Actions | Có | Có | Có |

**Cách A là lựa chọn mặc định** khi bạn không tạo được integration token. Routine dùng connector Notion đã nối trong tài khoản Claude cá nhân — loại OAuth theo người dùng, không phải integration của workspace — nên không ai phải duyệt. Nó chạy trên cloud của Anthropic kể cả khi máy bạn tắt, và commit vào repo dưới danh nghĩa GitHub của bạn.

Hướng dẫn đầy đủ kèm prompt để dán: **[ROUTINE.md](ROUTINE.md)**

**Cách B** dùng khi routine bị tổ chức tắt, hoặc bạn muốn chạy tay:

```bash
# Notion → mở Creative Production DB → menu "..." → Export → Markdown & CSV
python3 update_dashboard.py --csv "~/Downloads/Creative Production DB.csv"
```

Rồi commit file HTML lên GitHub như bình thường.

**Cách C** là phần bên dưới, chỉ làm được khi có token.

## Cách C — Tạo integration token

Nếu ô tạo connection báo *You don't have permission to create connections in this workspace*, bạn đang bị chặn ở cấp workspace — nhờ admin mở quyền hoặc tạo hộ, còn không thì dùng cách A hoặc B.

1. Vào https://www.notion.so/profile/integrations, bấm **New integration**.
2. Đặt tên (ví dụ `Creative Tracker`), chọn workspace Supercent, loại **Internal**.
3. Ở phần Capabilities, bật **Read content** và **Read user information**. Quyền đọc user là bắt buộc — thiếu nó thì cột Creator trả về rỗng và mọi dòng bị bỏ qua.
4. Copy token (dạng `ntn_...`). Token này đọc được mọi trang bạn chia sẻ với nó, nên đừng commit lên repo.

### Chia sẻ database cho integration

Mở trang **🗓️ Creative Production DB** trên Notion → menu `...` góc phải → **Connections** → chọn integration vừa tạo.

Không làm bước này thì script báo HTTP 404 dù token đúng.

### Chạy thử tại máy

```bash
export NOTION_TOKEN=ntn_xxx
python3 update_dashboard.py --check    # xem có gì thay đổi, chưa ghi file
python3 update_dashboard.py            # ghi thật
```

Kết quả mong đợi:

```
Đang đọc Notion...
  Đọc qua data_sources (Notion-Version 2025-09-03)
  312 dòng

Tổng hợp:
  Video Ads Team: 9 người, 1080 video, 15 kỳ
  Kỳ mới nhất: Tuần 14/09 ([W2] Sep W3 (09/14~))

Đã cập nhật creative-weekly-tracker.html.
```

### Bật chạy tự động

1. Đẩy thư mục này lên một repo GitHub **private**.
2. Vào **Settings → Secrets and variables → Actions → New repository secret**, tên `NOTION_TOKEN`, dán token vào.
3. Tab **Actions** → chọn workflow → **Run workflow** để chạy thử ngay.

Lịch mặc định là 09:00 giờ Việt Nam thứ Hai hàng tuần. Đổi giờ ở dòng `cron` trong file workflow (giờ trong đó là UTC, cộng 7 ra giờ Việt Nam).

Muốn xem dashboard qua link thay vì tải file: bật **Settings → Pages → Deploy from branch → main**, rồi truy cập `https://<tên-bạn>.github.io/<tên-repo>/creative-weekly-tracker.html`. Repo private cần GitHub Pro mới bật được Pages.

## Bảo trì

**Mỗi khi đội tạo Period mới trong Notion, phải thêm vào `config.json`.** Đây là việc thủ công duy nhất còn lại. Script cố tình dừng hẳn khi gặp Period lạ thay vì bỏ qua — nếu để nó tự đoán, một tuần dữ liệu sẽ biến mất mà không ai biết.

```json
{ "notion": "[W4] Sep W5 (09/28~)", "label": "Tuần 28/09", "month": "Tháng 9" }
```

Với kỳ gộp nhiều tuần do nghỉ lễ, thêm `bonus` — chỉ tiêu và mốc dev/art đều tự dịch lên theo:

```json
{
  "notion": "[W1] Tet W1W2 (02/15~)",
  "label": "Tuần 15/02",
  "month": "Tháng 2",
  "bonus": 3,
  "note": "Kỳ gộp W1/W2 vì nghỉ Tết — chỉ tiêu cộng thêm 3 video mỗi người"
}
```

## Hai chỗ dữ liệu dễ sai

**`Video Count` đang là kiểu Text.** Script bóc số đầu tiên tìm được và in cảnh báo cho từng ô bất thường:

```
! [W2] August W2 (08/10~) / Miko → Video Count ghi "12 (2 var)", đọc thành 12
```

Đổi cột này sang **Number** trong Notion là hết cảnh báo, và không còn rủi ro đọc nhầm.

**Một task có nhiều Creator.** Số video được chia đều rồi làm tròn, nên tổng của từng người có thể lệch tổng thật 1–2 video. Trong dữ liệu hiện tại chỉ có một dòng như vậy (Aaron và Bomi, tuần 20/04).

## Đổi cách tính

Các quy tắc nằm ở `config.json`, không nằm trong script:

- `quota` — số video chuẩn mỗi người mỗi tuần (8).
- `support` — ghi đúng con số này nghĩa là có dev/art tham gia (12). Trên số này là creator tự làm vượt mức.
- `etc` — người làm ETC, xếp nhóm riêng nên không bị đọc nhầm thành có dev/art.

## Ngôn ngữ

Ô chọn ngôn ngữ nằm cạnh ô chọn tuần, có Tiếng Việt, English và 한국어. Toàn bộ chữ trên dashboard đổi theo, kể cả nhãn trục, tooltip và phần diễn giải. Dashboard luôn mở bằng **tiếng Anh** (`let LANG="en"` trong `index.html`); trang giới thiệu `creative-tracker-intro.html` cũng mở bằng tiếng Anh, thêm `?lang=vi` hoặc `?lang=ko` để mở sẵn ngôn ngữ khác.

Chuỗi dịch nằm trong `const I18N` ngay dưới khối dữ liệu trong file HTML. Thêm ngôn ngữ mới bằng cách chép nguyên một khối, dịch từng dòng, rồi thêm một `<option>` vào ô `#lang` ở phần đầu trang.

Tên tuần và tên tháng trong dữ liệu vẫn sinh ra bằng tiếng Việt (`Tuần 01/06`, `Tháng 6`), dashboard tự chuyển sang `Week 01/06` / `01/06 주` và `June` / `6월` khi đổi ngôn ngữ. Không cần sửa `config.json`.

Tên đội (`Video Ads Team`) và tên Period gốc của Notion giữ nguyên ở mọi ngôn ngữ, để đối chiếu với Notion không bị lệch.
