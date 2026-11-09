#!/usr/bin/env python3
"""Tổng hợp số video từng Creator đã làm từ danh sách file trên Google Drive (thư mục <game>/VN 제작/…).

    python3 -I tools/drive_summary.py <drive_files.csv> [data]

drive_files.csv: Game,Subfolder,Path,File,CreatedTime,ModifiedTime,FileId (một dòng mỗi file, do Claude quét Drive).
Tên file: video<số Creative>_<concept>_<ngôn ngữ>_<thời lượng>_<kích thước>_<creator>_<ddmmyy>.mp4
Một video xuất nhiều kích thước (9x16, 1x1, 16x9) → đếm 1 lần theo (game, số Creative, loại UA/Social).

Ghi ra:
  data/drive_videos.csv   — mỗi dòng 1 video: Game, Type, Creative number, Creator, Date, Concept, Lang, Duration, Sizes, Files
  data/drive_numbers.csv  — số Creative lớn nhất theo game (tab Đánh số dùng)
"""
import csv, datetime, os, re, sys
from collections import defaultdict

NAME_RE = re.compile(r"^video(\d{4,})_(.+)\.(mp4|mov|webm)$", re.I)
SIZE_RE = re.compile(r"^\d{3,4}x\d{3,4}$")
DATE_RE = re.compile(r"^\d{6}$")
DUR_RE = re.compile(r"^\d+s$", re.I)


def parse(name):
    m = NAME_RE.match(name.strip())
    if not m:
        return None
    parts = m.group(2).split("_")
    out = {"n": int(m.group(1)), "concept": "", "lang": "", "dur": "", "size": "", "creator": "", "date": None}
    # tìm từ phải sang: ddmmyy, creator, kích thước, thời lượng, ngôn ngữ, phần còn lại là concept
    if parts and DATE_RE.match(parts[-1]):
        d = parts.pop()
        try:
            out["date"] = datetime.date(2000 + int(d[4:]), int(d[2:4]), int(d[:2]))
        except ValueError:
            pass
    si = next((i for i in range(len(parts) - 1, -1, -1) if SIZE_RE.match(parts[i])), None)
    if si is not None:
        out["size"] = parts[si]
        out["creator"] = "_".join(parts[si + 1:]).strip().lower()
        rest = parts[:si]
        if rest and DUR_RE.match(rest[-1]):
            out["dur"] = rest.pop()
        if rest and re.fullmatch(r"[a-z]{2}", rest[-1], re.I):
            out["lang"] = rest.pop().lower()
        out["concept"] = "_".join(rest)
    else:
        out["concept"] = "_".join(parts)
    return out


def main(src, outdir):
    rows = list(csv.DictReader(open(src, encoding="utf-8")))
    vids, bad = {}, []
    for r in rows:
        p = parse(r["File"])
        if not p:
            if re.search(r"\.(mp4|mov|webm)$", r["File"], re.I):
                bad.append(r)
            continue
        social = "social" in (r.get("Path") or r.get("Subfolder") or "").lower()
        k = (r["Game"], "Social" if social else "UA", p["n"])
        v = vids.setdefault(k, {"game": r["Game"], "type": k[1], "n": p["n"], "creator": "", "date": None,
                                "concept": "", "lang": "", "dur": "", "sizes": set(), "files": 0})
        v["files"] += 1
        if p["size"]:
            v["sizes"].add(p["size"])
        for f in ("creator", "concept", "lang", "dur"):
            if p[f] and not v[f]:
                v[f] = p[f]
        if p["date"] and (not v["date"] or p["date"] < v["date"]):
            v["date"] = p["date"]
        if not v["date"]:
            v["date"] = datetime.date.fromisoformat(r["CreatedTime"][:10])
    vs = sorted(vids.values(), key=lambda v: (v["game"], v["type"], v["n"]))
    w = csv.writer(open(os.path.join(outdir, "drive_videos.csv"), "w", encoding="utf-8", newline=""), lineterminator="\n")
    w.writerow(["Game", "Type", "Creative number", "Creator", "Date", "Concept", "Lang", "Duration", "Sizes", "Files"])
    for v in vs:
        w.writerow([v["game"], v["type"], v["n"], v["creator"].title(), v["date"].isoformat() if v["date"] else "",
                    v["concept"], v["lang"], v["dur"], " ".join(sorted(v["sizes"])), v["files"]])
    mx = defaultdict(lambda: None)
    for v in vs:
        k = (v["game"], v["type"])
        if not mx[k] or v["n"] > mx[k]["n"]:
            mx[k] = v
    today = datetime.date.today().isoformat()
    w = csv.writer(open(os.path.join(outdir, "drive_numbers.csv"), "w", encoding="utf-8", newline=""), lineterminator="\n")
    w.writerow(["Game", "Type", "Max", "Files", "Latest file", "Updated"])
    for (g, t), v in sorted(mx.items()):
        cnt = sum(1 for x in vs if x["game"] == g and x["type"] == t)
        w.writerow([g, t, v["n"], cnt, f"video{v['n']}_{v['concept']}_{v['creator']}", today])
    print(f"{len(rows)} file → {len(vs)} video (đếm 1 lần mỗi số Creative), {len(mx)} dãy số")
    if bad:
        print(f"{len(bad)} file video không đúng mẫu tên, ví dụ:")
        for r in bad[:10]:
            print("  ", r["Game"], "/", r.get("Path"), "/", r["File"])


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "data")
