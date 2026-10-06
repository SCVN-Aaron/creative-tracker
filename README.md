# Creative Weekly Tracker — hướng dẫn

Dashboard theo tuần cho Video Ads Team, đọc số liệu từ Creative Production DB trên Notion. Các dòng thuộc đội khác trong database sẽ được bỏ qua.

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
  Cuối tab Creator có bảng **Lịch 4-Week Loop Schedule (🇻🇳 UA)** lấy nguyên từ Google Sheet tới hết file: "Theo creator" (creator × tuần → game, ✓/⚠ so với Notion) hoặc "Theo game" (game × tuần → creator, kèm số tuần).
- **PM** — tổng quan từng tuần: PM nào làm game gì, iteration nào, với creator nào, bao nhiêu creative, trạng thái ra sao. Có cả các tuần sắp tới. Bấm tên iteration để mở task trên Notion. Bên dưới là bảng "PM và các game đảm nhận": mỗi PM một dòng, chia game **đang đảm nhận** (có task tuần này hoặc sắp tới) và **đã làm** (chỉ còn ở các tuần trước), kèm số tuần, số creative và creator. Mở thẳng tab này bằng link có đuôi `#pm`.
- **Overview** — một bảng "PM đang làm game gì — theo tuần": mỗi hàng là một PM, mỗi cột là một tuần (từ lúc đội bắt đầu điền PM tới **hết file 4-Week Loop Schedule**), mỗi ô liệt kê game · creator · số lượng. Bấm một dòng để mở task trên Notion. Creator nào có trong lịch mà tuần đó chưa có task trên Notion thì hiện mờ, in nghiêng, kèm PM dự đoán (lần gần nhất creator/game đó làm). Bảng tự cuộn tới tuần này. Mở thẳng bằng link có đuôi `#overview`.
- **4-Week Loop Schedule** — Google Sheet (chỉ các dòng 🇻🇳 베트남 — UA) được *Publish to web* dạng CSV, dashboard đọc trực tiếp link đó (hằng `SCHEDULE_URL` trong `index.html`), nên sửa Sheet là dashboard thấy ngay. Không đọc được link thì dùng bản lưu `data/schedule.csv`.
- **Lên kế hoạch ↗** — mở [Creative Planner](https://claude.ai/artifact/4KF9BgskfgqJvzsSCgeCsS), nơi PM chọn tuần, PM, game, số lượng, creator làm chung rồi xác nhận để tạo task (Status = To do) trong Creative Production DB.

Creative Planner phải chạy trên claude.ai vì nó ghi vào Notion bằng connector Notion của chính người đang mở, không cần integration token. GitHub Pages không có quyền đó nên dashboard chỉ link sang. Artifact đang ở chế độ riêng tư — muốn PM khác dùng thì chủ artifact chia sẻ qua menu Share, và mỗi người cần nối connector Notion trong claude.ai.

Tab PM cần `data/latest.csv` ở định dạng mới, có thêm các cột `Game Title`, `Iteration`, `Status`, `Page ID` (xem `ROUTINE.md`). File ở định dạng cũ thì tab Creator vẫn chạy bình thường, tab PM báo chưa có dữ liệu.

PM đã rời team (hằng `HIDDEN_PM` trong `index.html`, hiện là Avril) không hiện trong tab PM và Overview; số của creator ở tab Creator vẫn giữ nguyên.

Creator nghỉ phép vài ngày: thêm một dòng vào hằng `LEAVE` trong `index.html`, khoá là tên Period gốc trên Notion, ví dụ `"[W1] Oct W2 (10/5~)":{"Tilda":{quota:6, note:{vi:"Nghỉ thứ 5, thứ 6",en:"Off Thu–Fri",ko:"목·금 휴무"}}}`. Tuần đó chỉ tiêu của người đó tính theo `quota` và tab Creator hiện ghi chú cạnh tên.

Màu tag mỗi game lấy theo thứ tự option `Game Title` trên Notion (hằng `GAME_ORDER` trong `index.html`, khớp với Creative Planner). Thêm game mới trên Notion thì thêm tên vào cuối `GAME_ORDER` để màu hai nơi giống nhau.

Ô **Giao diện** cạnh ô ngôn ngữ chọn Tự động / Sáng / Tối. Tự động đi theo chế độ sáng tối của máy. Lựa chọn được nhớ trên trình duyệt đó. Màu nằm trong các biến `--...` ở đầu khối `<style>`: một bộ cho giao diện sáng, một bộ cho giao diện tối.

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

Ô chọn ngôn ngữ nằm cạnh ô chọn tuần, có Tiếng Việt, English và 한국어. Toàn bộ chữ trên dashboard đổi theo, kể cả nhãn trục, tooltip và phần diễn giải.

Chuỗi dịch nằm trong `const I18N` ngay dưới khối dữ liệu trong file HTML. Thêm ngôn ngữ mới bằng cách chép nguyên một khối, dịch từng dòng, rồi thêm một `<option>` vào ô `#lang` ở phần đầu trang.

Tên tuần và tên tháng trong dữ liệu vẫn sinh ra bằng tiếng Việt (`Tuần 01/06`, `Tháng 6`), dashboard tự chuyển sang `Week 01/06` / `01/06 주` và `June` / `6월` khi đổi ngôn ngữ. Không cần sửa `config.json`.

Tên đội (`Video Ads Team`) và tên Period gốc của Notion giữ nguyên ở mọi ngôn ngữ, để đối chiếu với Notion không bị lệch.
