#!/usr/bin/env python3
"""Gộp file CSV export của Notion (Creative Production DB …_all.csv) vào data/latest.csv.

    python3 tools/notion_merge.py <export_all.csv> [data/latest.csv]

- Chỉ lấy Video Ads Team, bỏ tháng 4 – 5 và dòng không có Creator.
- Giữ Page ID (link mở Notion) của task cũ: khớp theo Period + Creator + Iteration; task đổi tên,
  đổi creator hay đổi kỳ thì khớp tiếp theo (Creator, Iteration) → (Period, Creator) → (Period, Iteration).
- Trang trùng trên Notion (cùng Period + Creator + Iteration + số video): giữ bản có Creative number.
- Ideation ở tuần đã qua = chưa phải sản lượng → bỏ; Ideation từ tuần này trở đi = kế hoạch → giữ.
- Game Title trống → suy ra từ Iteration (bỏ "(YT)/(Social)", lấy phần trước "_").
- Iteration là link Notion → đổi về tên.
- Ghi cả cột Creative number (tab "Đánh số" dùng để tính số tiếp theo).
- Team UA và team AI dùng chung dãy số Creative theo game (UA điền trước, AI điền sau), nên số Creative
  của AI Ads Team được ghi riêng vào data/ai_numbers.csv để tab "Đánh số" tính số cuối trên cả hai team.
In ra các thay đổi đáng chú ý để kiểm tra trước khi commit.
"""
import csv, datetime, re, sys
from collections import defaultdict

FIELDS = ["Period", "Team", "Creator", "PM", "Video Count", "Game Title", "Iteration", "Status", "Page ID", "Creative number"]
TEAM = "Video Ads Team"

norm = lambda s: re.sub(r"\s+", " ", (s or "").strip())
cre = lambda s: ", ".join(sorted(norm(c) for c in (s or "").split(",") if c.strip()))


def iter_name(s):
    s = norm(s)
    m = re.match(r"https?://\S*/p/([^?#]+)", s)
    if not m:
        return s
    return re.sub(r"-[0-9a-f]{32}$", "", m.group(1)).replace("-", " ")


def period_start(p):
    m = re.search(r"\((\d+)/(\d+)\s*~", p)
    if not m:
        return None
    today = datetime.date.today()
    d = datetime.date(today.year, int(m[1]), int(m[2]))
    if (d - today).days > 180:
        d = d.replace(year=d.year - 1)
    return d


AI_TEAM = "AI Ads Team"
AI_FIELDS = ["Period", "Creator", "Game Title", "Iteration", "Creative number"]


def write_ai(src, dst):
    rows = [r for r in csv.DictReader(open(src, encoding="utf-8-sig"))
            if norm(r.get("Team")) == AI_TEAM and norm(r.get("Creative number")) and norm(r.get("Period"))]
    w = csv.DictWriter(open(dst, "w", encoding="utf-8", newline=""), fieldnames=AI_FIELDS, lineterminator="\n")
    w.writeheader()
    for r in rows:
        w.writerow({"Period": norm(r["Period"]), "Creator": norm(r.get("Creator")), "Game Title": norm(r.get("Game Title")),
                    "Iteration": iter_name(r.get("Iteration")), "Creative number": norm(r["Creative number"])})
    print(f"Đã ghi {len(rows)} task AI có Creative number vào {dst}")


def main(src, dst):
    old = list(csv.DictReader(open(dst, encoding="utf-8")))
    rows = [r for r in csv.DictReader(open(src, encoding="utf-8-sig"))
            if norm(r.get("Team")) == TEAM and norm(r.get("Period")) and norm(r.get("Creator"))
            and not re.match(r"(April|May)", norm(r["Period"]))]

    # trang trùng
    groups = defaultdict(list)
    for r in rows:
        groups[(norm(r["Period"]), cre(r["Creator"]), iter_name(r["Iteration"]), norm(r.get("Video Count")))].append(r)
    for k, v in groups.items():
        if len(v) > 1:
            keep = ([x for x in v if norm(x.get("Creative number"))] or v)[0]
            for x in v:
                if x is not keep:
                    print(" TRÙNG (bỏ 1 bản):", k)
                    rows.remove(x)

    # Ideation ở tuần đã qua
    today = datetime.date.today()
    wk0 = today - datetime.timedelta(days=today.weekday())
    def past_idea(r):
        st = period_start(norm(r["Period"]))
        return norm(r.get("Status")).lower() == "ideation" and (st is None or st < wk0)
    for r in [r for r in rows if past_idea(r)]:
        print(" BỎ Ideation tuần đã qua:", norm(r["Period"]), norm(r["Creator"]), iter_name(r["Iteration"]))
    rows = [r for r in rows if not past_idea(r)]

    games = {norm(r["Game Title"]).lower(): norm(r["Game Title"]) for r in rows if norm(r.get("Game Title"))}
    def guess_game(it):
        t = re.sub(r"^\s*\([^)]*\)\s*", "", iter_name(it))
        t = t.split("_")[0].strip()
        k = re.sub(r"[^a-z0-9]", "", t.lower())
        for g in games.values():
            if re.sub(r"[^a-z0-9]", "", g.lower()) == k:
                return g
        return t

    key = lambda r: (norm(r["Period"]), cre(r["Creator"]), iter_name(r["Iteration"]))
    left = defaultdict(list)
    for r in old:
        left[key(r)].append(r)
    out, new_tasks = [], []
    for r in rows:
        m = left.get(key(r))
        o = m.pop(0) if m else None
        out.append({"Period": norm(r["Period"]), "Team": TEAM, "Creator": norm(r["Creator"]), "PM": norm(r.get("PM")),
                    "Video Count": norm(r.get("Video Count")),
                    "Game Title": norm(r.get("Game Title")) or guess_game(r["Iteration"]),
                    "Iteration": iter_name(r["Iteration"]), "Status": norm(r.get("Status")),
                    "Page ID": o["Page ID"] if o else "", "Creative number": norm(r.get("Creative number"))})
        if not o:
            new_tasks.append(out[-1])
    rest = [r for v in left.values() for r in v]
    for row in list(new_tasks):
        for f in (lambda o: cre(o["Creator"]) == cre(row["Creator"]) and iter_name(o["Iteration"]) == row["Iteration"],
                  lambda o: norm(o["Period"]) == row["Period"] and cre(o["Creator"]) == cre(row["Creator"]),
                  lambda o: norm(o["Period"]) == row["Period"] and iter_name(o["Iteration"]) == row["Iteration"]):
            c = [o for o in rest if f(o)]
            if len(c) == 1:
                row["Page ID"] = c[0].get("Page ID", "")
                rest.remove(c[0]); new_tasks.remove(row)
                print(" ĐỔI:", c[0]["Period"], c[0]["Creator"], c[0]["Iteration"], "→", row["Period"], row["Creator"], row["Iteration"])
                break
    for r in new_tasks:
        print(" MỚI:", r["Period"], r["Creator"], r["Iteration"], r["Video Count"], r["Status"])
    for r in rest:
        print(" KHÔNG CÒN TRÊN NOTION:", r["Period"], r["Creator"], r["Iteration"], r.get("Video Count"))
    w = csv.DictWriter(open(dst, "w", encoding="utf-8", newline=""), fieldnames=FIELDS, lineterminator="\n")
    w.writeheader(); w.writerows(out)
    print(f"Đã ghi {len(out)} task vào {dst}")
    import os
    write_ai(src, os.path.join(os.path.dirname(dst) or ".", "ai_numbers.csv"))


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "data/latest.csv")
