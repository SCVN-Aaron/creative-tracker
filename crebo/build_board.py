"""Dựng lại bảng Crebo từ chữ copy trên Table view (Ctrl+A, Ctrl+C) — Crebo chưa có export.

Cách dùng:
  1. Mở board trên Crebo (Table view), Ctrl+A, Ctrl+C, dán vào 1 file .txt trong crebo/raw/
  2. python3 -I crebo/build_board.py crebo/xphero_board.csv crebo/raw/*.txt
     -> gộp các dòng mới vào CSV (trùng số thì lấy bản dán sau), ghi lại CSV + HTML cùng tên.
"""
import csv, html, re, sys
from collections import defaultdict
from pathlib import Path

COLS = ["No", "Iteration", "Asset", "Versions", "AOS CVR (WW)", "Status", "Creator", "Concept",
        "Description", "PM", "Code", "Lang", "Duration", "Size", "Date", "Note"]
FN = re.compile(r"video(\d+)_([a-z0-9]+)_([a-z]+)_(\d+s)_(\d+x\d+)_([a-z0-9]+)_(\d\d)(\d\d)(\d\d)\.mp4", re.I)
NUM = re.compile(r"^\d{5}$")
ITER = re.compile(r"^\d+(st|nd|rd|th)$")
CMT = re.compile(r"^\d+ comments?$")


def from_file(f, row):
    m = FN.match(f or "")
    if m:
        row.update(Code=m[2], Lang=m[3], Duration=m[4], Size=m[5], Date=f"20{m[9]}-{m[8]}-{m[7]}")
    return row


def status(cvr, asset):
    t = cvr.strip()
    if re.fullmatch(r"\d+(\.\d+)?", t):
        return t, "Tested", ""
    if t.lower() == "rejected":
        return "", "Rejected", ""
    if t:
        return "", "Not tested", t
    return "", ("Pending" if asset else "Empty"), ""


def parse(text):
    L = [x.strip() for x in text.splitlines()]
    starts = [i for i, x in enumerate(L) if NUM.match(x) and any(ITER.match(y) for y in L[i + 1:i + 4] if y)]
    out = []
    for k, s in enumerate(starts):
        blk = L[s:starts[k + 1] if k + 1 < len(starts) else len(L)]
        ne = [x for x in blk if x]
        no, it = ne[0], ne[1]
        rest = ne[2:]
        if rest and rest[0] == "Add":
            rest = rest[1:]
        ci = next((i for i, x in enumerate(rest) if CMT.match(x)), None)
        assets = rest[:ci] if ci is not None else []
        rest = rest[ci + 1:] if ci is not None else rest
        rest = [x for x in rest if x not in ("Add", "Add new item", "Command palette", "Search boards or actions")]
        people = [i for i in range(len(rest) - 1) if rest[i] == rest[i + 1]]
        cvr, creator, pm, mid = "", "", "", []
        if people:
            p0 = people[0]
            cvr = " ".join(rest[:p0])
            creator = rest[p0]
            p1 = next((i for i in people if i > p0 + 1), None)
            if p1 is not None:
                pm, mid = rest[p1], rest[p0 + 2:p1]
            else:
                mid = rest[p0 + 2:]
        else:
            cvr = " ".join(rest)
        asset = re.sub(r"V\d+$", "", assets[0]) if assets else ""
        v, st, note = status(cvr, asset)
        out.append(from_file(asset, dict(No=no, Iteration=it, Asset=asset, Versions=str(len(assets)) if len(assets) > 1 else "",
                                          **{"AOS CVR (WW)": v}, Status=st, Creator=creator,
                                          Concept=mid[0] if mid else "", Description=" · ".join(mid[1:]),
                                          PM=pm, Note=note)))
    return out


def load(p):
    if not Path(p).exists():
        return {}
    rows = {}
    for r in csv.DictReader(open(p, encoding="utf-8")):
        if "File" in r:  # bản CSV cũ
            r = from_file(r["File"], dict(No=r["No"], Iteration=r["Iteration"], Asset=r["File"], Creator=r["Creator"],
                                          **{"AOS CVR (WW)": r["AOS CVR (WW)"]},
                                          Status="Rejected" if r["Status"] == "Rejected" else "Tested"))
        rows[r["No"]] = {c: r.get(c, "") for c in COLS}
    return rows


def esc(x):
    return html.escape(x or "")


def page(rows, title):
    sc = [float(r["AOS CVR (WW)"]) for r in rows if r["AOS CVR (WW)"]]
    lo, hi = (min(sc), max(sc)) if sc else (0, 1)
    nums = sorted(int(r["No"]) for r in rows)
    gaps = [(a + 1, b - 1) for a, b in zip(nums, nums[1:]) if b - a > 1]
    badge = {"Rejected": "rej", "Not tested": "nt", "Pending": "pend", "Empty": "pend"}

    def cvr(r):
        if r["AOS CVR (WW)"]:
            a = .12 + .55 * ((float(r["AOS CVR (WW)"]) - lo) / ((hi - lo) or 1))
            return f'<td class="num" style="background:rgba(34,160,90,{a:.2f})">{r["AOS CVR (WW)"]}</td>'
        t = {"Rejected": "REJECTED", "Not tested": "NOT TESTED", "Pending": "—", "Empty": "—"}[r["Status"]]
        return f'<td><span class="b {badge[r["Status"]]}" title="{esc(r["Note"])}">{t}</span></td>'

    def person(n):
        return f'<span class="av">{"".join(x[0] for x in n.split())}</span>{esc(n)}' if n else ""

    def it(r):
        n = int(re.sub(r"\D", "", r["Iteration"]) or 0)
        return f'<span class="it" style="--h:{(n * 47) % 360}">{esc(r["Iteration"])}</span>'

    def ver(r):
        return f' <span class="v">{r["Versions"]} bản</span>' if r["Versions"] else ""

    body = "".join(
        f'<tr><td class="mono">{r["No"]}</td><td>{it(r)}</td>'
        f'<td class="file">{esc(r["Asset"]) or "—"}{ver(r)}</td>'
        f'{cvr(r)}<td>{person(r["Creator"])}</td><td>{esc(r["Concept"])}</td><td class="desc">{esc(r["Description"])}</td>'
        f'<td>{person(r["PM"])}</td><td class="num">{esc(r["Duration"])}</td><td class="num">{esc(r["Date"])}</td></tr>'
        for r in rows)
    S = defaultdict(lambda: [0, 0, 0, []])
    for r in rows:
        if not r["Creator"]:
            continue
        s = S[(r["Iteration"], r["Creator"])]
        s[0] += 1
        s[1] += r["Status"] == "Rejected"
        s[2] += r["Status"] in ("Not tested", "Pending")
        if r["AOS CVR (WW)"]:
            s[3].append(float(r["AOS CVR (WW)"]))
    it_n = lambda k: int(re.sub(r"\D", "", k) or 0)
    summ = "".join(
        f'<tr><td>{esc(i)}</td><td>{esc(c)}</td><td class="num">{v[0]}</td><td class="num">{len(v[3])}</td>'
        f'<td class="num">{v[1]}</td><td class="num">{v[2]}</td><td class="num">{(sum(v[3]) / len(v[3])):.2f}</td></tr>'
        if v[3] else
        f'<tr><td>{esc(i)}</td><td>{esc(c)}</td><td class="num">{v[0]}</td><td class="num">0</td>'
        f'<td class="num">{v[1]}</td><td class="num">{v[2]}</td><td class="num">—</td></tr>'
        for (i, c), v in sorted(S.items(), key=lambda x: (it_n(x[0][0]), x[0][1])))
    gap = ("Chưa có dữ liệu: " + ", ".join(f"{a}–{b}" if a != b else str(a) for a, b in gaps)) if gaps else ""
    return f"""<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{esc(title)}</title>
<style>
:root{{--bg:#f6f7f9;--panel:#fff;--ink:#1b1f27;--ink2:#667085;--line:#e4e7ec;--soft:#f2f4f7}}
@media (prefers-color-scheme:dark){{:root{{--bg:#0f1115;--panel:#171a21;--ink:#e8eaee;--ink2:#98a2b3;--line:#2a2f3a;--soft:#1f232c}}}}
body{{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 system-ui,-apple-system,"Segoe UI",sans-serif}}
.wrap{{max-width:1480px;margin:0 auto;padding:24px 16px}}
h1{{font-size:22px;margin:0 0 4px}} .sub{{color:var(--ink2);margin:0 0 18px}} .gap{{color:#b54708}}
.card{{background:var(--panel);border:1px solid var(--line);border-radius:12px;overflow:auto;margin-bottom:18px}}
table{{border-collapse:collapse;width:100%;min-width:760px}}
th,td{{padding:9px 12px;border-bottom:1px solid var(--line);text-align:left;white-space:nowrap}}
th{{background:var(--soft);font-size:12px;color:var(--ink2);font-weight:600;position:sticky;top:0}}
th.num{{text-align:right}} tr:last-child td{{border-bottom:0}}
.num,.mono{{font-variant-numeric:tabular-nums}} .num{{text-align:right}}
.mono{{font-family:ui-monospace,Menlo,monospace;font-weight:600}}
.file{{font-family:ui-monospace,Menlo,monospace;font-size:12px;color:var(--ink2)}}
.desc{{white-space:normal;min-width:220px;max-width:340px;color:var(--ink2);font-size:13px}}
.it{{display:inline-block;padding:2px 8px;border-radius:999px;font-size:12px;font-weight:600;background:hsl(var(--h) 85% 93%);color:hsl(var(--h) 65% 38%)}}
.v{{font-family:system-ui;font-size:11px;font-weight:700;padding:1px 5px;border-radius:4px;background:var(--soft);border:1px solid var(--line)}}
.b{{display:inline-block;padding:2px 8px;border-radius:6px;font-size:11.5px;font-weight:700}}
.rej{{background:#fee4e2;color:#b42318}} .nt{{background:#fef0c7;color:#b54708}} .pend{{color:var(--ink2)}}
.av{{display:inline-grid;place-items:center;width:22px;height:22px;border-radius:50%;background:var(--soft);border:1px solid var(--line);font-size:10px;font-weight:700;margin-right:7px}}
h2{{font-size:15px;margin:0;padding:12px 14px;border-bottom:1px solid var(--line)}}
</style></head><body><div class="wrap">
<h1>{esc(title)}</h1><p class="sub">Dựng lại từ Crebo (Table view) · {len(rows)} dòng · {sum(r["Status"] == "Tested" for r in rows)} đã có AOS CVR (WW) · <span class="gap">{gap}</span></p>
<div class="card"><table><thead><tr><th>#</th><th>Iteration</th><th>Asset</th><th class="num">AOS CVR (WW)</th><th>Creator</th><th>Concept</th><th>Description</th><th>PM</th><th class="num">Dur.</th><th class="num">Date</th></tr></thead><tbody>{body}</tbody></table></div>
<div class="card"><h2>Tổng hợp theo iteration × creator</h2><table><thead><tr><th>Iteration</th><th>Creator</th><th class="num">Video</th><th class="num">Có CVR</th><th class="num">Rejected</th><th class="num">Chưa test</th><th class="num">AOS CVR TB</th></tr></thead><tbody>{summ}</tbody></table></div>
</div></body></html>"""


def main():
    out, raws = sys.argv[1], sys.argv[2:]
    rows = load(out)
    for p in raws:
        for r in parse(Path(p).read_text(encoding="utf-8")):
            rows[r["No"]] = {c: r.get(c, "") for c in COLS}
    rows = [rows[k] for k in sorted(rows, key=int)]
    with open(out, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, COLS)
        w.writeheader()
        w.writerows(rows)
    Path(out).with_suffix(".html").write_text(page(rows, "XP Hero — Crebo board"), encoding="utf-8")
    print(f"{len(rows)} rows -> {out}")


if __name__ == "__main__":
    main()
