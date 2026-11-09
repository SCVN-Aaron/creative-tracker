#!/usr/bin/env python3
"""Tổng hợp số video từng Creator đã làm từ danh sách file trên Google Drive (thư mục <game>/VN 제작/…).

    python3 -I tools/drive_summary.py <drive_files.csv> [data]

drive_files.csv: Game,Subfolder,Path,File,CreatedTime,ModifiedTime,FileId (một dòng mỗi file, do Claude quét Drive).
Tên file: video<số Creative>_<concept>_<ngôn ngữ>_<thời lượng>_<kích thước>_<creator>_<ddmmyy>.mp4
Một video xuất nhiều kích thước (9x16, 1x1, 16x9) → đếm 1 lần theo (game, số Creative, loại UA/Social).

Ghi ra:
  data/drive_videos.csv   — mỗi dòng 1 video: Game, Type, Creative number, Creator, Date, Concept, Lang, Duration, Sizes, Files
  data/drive_numbers.csv  — số Creative lớn nhất theo game, dãy UA (tab Đánh số dùng)
  data/drive_creator_month.csv — số video mỗi Creator theo tháng (Team lấy theo data Notion: UA / AI)
  data/drive_creator_game.csv  — số video mỗi Creator theo game
  data/drive_games.csv         — link thư mục Drive của từng game (nếu có drive_folders.csv cạnh drive_files.csv)
"""
import csv, datetime, os, re, sys
from collections import defaultdict

NAME_RE = re.compile(r"^(?:copy of )?video(\d{4,})_(.+?)(?:\.(?:mp4|mov|webm))*$", re.I)
SIZE_RE = re.compile(r"^\d{3,4}x\d{3,4}$")
DATE_RE = re.compile(r"^\d{6}$")
DUR_RE = re.compile(r"^\d+s$", re.I)


RATIO = {"1080x1920": "9x16", "1080x1080": "1x1", "1920x1080": "16x9"}


def ratio_of(folder):
    f = folder.replace(":", "x").replace(".", " ")
    for k in ("9x16", "16x9", "1x1"):
        if k in f:
            return k
    return "other"


def parse(name):
    # "99999_video…" = video bị loại/đánh dấu xoá → không đếm
    name = re.sub(r"[.\-]+(mp4|mov|webm)(\.(mp4|mov|webm))*$", r".\1", name.strip(), flags=re.I)  # "..mp4", "-mp4.mp4", ".mp4.mp4"
    m = NAME_RE.match(name)
    if not m:
        return None
    parts = m.group(2).split("_")
    out = {"n": int(m.group(1)), "concept": "", "lang": "", "dur": "", "size": "", "creator": "", "date": None}
    # tìm từ phải sang: ddmmyy, creator, kích thước, thời lượng, ngôn ngữ, phần còn lại là concept
    if len(parts) > 1 and re.fullmatch(r"\d{1,2}", parts[-1]) and DATE_RE.match(parts[-2]):   # "_301026_1"
        parts.pop()
    if parts and re.fullmatch(r"\d{7}", parts[-1]):      # 5112026 → 051126
        parts[-1] = parts[-1].zfill(8)[:4] + parts[-1][-2:]
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
        if p and p["n"] >= 99000:          # 99999 = số tạm / video bị loại
            p = None
        if not p:
            if re.search(r"\.(mp4|mov|webm)$", r["File"], re.I):
                bad.append(r)
            continue
        social = "social" in (r.get("Path") or r.get("Subfolder") or "").lower()
        k = (r["Game"], "Social" if social else "UA", p["n"])
        v = vids.setdefault(k, {"game": r["Game"], "type": k[1], "n": p["n"], "creator": "", "date": None,
                                "concept": "", "lang": "", "dur": "", "sizes": set(), "files": 0, "links": {}})
        v["files"] += 1
        if p["size"]:
            v["sizes"].add(p["size"])
        lab = RATIO.get(p["size"]) or ratio_of(r.get("Subfolder") or "")
        if lab and r.get("FileId") and lab not in v["links"]:
            v["links"][lab] = r["FileId"]
        for f in ("creator", "concept", "lang", "dur"):
            if p[f] and not v[f]:
                v[f] = p[f]
        created = datetime.date.fromisoformat(r["CreatedTime"][:10])
        d = p["date"]
        if not d or abs((d - created).days) > 45:   # ngày trong tên file gõ sai → lấy ngày upload
            d = created
        if not v["date"] or d < v["date"]:
            v["date"] = d
    vs = sorted(vids.values(), key=lambda v: (v["game"], v["type"], v["n"]))
    w = csv.writer(open(os.path.join(outdir, "drive_videos.csv"), "w", encoding="utf-8", newline=""), lineterminator="\n")
    w.writerow(["Game", "Type", "Creative number", "Creator", "Date", "Concept", "Lang", "Duration", "Sizes", "Files", "Links"])
    for v in vs:
        w.writerow([v["game"], v["type"], v["n"], v["creator"].title(), v["date"].isoformat() if v["date"] else "",
                    v["concept"], v["lang"], v["dur"], " ".join(sorted(v["sizes"])), v["files"],
                    " ".join(f"{k}={v['links'][k]}" for k in ("9x16", "1x1", "16x9", "other") if k in v["links"])])
    mx = defaultdict(lambda: None)
    for v in vs:
        if v["type"] != "UA":                # dãy Social trên Drive không khớp dãy Social trên Notion → bỏ
            continue
        k = (v["game"], v["type"])
        if not mx[k] or v["n"] > mx[k]["n"]:
            mx[k] = v
    today = datetime.date.today().isoformat()
    w = csv.writer(open(os.path.join(outdir, "drive_numbers.csv"), "w", encoding="utf-8", newline=""), lineterminator="\n")
    w.writerow(["Game", "Type", "Max", "Files", "Latest file", "Updated"])
    for (g, t), v in sorted(mx.items()):
        cnt = sum(1 for x in vs if x["game"] == g and x["type"] == t)
        w.writerow([g, t, v["n"], cnt, f"video{v['n']}_{v['concept']}_{v['creator']}", today])
    # Team: theo tên Creator trong data Notion (latest.csv = UA, ai_numbers.csv = AI)
    team = {}
    for fn, t in (("latest.csv", "UA"), ("ai_numbers.csv", "AI")):
        fp = os.path.join(outdir, fn)
        if os.path.exists(fp):
            for r in csv.DictReader(open(fp, encoding="utf-8")):
                for c in (r.get("Creator") or "").split(","):
                    if c.strip():
                        team.setdefault(c.strip().lower(), t)
    months = sorted({v["date"].strftime("%Y-%m") for v in vs})
    by_cm, by_cg = defaultdict(int), defaultdict(int)
    for v in vs:
        c = v["creator"] or "?"
        by_cm[(c, v["date"].strftime("%Y-%m"))] += 1
        by_cg[(c, v["game"])] += 1
    creators = sorted({c for c, _ in by_cm}, key=lambda c: (team.get(c, "?"), -sum(by_cm[(c, m)] for m in months)))
    w = csv.writer(open(os.path.join(outdir, "drive_creator_month.csv"), "w", encoding="utf-8", newline=""), lineterminator="\n")
    w.writerow(["Creator", "Team"] + months + ["Total"])
    for c in creators:
        ns = [by_cm[(c, m)] for m in months]
        w.writerow([c.title(), team.get(c, "?")] + [n or "" for n in ns] + [sum(ns)])
    games = sorted({g for _, g in by_cg})
    w = csv.writer(open(os.path.join(outdir, "drive_creator_game.csv"), "w", encoding="utf-8", newline=""), lineterminator="\n")
    w.writerow(["Creator", "Team"] + games + ["Total"])
    for c in creators:
        ns = [by_cg[(c, g)] for g in games]
        w.writerow([c.title(), team.get(c, "?")] + [n or "" for n in ns] + [sum(ns)])
    # Link thư mục của từng game (drive_folders.csv cùng chỗ với drive_files.csv: Game,GameFolderId,VNFolderId)
    fp = os.path.join(os.path.dirname(src) or ".", "drive_folders.csv")
    if os.path.exists(fp):
        last = {}
        for v in vs:
            g = last.setdefault(v["game"], {"n": 0, "d": None, "c": 0})
            g["c"] += 1
            if v["type"] == "UA" and v["n"] > g["n"]:
                g["n"] = v["n"]
            if not g["d"] or v["date"] > g["d"]:
                g["d"] = v["date"]
        w = csv.writer(open(os.path.join(outdir, "drive_games.csv"), "w", encoding="utf-8", newline=""), lineterminator="\n")
        w.writerow(["Game", "Folder", "Videos folder", "Videos", "Last number", "Last upload"])
        for r in sorted(csv.DictReader(open(fp, encoding="utf-8")), key=lambda r: r["Game"].lower()):
            g = last.get(r["Game"], {})
            w.writerow([r["Game"], r["GameFolderId"], r.get("VNFolderId", ""), g.get("c", 0) or "",
                        g.get("n") or "", g["d"].isoformat() if g.get("d") else ""])
        print(f"Đã ghi link thư mục của {sum(1 for _ in open(fp)) - 1} game vào drive_games.csv")
    print(f"{len(rows)} file → {len(vs)} video (đếm 1 lần mỗi số Creative), {len(mx)} dãy số")
    if bad:
        print(f"{len(bad)} file video không đúng mẫu tên, ví dụ:")
        for r in bad[:10]:
            print("  ", r["Game"], "/", r.get("Path"), "/", r["File"])


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "data")
