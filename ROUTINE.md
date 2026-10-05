# Cập nhật tự động bằng Claude Routine

Cách này không cần integration token của Notion, nên không cần admin workspace duyệt gì cả. Routine dùng connector Notion đã nối sẵn trong tài khoản Claude của bạn, chạy trên cloud của Anthropic, và commit vào repo dưới danh nghĩa tài khoản GitHub của bạn.

## Cần có trước

- Tài khoản Claude trả phí (Pro, Max, Team hoặc Enterprise)
- Connector Notion đã nối tại `claude.ai/customize/connectors`, và trang **🗓️ Creative Production DB** đã được chia sẻ cho connector đó
- Repo GitHub chứa thư mục này, và Claude GitHub App đã cài trên repo

## Tạo routine

Vào `claude.ai/code/routines` → **New routine**.

**Repositories:** chọn repo chứa dashboard.

**Connectors:** chỉ giữ lại Notion. Bỏ hết những connector khác — trong lúc chạy, routine dùng được mọi tool của connector bạn để lại, kể cả tool ghi.

**Trigger:** Schedule → Weekly → thứ Hai 09:00 (giờ nhập theo múi giờ của bạn, hệ thống tự quy đổi).

**Prompt:** dán nguyên khối dưới đây. Đây cũng là prompt của routine "Update Tracking" đang chạy mỗi thứ Ba.

---

```
Cập nhật số liệu dashboard của Video Ads Team từ Notion.

Công cụ truy vấn Notion có hạn mức tính theo workspace. Được phép gọi TỐI ĐA 4 lượt
truy vấn SQL, không hơn. Không chạy truy vấn thăm dò, không thử lại khi đã đủ dữ liệu.

Bước 1 — Truy vấn SQL trên data source
collection://33807693-3135-80bc-90cd-000ba3b49e87, mỗi lượt đúng câu này:

SELECT url, "Period", "Creator", "PM", "Video Count", "Game Title", "Iteration", "Status"
FROM "collection://33807693-3135-80bc-90cd-000ba3b49e87"
WHERE "Team" = 'Video Ads Team'
  AND "Period" IS NOT NULL
  AND "Period" NOT LIKE 'April%'
  AND "Period" NOT LIKE 'May%'
ORDER BY createdTime, url
LIMIT 100 OFFSET 0

Notion trả tối đa 100 dòng mỗi lượt. Lượt sau giữ nguyên câu truy vấn, chỉ tăng OFFSET
thêm 100 (100, 200, 300). Dừng khi một lượt trả về ít hơn 100 dòng. Đừng dựa vào
has_more — có lúc nó báo false dù vẫn còn dòng.

Bước 2 — Kiểm tra tính đầy đủ TRƯỚC KHI ghi file.

Lượt cuối phải trả về ít hơn 100 dòng, và dữ liệu phải có các kỳ gần đây (kỳ của tuần
này và tuần trước). Nếu dùng hết 4 lượt mà lượt thứ 4 vẫn đủ 100 dòng, hoặc thiếu kỳ
gần đây:

  DỪNG LẠI. Không ghi file, không commit, không mở pull request.
  Báo rõ lấy được bao nhiêu dòng, tới kỳ nào và còn thiếu gì.

Ghi đè file bằng dữ liệu thiếu sẽ làm dashboard tụt về quá khứ — tệ hơn là không
làm gì cả.

Bước 3 — Ghi ra file data/latest.csv trong repo, mỗi task một dòng, đúng định dạng:

Period,Team,Creator,PM,Video Count,Game Title,Iteration,Status,Page ID

- Dòng đầu là đúng header trên, đúng thứ tự cột
- Cột Team luôn ghi: Video Ads Team
- Cột Creator ghi TÊN người, không phải user ID. Truy vấn trả về ID thì dùng công cụ
  tra người dùng của Notion để đổi sang tên — công cụ đó không tính vào hạn mức truy
  vấn. ID không tra ra tên (người đã rời workspace) thì ghi: Khong xac dinh
- Nhiều người trong một task thì nối bằng dấu phẩy và bọc trong nháy kép,
  ví dụ "Zaid, Elvis"
- Cột PM: ghi TÊN PM, đổi từ user ID giống cột Creator. Task không có PM thì để trống
  hẳn ô đó — KHÔNG ghi "[]", "null" hay "none". Ô trống nghĩa là creator làm solo.
- Cột Video Count: số nguyên. Task chưa nhập số thì để trống ô nhưng VẪN giữ dòng
  (dòng đó là kế hoạch, tab PM cần hiển thị).
- Cột Game Title, Iteration, Status: giữ nguyên văn như Notion. Iteration trống thì để
  trống. Giá trị có dấu phẩy hoặc dấu nháy thì bọc nháy kép theo chuẩn CSV.
- Cột Page ID: 32 ký tự hex của trang task, lấy từ cột url (phần sau dấu / cuối cùng,
  bỏ dấu gạch ngang nếu có). Ví dụ url https://app.notion.com/3df0769331358038b85ff9476ac3a385
  → Page ID 3df0769331358038b85ff9476ac3a385
- Giữ nguyên văn tên Period, không chuẩn hoá, không dịch, không bỏ tiền tố [W1]
- Giữ thứ tự dòng như kết quả truy vấn

Bước 4 — So với file data/latest.csv hiện có.

- Giống hệt: không commit, báo là chưa có gì mới, kết thúc.
- Khác: commit với message "Cập nhật số liệu <ngày hôm nay>", đẩy lên nhánh mặc
  định. Nếu bị từ chối thì đẩy lên nhánh claude/cap-nhat-so-lieu và mở pull request.

Bước 5 — Trong phần tóm tắt ghi rõ:
- Dùng hết bao nhiêu lượt truy vấn và tổng số dòng
- Kỳ mới nhất đã bắt đầu, tổng video của kỳ đó
- Period nào xuất hiện lần đầu so với file cũ

KHÔNG sửa index.html. KHÔNG sửa config.json. KHÔNG chạy update_dashboard.py.
```

---

Bấm **Create**, rồi bấm **Run now** trên trang chi tiết routine để chạy thử ngay thay vì đợi thứ Hai.

## Kiểm tra sau lần chạy đầu

Trạng thái xanh trong danh sách run **không có nghĩa là việc đã xong đúng** — nó chỉ báo session khởi động và thoát mà không lỗi hạ tầng. Mở run ra đọc transcript để xác nhận Claude thực sự đã làm gì.

Ba thứ cần thấy:

1. `data/latest.csv` có header 9 cột như trên, số dòng khớp số task của Video Ads Team trên Notion (trừ tháng 4, tháng 5)
2. Dashboard hiển thị đúng kỳ mới nhất ở tab Creator
3. Tab PM hiện đủ task của tuần này, bấm tên iteration mở đúng trang Notion

**Hạn mức 4 lượt × 100 dòng = 400 task.** Hiện có khoảng 200 task từ tháng 6, mỗi tuần thêm chừng 10. Khi gần chạm 400, thêm điều kiện loại các kỳ cũ (ví dụ `AND "Period" NOT LIKE 'June%'`) vào câu truy vấn.

## Vì sao chia việc như vậy

Routine chỉ làm đúng một việc: bê dữ liệu thô từ Notion ra CSV. Toàn bộ phần dễ sai — map Period sang tuần, cộng chỉ tiêu cho kỳ gộp, phân loại dev/art, chia video khi một task có nhiều creator — nằm trong `update_dashboard.py`, chạy hoàn toàn xác định.

Nghĩa là hai lần chạy trên cùng dữ liệu luôn cho kết quả y hệt, và khi số liệu trông sai bạn đọc được CSV để biết lỗi nằm ở Notion hay ở cách tính. Nếu để Claude tự viết khối dữ liệu vào HTML, mỗi lần chạy sẽ khác nhau một chút và không lần ra được nguyên nhân.

Đây cũng là lý do prompt cấm routine tự sửa `config.json`. Một Period mới có thể là tuần bình thường, cũng có thể là kỳ gộp nghỉ lễ cần cộng thêm chỉ tiêu — không có cách nào phân biệt từ dữ liệu. Đoán sai thì mọi con số phần trăm của kỳ đó sai theo mà không ai biết, nên routine được yêu cầu dừng lại và hỏi.

## Vài điều cần biết

**Routine thuộc về tài khoản cá nhân của bạn**, không chia sẻ cho đồng nghiệp, và tính vào hạn mức chạy hằng ngày của tài khoản bạn. Nếu bạn nghỉ việc hoặc rời tổ chức, routine dừng theo. Với thứ cả đội dùng chung thì nên có người thứ hai tạo routine dự phòng, hoặc chuyển sang cách dùng token khi admin cấp.

**Admin Team/Enterprise có thể tắt routine cho cả tổ chức** bằng toggle Routines tại `claude.ai/admin-settings/claude-code`. Nếu workspace Notion của bạn đã khoá quyền tạo connection thì khả năng cái này cũng bị khoá là có thật. Thử tạo routine là biết ngay.

**Push thẳng lên nhánh mặc định có thể bị chặn** nếu nhánh được bảo vệ, hoặc đang có commit của người khác. Prompt đã tính tới việc này và bảo routine mở pull request thay thế.

**Nếu routine không dùng được**, phần "Cách C" trong README vẫn chạy tốt — chỉ cần nhờ admin tạo integration rồi đưa lại token, không cần nới quyền cho ai.
