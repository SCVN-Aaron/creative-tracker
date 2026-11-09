#!/usr/bin/env python3
"""Xuất file Excel số video từng Creator đã làm (từ data/drive_videos.csv do tools/drive_summary.py ghi).

    python3 -I tools/drive_excel.py [data] [output.xlsx]

Sheet: Theo tháng · Theo tuần · Theo game · Danh sách video · Ghi chú
"""
import csv, datetime, os, sys
from collections import defaultdict
from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.formatting.rule import ColorScaleRule
from openpyxl.utils import get_column_letter

HEAD = PatternFill("solid", fgColor="1F4E5F")
TOT = PatternFill("solid", fgColor="E8EEF1")
WHITE = Font(bold=True, color="FFFFFF")
BOLD = Font(bold=True)


def team_map(d):
    team = {}
    for fn, t in (("latest.csv", "UA"), ("ai_numbers.csv", "AI")):
        fp = os.path.join(d, fn)
        if os.path.exists(fp):
            for r in csv.DictReader(open(fp, encoding="utf-8")):
                for c in (r.get("Creator") or "").split(","):
                    if c.strip():
                        team.setdefault(c.strip().lower(), t)
    return team


def pivot(ws, title, vids, colkey, cols, collabel, team, note):
    ws.append([title]); ws["A1"].font = Font(bold=True, size=13)
    ws.append([note]); ws["A2"].font = Font(italic=True, color="666666")
    ws.append([])
    hdr = ["Creator", "Team"] + [collabel(c) for c in cols] + ["Tổng"]
    ws.append(hdr)
    hr = ws.max_row
    for i in range(1, len(hdr) + 1):
        c = ws.cell(hr, i); c.fill = HEAD; c.font = WHITE
        c.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
    m = defaultdict(lambda: defaultdict(int))
    for v in vids:
        m[v["Creator"]][colkey(v)] += 1
    people = sorted(m, key=lambda p: (team.get(p.lower(), "?"), -sum(m[p].values())))
    first = ws.max_row + 1
    for p in people:
        ws.append([p, team.get(p.lower(), "?")] + [m[p].get(c) or None for c in cols])
        r = ws.max_row
        ws.cell(r, len(hdr)).value = f"=SUM({get_column_letter(3)}{r}:{get_column_letter(len(hdr)-1)}{r})"
        ws.cell(r, len(hdr)).font = BOLD
    last = ws.max_row
    ws.append(["Tổng", ""] + [f"=SUM({get_column_letter(i)}{first}:{get_column_letter(i)}{last})" for i in range(3, len(hdr) + 1)])
    for i in range(1, len(hdr) + 1):
        c = ws.cell(ws.max_row, i); c.fill = TOT; c.font = BOLD
    if cols:
        rng = f"C{first}:{get_column_letter(len(hdr)-1)}{last}"
        ws.conditional_formatting.add(rng, ColorScaleRule(start_type="num", start_value=0, start_color="FFFFFF",
                                                          end_type="max", end_color="7FB3C8"))
    ws.column_dimensions["A"].width = 14; ws.column_dimensions["B"].width = 7
    for i in range(3, len(hdr) + 1):
        ws.column_dimensions[get_column_letter(i)].width = 11
    ws.row_dimensions[hr].height = 32
    ws.freeze_panes = ws.cell(hr + 1, 3)


def main(d, out):
    vids = list(csv.DictReader(open(os.path.join(d, "drive_videos.csv"), encoding="utf-8")))
    team = team_map(d)
    for v in vids:
        v["_d"] = datetime.date.fromisoformat(v["Date"])
    scan = max((r.get("Updated", "") for r in csv.DictReader(open(os.path.join(d, "drive_numbers.csv"), encoding="utf-8"))), default="")
    note = f"Nguồn: Google Drive (thư mục VN 제작… của từng game), quét ngày {scan}. Mỗi số Creative = 1 video (không đếm trùng 3 kích thước)."
    wb = Workbook()
    months = sorted({v["_d"].strftime("%Y-%m") for v in vids})
    pivot(wb.active, "Số video đã làm theo tháng", vids, lambda v: v["_d"].strftime("%Y-%m"), months,
          lambda c: f"{c[5:]}/{c[:4]}", team, note)
    wb.active.title = "Theo tháng"
    mon = lambda x: x - datetime.timedelta(days=x.weekday())
    weeks = sorted({mon(v["_d"]) for v in vids})
    pivot(wb.create_sheet("Theo tuần"), "Số video đã làm theo tuần (bắt đầu thứ 2)", vids, lambda v: mon(v["_d"]), weeks,
          lambda c: c.strftime("%d/%m/%y"), team, note)
    cnt = defaultdict(int)
    for v in vids:
        cnt[v["Game"]] += 1
    games = sorted(cnt, key=lambda g: -cnt[g])
    pivot(wb.create_sheet("Theo game"), "Số video đã làm theo game", vids, lambda v: v["Game"], games, str, team, note)
    ws = wb.create_sheet("Danh sách video")
    cols = ["Game", "Type", "Creative number", "Creator", "Team", "Date", "Concept", "Lang", "Duration", "Sizes", "Files"]
    ws.append(cols)
    for i in range(1, len(cols) + 1):
        c = ws.cell(1, i); c.fill = HEAD; c.font = WHITE
    for v in sorted(vids, key=lambda v: (v["_d"], v["Game"], int(v["Creative number"]))):
        ws.append([v["Game"], v["Type"], int(v["Creative number"]), v["Creator"], team.get(v["Creator"].lower(), "?"),
                   v["_d"], v["Concept"], v["Lang"], v["Duration"], v["Sizes"], int(v["Files"] or 0)])
        ws.cell(ws.max_row, 6).number_format = "dd/mm/yyyy"
    for i, w in enumerate([20, 8, 15, 12, 7, 12, 22, 6, 9, 30, 6], 1):
        ws.column_dimensions[get_column_letter(i)].width = w
    ws.freeze_panes = "A2"; ws.auto_filter.ref = ws.dimensions
    ws = wb.create_sheet("Ghi chú")
    for line in [
        "Cách đếm",
        "• Dữ liệu lấy từ tên file video trên Google Drive: video<số Creative>_<concept>_<ngôn ngữ>_<thời lượng>_<kích thước>_<creator>_<ddmmyy>.mp4",
        "• Mỗi số Creative tính 1 video, dù có nhiều bản kích thước (9x16, 1x1, 16x9). Video trong thư mục Social Media là dãy Social riêng.",
        "• Ngày = ngày trong tên file; nếu gõ sai (lệch quá 45 ngày so với ngày upload) thì lấy ngày upload.",
        "• Team theo data Notion: Video Ads Team = UA, AI Ads Team = AI.",
        "• Bỏ qua: file 'please delete', file có tiền tố 99999_, số tạm 99999.",
        "• Chỉ tính video đã upload lên Drive — task đang làm chưa upload thì chưa có trong file này.",
        f"• Quét ngày {scan}.",
    ]:
        ws.append([line])
    ws["A1"].font = Font(bold=True, size=13); ws.column_dimensions["A"].width = 120
    wb.save(out)
    print(f"Đã ghi {out}: {len(vids)} video, {len(months)} tháng, {len(games)} game")


if __name__ == "__main__":
    d = sys.argv[1] if len(sys.argv) > 1 else "data"
    main(d, sys.argv[2] if len(sys.argv) > 2 else "creator_videos.xlsx")
