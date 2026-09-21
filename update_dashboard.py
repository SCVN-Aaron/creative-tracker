#!/usr/bin/env python3
"""
Cập nhật khối dữ liệu trong index.html từ Creative Production DB.
Chỉ dùng thư viện chuẩn của Python, không cần pip install.

Hai nguồn đọc, chọn cái nào cũng ra kết quả giống nhau:

  Từ API — cần integration token, phải được admin workspace cho phép tạo:
      export NOTION_TOKEN=ntn_xxx
      python3 update_dashboard.py

  Từ file CSV — chỉ cần quyền xem và nút Export của Notion, không cần token:
      python3 update_dashboard.py --csv "~/Downloads/Creative Production DB.csv"

Thêm --check để xem trước mà không ghi đè file.
"""

import csv
import json
import os
import re
import sys
import urllib.error
import urllib.request
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
CONFIG = json.loads((HERE / "config.json").read_text(encoding="utf-8"))
TOKEN = os.environ.get("NOTION_TOKEN", "").strip()

START = "// <<< DATA_START"
END = "// <<< DATA_END >>>"


# --------------------------------------------------------------- Notion API
def _post(url, body, version):
    req = urllib.request.Request(
        url,
        data=json.dumps(body).encode(),
        headers={
            "Authorization": f"Bearer {TOKEN}",
            "Notion-Version": version,
            "Content-Type": "application/json",
        },
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=45) as r:
        return json.loads(r.read())


def fetch_rows():
    """Thử endpoint data source (API mới) trước, không được thì lùi về database query."""
    attempts = [
        (f"https://api.notion.com/v1/data_sources/{CONFIG['data_source_id']}/query", "2025-09-03"),
        (f"https://api.notion.com/v1/databases/{CONFIG['database_id']}/query", "2022-06-28"),
    ]
    last_err = None
    for url, version in attempts:
        rows, cursor = [], None
        try:
            while True:
                body = {"page_size": 100}
                if cursor:
                    body["start_cursor"] = cursor
                data = _post(url, body, version)
                rows.extend(data["results"])
                if not data.get("has_more"):
                    break
                cursor = data["next_cursor"]
            print(f"  Đọc qua {url.split('/v1/')[1].split('/')[0]} (Notion-Version {version})")
            return rows
        except urllib.error.HTTPError as e:
            last_err = f"HTTP {e.code} tại {url}: {e.read().decode()[:300]}"
        except urllib.error.URLError as e:
            last_err = f"Không kết nối được {url}: {e.reason}"
    die(f"Không đọc được database.\n  {last_err}")


def read_csv(path):
    """Notion → menu ... → Export → Markdown & CSV. Cột giữ nguyên tên trên Notion."""
    path = Path(path).expanduser()
    if not path.exists():
        die(f"Không thấy file {path}")
    rows = []
    with path.open(encoding="utf-8-sig", newline="") as f:
        for r in csv.DictReader(f):
            # dựng lại đúng hình dạng mà phần xử lý bên dưới đang mong đợi
            creators = [c.strip() for c in (r.get("Creator") or "").split(",") if c.strip()]
            rows.append({"properties": {
                "Period": {"select": {"name": (r.get("Period") or "").strip() or None}},
                "Team": {"select": {"name": (r.get("Team") or "").strip() or None}},
                "Creator": {"people": [{"name": c} for c in creators]},
                "Video Count": {"type": "rich_text",
                                "rich_text": [{"plain_text": (r.get("Video Count") or "").strip()}]},
                "Iteration": {"title": [{"plain_text": (r.get("Iteration") or "").strip()}]},
                "PM": {"people": [{"name": x.strip()} for x in (r.get("PM") or "").split(",")
                                  if x.strip() and x.strip() not in ("[]", "null")]},
            }})
    print(f"  Đọc từ CSV: {path.name}")
    return rows


def arg_value(flag):
    if flag in sys.argv:
        i = sys.argv.index(flag)
        if i + 1 < len(sys.argv):
            return sys.argv[i + 1]
        die(f"Thiếu đường dẫn sau {flag}")
    return None


# --------------------------------------------------------------- đọc property
def sel(props, name):
    v = props.get(name) or {}
    return (v.get("select") or {}).get("name")


def people(props, name):
    v = props.get(name) or {}
    out = []
    for p in v.get("people", []):
        n = p.get("name")
        if n:
            out.append(n.strip())
    return out


def row_title(props):
    v = props.get("Iteration") or {}
    parts = v.get("title") or v.get("rich_text") or []
    return "".join(t.get("plain_text", "") for t in parts).strip() or "(không tên)"


def video_count(props, name="Video Count"):
    """Cột này đang là text nên phải bóc số ra, và ghi nhận ô nào không đọc được."""
    v = props.get(name) or {}
    if v.get("type") == "number":
        return v.get("number"), None
    raw = "".join(t.get("plain_text", "") for t in v.get("rich_text", [])).strip()
    if not raw:
        return None, None
    if re.fullmatch(r"\d+", raw):
        return int(raw), None
    m = re.search(r"\d+", raw)
    return (int(m.group()) if m else None), raw


# --------------------------------------------------------------- helpers
WARNINGS = []


def warn(msg):
    WARNINGS.append(msg)
    print(f"  ! {msg}")


def die(msg):
    print(f"\nDỪNG: {msg}\n", file=sys.stderr)
    sys.exit(1)


def js(v):
    return "null" if v is None else json.dumps(v, ensure_ascii=False)


# --------------------------------------------------------------- build
def build():
    csv_path = arg_value("--csv")
    if csv_path:
        print("Đang đọc file CSV...")
        rows = read_csv(csv_path)
    else:
        if not TOKEN:
            die("Chưa có NOTION_TOKEN.\n"
                "  Đặt biến môi trường rồi chạy lại, hoặc dùng bản xuất CSV:\n"
                '    python3 update_dashboard.py --csv "~/Downloads/Creative Production DB.csv"')
        print("Đang đọc Notion...")
        rows = fetch_rows()
    print(f"  {len(rows)} dòng")

    # bảng tra Period -> cấu hình tuần
    period_map, order = {}, []
    for idx, p in enumerate(CONFIG["periods"]):
        for key in [p["notion"], *p.get("aliases", [])]:
            period_map[key] = idx
        order.append(p)
    skip = set(CONFIG["bo_qua_periods"])
    teams_cfg = CONFIG["teams"]

    # tổng hợp: videos[team][person][week_index]
    videos = {t: defaultdict(lambda: [None] * len(order)) for t in teams_cfg}
    # Tên PM đi kèm từng người mỗi kỳ. Có PM = làm chung, không có = solo.
    pms = {t: defaultdict(lambda: [set() for _ in range(len(order))]) for t in teams_cfg}
    seen_periods, unknown, bad_counts, empty_counts = set(), set(), [], []

    for row in rows:
        props = row["properties"]
        period = sel(props, "Period")
        team = sel(props, "Team")
        creators = people(props, "Creator")
        count, messy = video_count(props)

        if period is None or team is None or not creators:
            continue
        # Lọc đội TRƯỚC khi xét Period: các đội không theo dõi có thể đặt tên kỳ
        # theo quy ước riêng, và những cái tên đó không được phép làm dừng cả lần chạy.
        if team not in teams_cfg:
            continue
        if period in skip:
            continue
        if period not in period_map:
            unknown.add(period)
            continue
        if count is None:
            # Có task, đúng đội, đúng kỳ, nhưng chưa ai điền Video Count.
            # Người này bị loại khỏi cả sản lượng lẫn đầu người, làm tỷ lệ đạt
            # chỉ tiêu của kỳ đó cao hơn thực tế — nên phải báo, không bỏ qua im lặng.
            empty_counts.append(f'{period} / {", ".join(creators)} — "{row_title(props)}" chưa nhập Video Count')
            continue
        if messy:
            bad_counts.append(f"{period} / {creators[0]} → Video Count ghi “{messy}”, đọc thành {count}")

        seen_periods.add(period)
        w = period_map[period]
        # một task có nhiều creator thì chia đều, làm tròn về số nguyên gần nhất
        share = count / len(creators)
        pm_names = people(props, "PM")
        for c in creators:
            cur = videos[team][c][w]
            videos[team][c][w] = share if cur is None else cur + share
            for n in pm_names:
                pms[team][c][w].add(n)

    if unknown:
        team_names = ", ".join(teams_cfg)
        die(
            f"Gặp Period chưa khai báo trong config.json (đội đang theo dõi: {team_names}):\n    - "
            + "\n    - ".join(sorted(unknown))
            + "\n  Thêm vào mục 'periods' (hoặc 'bo_qua_periods') rồi chạy lại."
        )
    for b in bad_counts:
        warn(b)
    if empty_counts:
        print(f"\n  {len(empty_counts)} task có người làm nhưng chưa nhập Video Count:")
        for e in empty_counts:
            warn(e)
        print("  Những người này không được tính vào đầu người, nên tỷ lệ đạt chỉ tiêu")
        print("  của các kỳ đó đang cao hơn thực tế. Điền số trên Notion rồi chạy lại.")

    # làm tròn
    for team in videos:
        for person in videos[team]:
            videos[team][person] = [
                None if v is None else int(round(v)) for v in videos[team][person]
            ]

    # cắt bỏ các tuần cuối chưa ai nhập số
    used = [i for i in range(len(order)) if any(
        v[i] is not None for t in videos for v in videos[t].values())]
    if not used:
        die("Không có tuần nào có Video Count. Kiểm tra lại quyền của integration.")
    last = max(used) + 1
    order = order[:last]
    for team in videos:
        for person in videos[team]:
            videos[team][person] = videos[team][person][:last]
        for person in pms[team]:
            pms[team][person] = pms[team][person][:last]

    # ---------- sinh JS ----------
    def weeks_js():
        out = []
        for p in order:
            bits = [f'label:{js(p["label"])}', f'full:{js(p["notion"])}']
            if p.get("bonus"):
                bits.append(f'bonus:{p["bonus"]}')
            if p.get("bonusEtc"):
                bits.append(f'bonusEtc:{p["bonusEtc"]}')
            if p.get("note"):
                bits.append(f'note:{js(p["note"])}')
            out.append("  {" + ", ".join(bits) + "}")
        return "const WEEKS = [\n" + ",\n".join(out) + "\n];"

    def team_js(varname, team_name):
        ppl = videos[team_name]
        if not ppl:
            return f"const {varname} = {{}};"
        width = max(len(n) for n in ppl)
        lines = []
        for name in sorted(ppl, key=lambda n: -sum(v or 0 for v in ppl[n])):
            arr = ",".join(f"{js(v):>4}" for v in ppl[name])
            lines.append(f'  {json.dumps(name, ensure_ascii=False):<{width+3}}:[{arr}]')
        return f"const {varname} = {{\n" + ",\n".join(lines) + "\n};"

    def pm_js(team_name):
        rows = []
        for name, arr in pms[team_name].items():
            if not any(arr):
                continue
            cells = ",".join(js(", ".join(sorted(x))) if x else '""' for x in arr)
            rows.append(f"  {json.dumps(name, ensure_ascii=False)}:[{cells}]")
        return "const VIDEO_PM = {\n" + ",\n".join(rows) + "\n};"

    def months_js():
        buckets = defaultdict(list)
        notes = {}
        for i, p in enumerate(order):
            buckets[p["month"]].append(i)
            if p.get("bonus"):
                notes[p["month"]] = f'kỳ {p["label"].replace("Tuần ", "")} gộp nhiều tuần'
        out = []
        for m, ws in buckets.items():
            bits = [f"label:{js(m)}", f"weeks:[{','.join(map(str, ws))}]"]
            if m in notes:
                bits.append(f"note:{js(notes[m])}")
            out.append("  {" + ", ".join(bits) + "}")
        return "const MONTHS = [\n" + ",\n".join(out) + "\n];"

    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    block = "\n".join([
        f"{START} — khối này do update_dashboard.py ghi đè, đừng sửa tay >>>",
        f'const DATA_STAMP = "{stamp}";',
        weeks_js(),
        "",
        team_js("VIDEO_TEAM", "Video Ads Team"),
        pm_js("Video Ads Team"),
        "",
        months_js(),
        END,
    ])

    # tóm tắt
    print("\nTổng hợp:")
    for team_name, cfg in teams_cfg.items():
        ppl = videos[team_name]
        tot = sum(v for arr in ppl.values() for v in arr if v)
        print(f"  {team_name}: {len(ppl)} người, {tot} video, {len(order)} kỳ")
    latest = order[-1]
    print(f"  Kỳ mới nhất: {latest['label']} ({latest['notion']})")

    return block


# --------------------------------------------------------------- ghi file
def find_dashboard():
    """Tìm file dashboard. Ưu tiên tên trong config, nhưng nếu tên đó không còn
    thì tự dò file .html nào có dấu mốc DATA_START — để việc đổi tên file không
    làm hỏng cả quy trình."""
    named = CONFIG.get("output_html", "index.html")
    candidates = [HERE / named, HERE / "index.html"]
    for c in candidates:
        if c.exists() and START in c.read_text(encoding="utf-8", errors="ignore"):
            return c

    found = [f for f in sorted(HERE.glob("*.html"))
             if START in f.read_text(encoding="utf-8", errors="ignore")]
    if len(found) == 1:
        warn(f"Không thấy {named}, dùng {found[0].name} thay thế. "
             f'Nên sửa "output_html" trong config.json cho khớp.')
        return found[0]
    if len(found) > 1:
        die("Có nhiều file .html chứa dấu mốc dữ liệu: "
            + ", ".join(f.name for f in found)
            + '\n  Sửa "output_html" trong config.json để chỉ rõ file nào.')
    die(f"Không tìm thấy file dashboard nào cạnh script này.\n"
        f"  Đã tìm: {named}, index.html, và mọi file .html trong thư mục.\n"
        f"  File dashboard phải chứa dòng đánh dấu {START}")


def write(block, check_only):
    path = find_dashboard()
    html = path.read_text(encoding="utf-8")
    a = html.find(START)
    b = html.find(END)
    if b < 0:
        die(f"Thiếu dấu mốc {END} trong {path.name}.")
    new = html[:a] + block + html[b + len(END):]

    if new == html:
        print("\nKhông có gì thay đổi.")
        return
    if check_only:
        print("\n--check: có thay đổi nhưng chưa ghi file.")
        return
    path.write_text(new, encoding="utf-8")
    print(f"\nĐã cập nhật {path.name}.")


if __name__ == "__main__":
    block = build()
    write(block, "--check" in sys.argv)
    if WARNINGS:
        print(f"\n{len(WARNINGS)} cảnh báo ở trên — nên kiểm tra lại các ô đó trong Notion.")
