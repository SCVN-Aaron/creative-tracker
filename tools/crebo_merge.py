"""Gộp file export Crebo (CVR từng video) với dữ liệu Google Drive -> data/cvr.csv cho tab Performance.

Cách dùng:
  python3 -I tools/crebo_merge.py <crebo_VN_all_boards_YYYY-MM-DD.csv> data

- CVR = cột "AOS CVR (WW)" trên Crebo. "2,80" được hiểu là 2.80; reject / rejected -> Rejected;
  ô có chữ khác (N/A, Not yet tested…) -> Not tested.
- Creator lấy theo Google Drive (data/drive_videos.csv, creator của bản 9x16) khớp theo game + số Creative.
  Không có trên Drive thì lấy tên trên Crebo (chữ đầu), cột Source ghi rõ nguồn.
- Tên game đổi về tên trên Notion (giống dashboard).
"""
import csv, os, re, sys
from pathlib import Path

GAME_ORDER = ["Arrow Fever", "BEHIND", "Bulldozer Master", "Burger Please!", "Captain of Space", "CrabLife",
              "Dino Universe", "Downhill", "Dragon Burst", "Frozia", "Gangster Universe", "Idle Oil Empire", "Lumber Chopper",
              "Match Flight", "Mosquito", "My Fish Shop", "My Supermarket", "Outlets Rush", "Paper Delivery Boy", "Pizza Ready",
              "Prison Life", "Rush Attack", "Shark Universe", "Smash Down", "Snake Clash", "Spin Miner", "Super Big Slime",
              "Suzy's Restaurant", "Target Smash!", "Theme park manager", "Whack Stack", "XP Hero"]
key = lambda s: re.sub(r"[^a-z0-9]", "", str(s).lower())
# tên board Crebo / thư mục Drive khác tên Notion
ALIAS = {"arrowflow": "arrowfever", "downhillracer": "downhill", "slime": "superbigslime", "mosquitoio": "mosquito",
         "aidrama": "aidramahypereels", "hypereels": "aidramahypereels", "dinouniverse": "dinouniverse"}
NOTION = {key(g): g for g in GAME_ORDER}
NOTION["aidramahypereels"] = "AI Drama"


def notion_game(name):
    k = ALIAS.get(key(name), key(name))
    return NOTION.get(k, name.strip())


def cvr(v):
    t = (v or "").strip()
    if re.fullmatch(r"\d+([.,]\d+)?", t):
        return t.replace(",", "."), "Tested"
    if not t:
        return "", ""
    if re.search(r"reject", t, re.I):
        return "", "Rejected"
    return "", "Not tested"


def main():
    src, out = sys.argv[1], Path(sys.argv[2])
    drive = {}
    for v in csv.DictReader(open(out / "drive_videos.csv", encoding="utf-8")):
        k = (ALIAS.get(key(v["Game"]), key(v["Game"])), v["Creative number"])
        if k not in drive or drive[k]["Type"] != "UA":  # cùng số có cả bản UA và Social -> lấy bản UA
            drive[k] = v
    rows, miss = [], 0
    m = re.search(r"(\d{4}-\d{2}-\d{2})", os.path.basename(src))
    exported = m[1] if m else ""
    for r in csv.DictReader(open(src, encoding="utf-8-sig")):
        val, st = cvr(r.get("AOS CVR (WW)"))
        num = (r.get("Creative ID") or "").strip()
        if not st or not re.fullmatch(r"\d{5}", num):
            continue
        g = notion_game(r["Board"])
        d = drive.get((ALIAS.get(key(r["Board"]), key(r["Board"])), num))
        if d:
            creator, source, date = d["Creator"], "Drive", d["Date"]
        else:
            miss += 1
            creator = (r.get("Creator") or "").split()[:1]
            creator = creator[0].capitalize() if creator else ""
            source, date = "Crebo", (r.get("Created At") or "")[:10]
        it = re.sub(r"\D", "", r.get("Iteration") or "")
        ios, _ = cvr(r.get("iOS CVR (WW)"))
        rows.append({"Game": g, "Iteration": it, "Number": num, "CVR": val, "iOS CVR": ios, "Status": st,
                     "Creator": creator, "Source": source, "Date": date,
                     "Top": (r.get("Top Creative (1위 소재)") or "").strip(),
                     "Concept": (r.get("Concept") or r.get("Scenario") or r.get("Title") or "").strip()[:80],
                     "Exported": exported})
    rows.sort(key=lambda x: (x["Game"], int(x["Iteration"] or 0), x["Number"]))
    cols = list(rows[0]) if rows else []
    with open(out / "cvr.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, cols)
        w.writeheader()
        w.writerows(rows)
    tested = sum(r["Status"] == "Tested" for r in rows)
    print(f"{len(rows)} rows ({tested} with CVR) -> {out/'cvr.csv'}; creator from Crebo (not on Drive): {miss}")


if __name__ == "__main__":
    main()
