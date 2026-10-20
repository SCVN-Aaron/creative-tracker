# Unity Creative Scanner

Bộ tool cho người quay creative trực tiếp từ project Unity (playable ad). Có hai phần, dùng riêng hay chung đều được:

| Phần | Làm gì | Cần gì |
|---|---|---|
| `unity_scan.py` | Quét project **không cần mở Unity**, xuất báo cáo: phím tắt, joystick, skin, material/texture, âm thanh, scene | Python 3.8+ |
| `CreativeRecorder/` | Cửa sổ **Tools ▸ Creative Recorder** ngay trong Unity: phân loại & giải thích logic game, sửa mọi tuỳ chọn (âm thanh, skin, thông số…), ẩn joystick, gắn SFX, chỉnh tốc độ khi quay | Unity 2019.4+, không cần Python |

## 1. Quét project → báo cáo

**Windows:** kéo thả thư mục project Unity vào file `Quet project Unity.bat`.
**macOS:** bấm đúp `Quet project Unity.command`, kéo thư mục project vào cửa sổ Terminal, Enter.
**Dòng lệnh:**

```bash
python3 unity_scan.py "D:/Projects/MyPlayable" --open
python3 unity_scan.py "D:/Projects/MyPlayable" --out ./bao-cao --json
```

Báo cáo nằm ở `reports/<tên project>/` (đã được gitignore):

- `creative-scan.html` — mở bằng trình duyệt, có ô lọc và nút **Copy Markdown cho AI**.
- `creative-scan.md` — cùng nội dung dạng Markdown, dán cho AI khi cần hỏi thêm ("phím W làm gì?", "thêm SFX vào lúc nhặt coin ở đâu?").

### Báo cáo có gì

- **Phím tắt** — mọi `Input.GetKey/GetKeyDown/GetButton…`, `Keyboard.current.xKey`, file `.inputactions`. Mỗi phím ghi rõ: nhấn/giữ/thả, gọi hàm gì (hoặc dòng `Debug.Log`), ở file:dòng nào, có phải phím chỉ chạy trong Editor (`#if UNITY_EDITOR` — thường là phím cheat dành cho người quay). Phím khai báo bằng biến `KeyCode` thì đọc luôn giá trị đã đổi trong Inspector của scene/prefab. Nút ảo (`GetButton("Jump")`) được tra ra phím thật từ InputManager. Phím trong thư viện bên thứ 3 (Plugins, Joystick Pack…) để riêng.
- **Joystick & UI** — GameObject joystick trong scene/prefab (theo tên hoặc component), đường dẫn trong Hierarchy, script đọc joystick, code bật/tắt joystick; cùng danh sách UI thường phải ẩn khi quay: tay hướng dẫn, tutorial, CTA, logo, end card…
- **Skin** — trường skin trong code (`Material[] skins`…), giá trị đang gán trong scene, hàm `SetSkin/ChangeSkin…`, nhân vật dùng SkinnedMeshRenderer (material + mesh), asset có chữ skin/outfit/costume.
- **Material & Texture** — mỗi material: shader, texture theo từng slot (`_BaseMap`, `_MainTex`…), được dùng ở scene/prefab nào. Mỗi texture: kích thước, loại (Sprite/Default/Normal map), material nào dùng, texture không dùng. Dấu 🌍 = tên giống map/môi trường (ground, floor, road, bg…).
- **Âm thanh** — file âm thanh (độ dài, chưa gán ở đâu), AudioSource trong scene, clip gán vào script, code đang phát âm thanh, và **danh sách hành động** (Jump, Attack, Collect, Win, OnTriggerEnter, Button onClick…) đánh dấu ✗ nếu chưa có âm thanh — chỗ nên thêm SFX.
- **Scene** — scene trong Build Settings, số object, object gốc.

Tool chỉ đọc file, không sửa gì trong project.

## 2. Cửa sổ Creative Recorder trong Unity

Cài một trong hai cách, rồi mở **Tools ▸ Creative Recorder**:

- **Copy:** chép cả thư mục `CreativeRecorder/` vào `Assets/` của project, chờ Unity biên dịch.
- **Package Manager:** Window ▸ Package Manager ▸ **+** ▸ *Add package from git URL…* rồi dán
  `https://github.com/SCVN-Aaron/creative-tracker.git?path=/tools/unity-scanner/CreativeRecorder#main`
  (repo private thì máy cần đăng nhập Git có quyền đọc repo). Cách này không chép file vào `Assets/`, gỡ trong Package Manager là sạch.

Cửa sổ mở ở **chế độ đơn giản** (cho người không biết code), gồm 5 màn hình:

| Màn hình | Dùng để |
|---|---|
| Skin | Mọi object có hình trong scene, chia nhóm Nhân vật / Môi trường & map / Đồ vật & item / UI, có ảnh xem trước. Bấm **Đổi** rồi kéo material, texture, sprite hoặc mesh mới vào ô; đổi một lần là áp dụng cho mọi object cùng tên (vd. tất cả Coin). Game có sẵn bộ skin (mảng skin trong script) thì hiện thành ô ảnh, bấm là dùng; có hàm `SetSkin(int)` thì có nút 1, 2, 3… khi Play |
| Cheat | Mọi biến số / bật-tắt của script game, kể cả biến private. Phần **Thông số quan trọng** gom sẵn: Tiền, Điểm, Combo, Stack, Máu, Sát thương, Tốc độ, Thời gian, Số lượng, Level, Giá… Gõ số mới, nút **+100 / +1000** cho tiền / điểm, **Khoá** để giữ nguyên giá trị suốt lúc chơi (tiền không bao giờ hết, máu không giảm) |
| Âm thanh | Tool **tự tìm hành động** trong game: nhận / tiêu tiền, stack thêm / bớt (theo biến đếm hoặc số vật trong chỗ chứa stack), ăn điểm, combo, va chạm (đọc `CompareTag`, `GetComponent<>` trong `OnTriggerEnter`…), nhặt / ăn đồ (coin biến mất), popup thắng / thua / perfect hiện ra, trúng đòn, hạ gục, lên level. Chọn âm thanh cho hành động (có danh sách **Gợi ý** theo tên file) là xong; **Cao dần khi liên tiếp** cho combo / stack tiền nghe “đã” hơn. Có thêm hành động thủ công, và **thay / tắt tiếng** âm thanh có sẵn của game |
| Unlock | Tìm các **ô mở khoá** (trả tiền → xây dựng, nâng cấp, mở rộng): giá (sửa được), object mà ô đó bật lên (đọc từ biến và UnityEvent của script). Bấm **Di chuyển** rồi kéo tay cầm trong Scene view: node và mọi object đi kèm đi cùng nhau. Có nút nhích ←→ theo bước, xoay 90°, đưa tới giữa Scene view, bỏ tick object không muốn kéo theo, và nút **Mở khoá ngay** khi Play |
| Quay | Ẩn joystick (vẫn điều khiển được), ẩn tay hướng dẫn / tutorial / CTA, ẩn toàn bộ UI, tốc độ game 0.25×–2×, tạm dừng, tiến 1 frame, chụp ảnh ×1/×2/×4 |

**Skin, Cheat, Âm thanh không sửa game gốc.** Lựa chọn được lưu vào *Bộ chỉnh* (`ProjectSettings/CreativeRecorderPreset.json`, nằm ngoài `Assets` nên không vào bản build) và tự áp dụng mỗi lần bấm Play; dừng Play là game như cũ. Thanh trên cùng cho bật / tắt cả Bộ chỉnh, **Khác ▾** để lưu ra file (gửi cho người khác dùng chung), mở file, hoặc xoá hết. Riêng **Unlock** là sửa level nên vị trí mới được lưu vào scene (Ctrl+Z hoàn tác, Ctrl+S lưu).

Bấm **Chế độ nâng cao →** ở góc dưới để về các tab chi tiết:

| Tab | Dùng để |
|---|---|
| Logic game | Quét mọi script của game, xếp vào nhóm: điều khiển nhân vật, camera, kẻ địch/AI, spawner, luồng thắng/thua, tiền & phần thưởng, tutorial, end card/SDK, UI, âm thanh, VFX, va chạm, animation, map, skin, input, dữ liệu, tiện ích. Mỗi script có giải thích: nhóm đó làm gì, code đọc phím nào / tạo object / phát âm thanh…, tuỳ chọn trong Inspector, hàm gọi được, đang gắn ở bao nhiêu object (bấm để chọn). **Copy giải thích cho AI** xuất toàn bộ ra Markdown |
| Tuỳ chỉnh | Gom **mọi tuỳ chọn** trong scene (script game + AudioSource, Renderer, Image, Text, Camera, Light; tuỳ chọn thêm ScriptableObject và prefab) để sửa thẳng, xếp theo loại: âm thanh, skin & hình ảnh, thông số số, bật/tắt, màu, phím, lựa chọn, prefab, chữ. Xem theo loại / theo script / theo object, có ô lọc. **Thay tất cả**: đổi một âm thanh / material / sprite / prefab ở mọi chỗ dùng nó trong một lần bấm. Từ tab Logic bấm **Tuỳ chỉnh** cạnh script để chỉ xem tuỳ chọn của script đó |
| Phím tắt | Quét script, liệt kê phím + việc nó làm, bấm **Mở** để nhảy tới dòng code, **Copy danh sách** |
| Joystick & UI | Tìm joystick và UI hướng dẫn trong scene. **Ẩn (vẫn chạy)**: joystick vô hình nhưng vẫn kéo được để điều khiển (CanvasGroup alpha = 0). **Tắt hẳn**: SetActive(false). Nút ẩn tất cả joystick / toàn bộ UI |
| Skin | Chọn nhân vật trong Hierarchy → danh sách skin có sẵn trong script (bấm **Áp dụng**), gọi hàm `SetSkin(int)` khi đang Play, kéo thả material/mesh/sprite để thay trực tiếp |
| Texture | Chọn object → thay texture từng slot, chỉnh tiling, màu. Mặc định sửa trên **bản sao** material nên material gốc không bị đổi |
| SFX | Chọn âm thanh + lúc phát (nhấn phím, click/chạm, object bật/tắt, va chạm, Button onClick) → **Gắn vào object** đang chọn. Nghe thử, tắt tiếng AudioSource có sẵn |
| Quay | Time.timeScale (slow-mo 0.25×/0.5×…), Play/Pause/tiến 1 frame, ẩn UI một chạm, ẩn con trỏ, chụp ảnh ×1/×2/×4 vào `Screenshots/` |

Nên **bấm Play rồi mới chỉnh**: mọi thay đổi trên object trong scene tự mất khi dừng Play, project giữ nguyên. Chỉnh ở Edit mode thì thay đổi được lưu vào scene (Ctrl+Z để hoàn tác). Riêng mục ghi **asset** trong tab Tuỳ chỉnh (ScriptableObject, prefab) là sửa thẳng vào file kể cả khi đang Play — nên commit project trước khi sửa.

SFX gắn bằng tool là component `CreativeSfxTrigger`. Muốn chèn vào code game thì thêm một dòng ở hàm hành động mà báo cáo đánh dấu ✗:

```csharp
CreativeRecorder.CreativeSfxTrigger.PlayClip(coinClip);
```

**Trước khi build playable gửi đi, xoá thư mục `Assets/CreativeRecorder/`** (cài bằng Package Manager thì Remove package) (hoặc chỉ cài tool vào bản copy dùng để quay), và bỏ các dòng `PlayClip` đã chèn vào code. Thư mục `Generated/` bên trong chứa các material bản sao tạo ra khi đổi texture ở Edit mode.
