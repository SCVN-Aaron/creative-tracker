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

**Prompt:** dán nguyên khối dưới đây.

---

```
Cập nhật dashboard sản lượng của Video Ads Team từ Notion.

Bối cảnh: repo này có creative-weekly-tracker.html (dashboard), update_dashboard.py
(script xử lý) và config.json (bảng tra Period). Nhiệm vụ của bạn là lấy dữ liệu thô
từ Notion và để script làm phần tính toán — đừng tự viết hay tự sửa khối dữ liệu
trong file HTML.

Các bước:

1. Dùng connector Notion, đọc toàn bộ dòng trong data source
   collection://33807693-3135-80bc-90cd-000ba3b49e87 (Creative Production DB).
   Lấy 4 cột: Period, Team, Creator, Video Count. Nhớ phân trang cho tới hết,
   database này hơn 300 dòng.

2. Ghi ra file data/notion-export.csv trong repo, đúng định dạng sau:
   - Dòng đầu là header: Period,Team,Creator,Video Count
   - Mỗi dòng dữ liệu là một task
   - Cột Creator: nếu một task có nhiều người thì nối bằng dấu phẩy và bọc trong
     dấu nháy kép, ví dụ "Aaron, Bomi"
   - Giá trị chứa dấu phẩy phải bọc trong nháy kép
   - Giữ nguyên văn tên Period, đừng chuẩn hoá hay dịch

3. Chạy: python3 update_dashboard.py --csv data/notion-export.csv

4. Đọc kỹ output của script:
   - Nếu script dừng vì gặp Period chưa khai báo, ĐỪNG tự thêm vào config.json.
     Thay vào đó, mở một pull request chỉ chứa file CSV, và trong phần mô tả PR
     ghi rõ tên các Period lạ cùng đề xuất dòng cần thêm vào config.json. Người
     phụ trách sẽ quyết định kỳ đó có phải kỳ gộp nghỉ lễ hay không, vì phần
     chỉ tiêu cộng thêm không suy ra được từ dữ liệu.
   - Nếu script in cảnh báo về ô Video Count không phải số thuần, cứ tiếp tục
     nhưng chép nguyên các cảnh báo đó vào phần mô tả commit hoặc PR.

5. Nếu script chạy xong và creative-weekly-tracker.html có thay đổi, commit cả
   file HTML lẫn data/notion-export.csv với message dạng
   "Cập nhật số liệu tuần <ngày hôm nay>", rồi push lên nhánh mặc định.
   Nếu push bị từ chối, mở pull request thay thế.

6. Nếu không có gì thay đổi, không commit gì cả và báo lại là dữ liệu chưa mới.

Coi như thành công khi: hoặc file HTML đã được cập nhật và push, hoặc đã mở PR
nêu rõ vướng mắc, hoặc xác nhận không có dữ liệu mới.
```

---

Bấm **Create**, rồi bấm **Run now** trên trang chi tiết routine để chạy thử ngay thay vì đợi thứ Hai.

## Kiểm tra sau lần chạy đầu

Trạng thái xanh trong danh sách run **không có nghĩa là việc đã xong đúng** — nó chỉ báo session khởi động và thoát mà không lỗi hạ tầng. Mở run ra đọc transcript để xác nhận Claude thực sự đã làm gì.

Ba thứ cần thấy:

1. `data/notion-export.csv` xuất hiện trong repo, số dòng khớp với số task trên Notion
2. Trong `creative-weekly-tracker.html`, dòng `const DATA_STAMP` mang thời gian mới
3. Dashboard hiển thị đúng kỳ mới nhất

## Vì sao chia việc như vậy

Routine chỉ làm đúng một việc: bê dữ liệu thô từ Notion ra CSV. Toàn bộ phần dễ sai — map Period sang tuần, cộng chỉ tiêu cho kỳ gộp, phân loại dev/art, chia video khi một task có nhiều creator — nằm trong `update_dashboard.py`, chạy hoàn toàn xác định.

Nghĩa là hai lần chạy trên cùng dữ liệu luôn cho kết quả y hệt, và khi số liệu trông sai bạn đọc được CSV để biết lỗi nằm ở Notion hay ở cách tính. Nếu để Claude tự viết khối dữ liệu vào HTML, mỗi lần chạy sẽ khác nhau một chút và không lần ra được nguyên nhân.

Đây cũng là lý do prompt cấm routine tự sửa `config.json`. Một Period mới có thể là tuần bình thường, cũng có thể là kỳ gộp nghỉ lễ cần cộng thêm chỉ tiêu — không có cách nào phân biệt từ dữ liệu. Đoán sai thì mọi con số phần trăm của kỳ đó sai theo mà không ai biết, nên routine được yêu cầu dừng lại và hỏi.

## Vài điều cần biết

**Routine thuộc về tài khoản cá nhân của bạn**, không chia sẻ cho đồng nghiệp, và tính vào hạn mức chạy hằng ngày của tài khoản bạn. Nếu bạn nghỉ việc hoặc rời tổ chức, routine dừng theo. Với thứ cả đội dùng chung thì nên có người thứ hai tạo routine dự phòng, hoặc chuyển sang cách dùng token khi admin cấp.

**Admin Team/Enterprise có thể tắt routine cho cả tổ chức** bằng toggle Routines tại `claude.ai/admin-settings/claude-code`. Nếu workspace Notion của bạn đã khoá quyền tạo connection thì khả năng cái này cũng bị khoá là có thật. Thử tạo routine là biết ngay.

**Push thẳng lên nhánh mặc định có thể bị chặn** nếu nhánh được bảo vệ, hoặc đang có commit của người khác. Prompt đã tính tới việc này và bảo routine mở pull request thay thế.

**Nếu routine không dùng được**, phần "Cách C" trong README vẫn chạy tốt — chỉ cần nhờ admin tạo integration rồi đưa lại token, không cần nới quyền cho ai.
