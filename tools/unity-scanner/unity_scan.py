#!/usr/bin/env python3
"""
Unity Creative Scanner — quét một project Unity nhỏ (playable ad) và xuất báo cáo
cho người quay creative: phím tắt, joystick, skin, texture/material, âm thanh.

Không cần mở Unity, không cần cài thêm gì (chỉ thư viện chuẩn Python 3.8+).

    python3 unity_scan.py "D:/Projects/MyPlayable"            # ghi báo cáo vào reports/<tên project>/
    python3 unity_scan.py "D:/Projects/MyPlayable" --open     # ghi xong mở luôn trên trình duyệt
    python3 unity_scan.py "D:/Projects/MyPlayable" --out ./out

Kết quả:
    creative-scan.html  — báo cáo đọc trên trình duyệt (có ô tìm kiếm)
    creative-scan.md    — bản Markdown, dán thẳng cho AI khi cần hỏi thêm
"""

import argparse
import bisect
import html
import json
import os
import re
import struct
import sys
import wave
import webbrowser
from collections import defaultdict
from datetime import datetime
from pathlib import Path

# ---------------------------------------------------------------------------
# Bảng tra
# ---------------------------------------------------------------------------

SKIP_DIRS = {"Library", "Temp", "Logs", "obj", "Build", "Builds", ".git", ".vs", ".idea", "UserSettings"}
VENDOR_PARTS = {"plugins", "thirdparty", "third party", "3rdparty", "textmesh pro", "textmeshpro", "demigiant",
                "dotween", "joystick pack", "samples", "sample", "demo", "demos", "examples", "example",
                "packages", "lunaplugin", "luna", "nicevibrations", "feel", "odin", "sirenix"}

IMAGE_EXT = {".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".exr", ".hdr", ".gif"}
AUDIO_EXT = {".wav", ".mp3", ".ogg", ".aif", ".aiff", ".flac"}
YAML_EXT = {".unity", ".prefab", ".mat", ".asset", ".controller", ".anim", ".overrideController",
            ".playable", ".spriteatlas", ".mask", ".physicMaterial", ".physicsMaterial2D", ".lighting"}

KEYCODES = {8: "Backspace", 9: "Tab", 12: "Clear", 13: "Return", 19: "Pause", 27: "Escape", 32: "Space",
            39: "Quote", 44: "Comma", 45: "Minus", 46: "Period", 47: "Slash", 59: "Semicolon", 61: "Equals",
            91: "LeftBracket", 92: "Backslash", 93: "RightBracket", 96: "BackQuote", 127: "Delete",
            266: "KeypadPeriod", 267: "KeypadDivide", 268: "KeypadMultiply", 269: "KeypadMinus",
            270: "KeypadPlus", 271: "KeypadEnter", 272: "KeypadEquals", 273: "UpArrow", 274: "DownArrow",
            275: "RightArrow", 276: "LeftArrow", 277: "Insert", 278: "Home", 279: "End", 280: "PageUp",
            281: "PageDown", 300: "Numlock", 301: "CapsLock", 302: "ScrollLock", 303: "RightShift",
            304: "LeftShift", 305: "RightControl", 306: "LeftControl", 307: "RightAlt", 308: "LeftAlt",
            309: "RightCommand", 310: "LeftCommand", 311: "LeftWindows", 312: "RightWindows"}
KEYCODES.update({48 + i: "Alpha%d" % i for i in range(10)})
KEYCODES.update({97 + i: chr(65 + i) for i in range(26)})
KEYCODES.update({256 + i: "Keypad%d" % i for i in range(10)})
KEYCODES.update({282 + i: "F%d" % (i + 1) for i in range(15)})
KEYCODES.update({323 + i: "Mouse%d" % i for i in range(7)})

KEY_WORDS = {"Return": "Enter", "Escape": "Esc", "Space": "Space", "UpArrow": "↑", "DownArrow": "↓",
             "LeftArrow": "←", "RightArrow": "→", "Mouse0": "Chuột trái", "Mouse1": "Chuột phải",
             "Mouse2": "Chuột giữa", "LeftShift": "Shift trái", "RightShift": "Shift phải",
             "LeftControl": "Ctrl trái", "RightControl": "Ctrl phải", "LeftAlt": "Alt trái",
             "RightAlt": "Alt phải", "BackQuote": "`", "Minus": "-", "Equals": "=", "Comma": ",",
             "Period": ".", "Slash": "/", "Backslash": "\\", "LeftBracket": "[", "RightBracket": "]",
             "Semicolon": ";", "Quote": "'", "KeypadPlus": "Numpad +", "KeypadMinus": "Numpad -",
             "KeypadEnter": "Numpad Enter", "PageUp": "Page Up", "PageDown": "Page Down"}

# Shader hay gặp mà file nằm trong package/built-in (không có trong Assets)
KNOWN_SHADERS = {"933532a4fcc9baf4fa0491de14d08ed7": "Universal Render Pipeline/Lit",
                 "8d2bb70cbf9db8d4da26e15b26e74248": "Universal Render Pipeline/Simple Lit",
                 "650dd9526735d5b46b79224bc6e94025": "Universal Render Pipeline/Unlit",
                 "0406db5a14f94604a8c57ccfbc9f3b46": "Universal Render Pipeline/Particles/Unlit",
                 "b7839dad95683814aa64166edc107ae2": "Universal Render Pipeline/Particles/Lit",
                 "e260cfa7296ee7642b167f1eb5be5023": "Universal Render Pipeline/2D/Sprite-Lit-Default"}
BUILTIN_SHADER_IDS = {"46": "Standard", "45": "Standard (Specular)", "10752": "Unlit/Texture",
                      "10753": "Sprites/Default", "10754": "UI/Default", "10755": "Unlit/Color",
                      "10720": "Mobile/Diffuse", "200": "Particles/Standard Unlit", "203": "Legacy/Particles"}

UNITY_LIFECYCLE = {"Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable",
                   "OnDestroy", "OnValidate", "Reset", "OnApplicationPause", "OnApplicationQuit",
                   "OnApplicationFocus", "OnDrawGizmos", "OnDrawGizmosSelected", "OnGUI"}
UNITY_ACTION_CALLBACKS = {"OnTriggerEnter", "OnTriggerEnter2D", "OnCollisionEnter", "OnCollisionEnter2D",
                          "OnMouseDown", "OnMouseUp", "OnPointerDown", "OnPointerUp", "OnPointerClick",
                          "OnBeginDrag", "OnEndDrag", "OnDrop", "OnParticleCollision"}
ACTION_TOKENS = {"jump", "attack", "shoot", "fire", "collect", "pick", "pickup", "hit", "damage", "hurt",
                 "die", "death", "dead", "kill", "win", "victory", "lose", "fail", "gameover", "complete",
                 "finish", "spawn", "upgrade", "levelup", "buy", "purchase", "unlock", "merge", "tap",
                 "click", "swipe", "throw", "explode", "explosion", "break", "smash", "slash", "swing",
                 "dash", "land", "eat", "coin", "coins", "gem", "gems", "reward", "score", "combo", "reveal",
                 "place", "build", "craft", "equip", "reload", "cut", "slice", "pop", "match", "grab",
                 "punch", "kick", "shot", "launch", "boost", "heal", "gain", "earn", "sell", "levelup", "smash"}
NOT_ACTION_PREFIX = {"get", "is", "has", "can", "load", "save", "init", "initialize", "setup", "find", "check",
                     "calc", "calculate", "refresh", "should", "try", "compute", "insert", "any", "reset", "clear"}
SKIN_TOKENS = {"skin", "skins", "outfit", "outfits", "costume", "costumes", "cosmetic", "cosmetics", "hat",
               "hats", "helmet", "cloth", "clothes", "appearance", "variant", "variants", "theme", "themes",
               "palette", "colorway", "avatar", "avatars", "character", "characters", "hero", "heroes",
               "weapon", "weapons", "hair", "face", "mask", "suit", "armor", "armour", "car", "cars", "vehicle"}
JOYSTICK_RE = re.compile(r"joystick|joy[_ ]?stick|onscreenstick|virtualstick|thumbstick|touchstick|"
                         r"analogstick|\bjoy\b|stickcontroller", re.I)
HIDE_UI_RE = re.compile(r"\b(hand|tutorial|tut|finger|pointer|cursor|cta|download|install|logo|end ?card|"
                        r"packshot|banner|fps|debug|playnow|play ?now|store|watermark|arrow|hint|guide)\b", re.I)
ENV_RE = re.compile(r"ground|floor|terrain|map|road|grass|level|env|background|\bbg\b|sky|wall|water|sand|"
                    r"tile|stage|arena|world|field|street|island", re.I)
SOUND_IN_BODY_RE = re.compile(r"PlayOneShot|PlayClipAtPoint|PlaySound|PlaySfx|PlaySFX|PlayFx|PlayAudio|"
                              r"\w*(?:audio|sound|sfx)\w*\s*\.\s*\w*Play", re.I)
VFX_IN_BODY_RE = re.compile(r"particle|vfx|\bfx\b|effect|\.Emit\s*\(", re.I)
NOT_CALLS = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return", "new", "nameof",
             "typeof", "sizeof", "default", "GetKeyDown", "GetKey", "GetKeyUp", "GetMouseButtonDown",
             "GetMouseButton", "GetMouseButtonUp", "GetButtonDown", "GetButton", "GetButtonUp", "Log",
             "LogWarning", "LogError", "GetComponent", "base", "this"}
CS_KEYWORDS = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return", "new",
               "else", "do", "try", "finally", "get", "set", "add", "remove", "when", "nameof", "typeof",
               "default", "sizeof", "checked", "unchecked", "delegate", "init"}


def split_tokens(name):
    """camelCase / snake_case → các từ viết thường."""
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", name)
    s = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1 \2", s)
    return [t.lower() for t in re.split(r"[\s_\-\d]+", s) if t]


def pretty_keycode(k):
    m = re.fullmatch(r"Alpha(\d)", k)
    if m:
        return m.group(1)
    m = re.fullmatch(r"Keypad(\d)", k)
    if m:
        return "Numpad " + m.group(1)
    return KEY_WORDS.get(k, k)


def pretty_keyname(s):
    """Tên phím dạng chuỗi (Input.GetKey("left shift"), Key.Space, <Keyboard>/digit1)."""
    s = s.strip()
    s = s.split("/")[-1] if "/" in s else s
    low = s.lower().replace(" ", "")
    m = re.fullmatch(r"(?:digit|alpha)?(\d)", low)
    if m:
        return m.group(1)
    m = re.fullmatch(r"(?:numpad|keypad)(\d)", low)
    if m:
        return "Numpad " + m.group(1)
    table = {"space": "Space", "enter": "Enter", "return": "Enter", "escape": "Esc", "esc": "Esc",
             "up": "↑", "down": "↓", "left": "←", "right": "→", "uparrow": "↑", "downarrow": "↓",
             "leftarrow": "←", "rightarrow": "→", "leftshift": "Shift trái", "rightshift": "Shift phải",
             "leftctrl": "Ctrl trái", "rightctrl": "Ctrl phải", "leftcontrol": "Ctrl trái",
             "rightcontrol": "Ctrl phải", "leftalt": "Alt trái", "rightalt": "Alt phải", "tab": "Tab",
             "backspace": "Backspace", "backquote": "`", "leftbutton": "Chuột trái",
             "rightbutton": "Chuột phải", "middlebutton": "Chuột giữa", "mouse0": "Chuột trái",
             "mouse1": "Chuột phải", "mouse2": "Chuột giữa", "press": "Chạm/nhấn"}
    if low in table:
        return table[low]
    if len(low) == 1:
        return low.upper()
    if re.fullmatch(r"f\d{1,2}", low):
        return low.upper()
    return s[:1].upper() + s[1:]


def rel(path, root):
    try:
        return Path(path).relative_to(root).as_posix()
    except ValueError:
        return Path(path).as_posix()


def is_vendor(relpath):
    parts = [p.lower() for p in Path(relpath).parts[:-1]]
    return any(p in VENDOR_PARTS for p in parts)


def is_editor_script(relpath):
    return any(p == "Editor" for p in Path(relpath).parts[:-1])


def read_text(path):
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return ""


# ---------------------------------------------------------------------------
# C#: bỏ comment, theo dõi class/method để biết mỗi dòng thuộc hàm nào
# ---------------------------------------------------------------------------

def clean_cs(src):
    """Trả (code, skel) cùng độ dài với src.
    code: comment thay bằng khoảng trắng, giữ chuỗi.
    skel: như code nhưng nội dung chuỗi/ký tự cũng bị xoá — dùng để đếm ngoặc."""
    code, skel = [], []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = src.find("\n", i)
            j = n if j < 0 else j
            blank = " " * (j - i)
            code.append(blank); skel.append(blank); i = j
        elif c == "/" and nxt == "*":
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            blank = re.sub(r"[^\n]", " ", src[i:j])
            code.append(blank); skel.append(blank); i = j
        elif c == "#" and src[src.rfind("\n", 0, i) + 1:i].strip() == "":
            j = src.find("\n", i)
            j = n if j < 0 else j
            code.append(src[i:j]); skel.append(" " * (j - i)); i = j
        elif c in "@$" and nxt == '"' or (c in "@$" and nxt in "@$" and i + 2 < n and src[i + 2] == '"'):
            start = i
            verbatim = "@" in src[i:i + 2]
            i = src.index('"', i) + 1
            while i < n:
                if verbatim and src[i] == '"' and i + 1 < n and src[i + 1] == '"':
                    i += 2; continue
                if not verbatim and src[i] == "\\":
                    i += 2; continue
                if src[i] == '"' or (not verbatim and src[i] == "\n"):
                    break
                i += 1
            i = min(i + 1, n)
            piece = src[start:i]
            code.append(piece)
            skel.append(re.sub(r"[^\n]", " ", piece[:-1]) + piece[-1:])
        elif c == '"' or c == "'":
            q, start = c, i
            i += 1
            while i < n and src[i] != q and src[i] != "\n":
                i += 2 if src[i] == "\\" else 1
            i = min(i + 1, n)
            piece = src[start:i]
            code.append(piece)
            skel.append(q + " " * max(0, len(piece) - 2) + (piece[-1] if len(piece) > 1 else ""))
        else:
            code.append(c); skel.append(c); i += 1
    return "".join(code), "".join(skel)


class CsFile:
    def __init__(self, path, relpath):
        self.path, self.rel = path, relpath
        self.src = read_text(path)
        self.code, self.skel = clean_cs(self.src)
        self.lines = self.src.split("\n")
        self.nl = [m.start() for m in re.finditer("\n", self.src)]
        self.vendor = is_vendor(relpath)
        self.editor = is_editor_script(relpath)
        self.classes, self.methods = [], []
        self._fields = None
        self._scan_scopes()
        self._scan_preproc()
        self.guid = None

    def line_of(self, pos):
        return bisect.bisect_right(self.nl, pos - 1) + 1

    def _classify(self, header):
        h = re.sub(r"^\s*(\[[^\]]*\]\s*)+", "", header).strip()
        if not h:
            return "block", None, 0
        pre = h.split("(")[0]
        m = re.search(r"\b(class|struct|interface|enum|record)\s+(\w+)", pre)
        if m:
            base = None
            mb = re.search(r":\s*([\w\.]+)", h[m.end():])
            if mb:
                base = mb.group(1).split(".")[-1]
            return "class", (m.group(2), base), header.find(m.group(2))
        if re.match(r"namespace\b", h):
            return "ns", None, 0
        m = re.search(r"(\w+)\s*(?:<[^<>]*(?:<[^<>]*>[^<>]*)*>)?\s*\((?:[^()]|\([^()]*\))*\)\s*"
                      r"(?:where\b[^{]*|:\s*(?:base|this)\s*\((?:[^()]|\([^()]*\))*\))?\s*$", h, re.S)
        if m and m.group(1) not in CS_KEYWORDS:
            before = h[:m.start(1)]
            if re.search(r"\bnew\s*$|[=.]\s*$|\breturn\s*$", before):
                return "block", None, 0
            return "method", m.group(1), header.rfind(m.group(1), 0, header.rfind("("))
        return "block", None, 0

    def _scan_scopes(self):
        stack, hs = [], 0
        for i, ch in enumerate(self.skel):
            if ch == "{":
                kind, name, off = self._classify(self.skel[hs:i])
                cls = next((s["name"][0] for s in reversed(stack) if s["kind"] == "class"), None)
                stack.append({"kind": kind, "name": name, "start": i, "name_pos": hs + max(off, 0), "cls": cls})
                hs = i + 1
            elif ch == "}":
                if stack:
                    s = stack.pop()
                    if s["kind"] == "method":
                        self.methods.append({"name": s["name"], "cls": s["cls"], "start": s["start"], "end": i,
                                             "line": self.line_of(s["name_pos"]),
                                             "end_line": self.line_of(i)})
                    elif s["kind"] == "class":
                        self.classes.append({"name": s["name"][0], "base": s["name"][1], "start": s["start"],
                                             "end": i, "line": self.line_of(s["name_pos"])})
                hs = i + 1
            elif ch == ";":
                hs = i + 1
        self.methods.sort(key=lambda m: m["start"])
        self.classes.sort(key=lambda c: c["start"])

    def _scan_preproc(self):
        """Đánh dấu các dòng nằm trong #if UNITY_EDITOR (phím chỉ chạy trong Editor = phím dành cho người quay)."""
        self.editor_only = set()
        stack = []
        for ln, line in enumerate(self.lines, 1):
            t = line.strip()
            if t.startswith("#if"):
                stack.append(t[3:].strip())
            elif t.startswith("#elif"):
                if stack:
                    stack[-1] = "else"
            elif t.startswith("#else"):
                if stack:
                    stack[-1] = "else"
            elif t.startswith("#endif"):
                if stack:
                    stack.pop()
            elif any(c != "else" and re.search(r"(?<!!)\bUNITY_EDITOR\b", c) for c in stack):
                self.editor_only.add(ln)

    def method_at(self, pos):
        best = None
        for m in self.methods:
            if m["start"] <= pos <= m["end"] and (best is None or m["start"] > best["start"]):
                best = m
        return best

    def class_at(self, pos):
        best = None
        for c in self.classes:
            if c["start"] <= pos <= c["end"] and (best is None or c["start"] > best["start"]):
                best = c
        return best

    def body(self, method):
        return self.code[method["start"]:method["end"] + 1]

    def main_class(self):
        stem = Path(self.rel).stem
        for c in self.classes:
            if c["name"] == stem:
                return c
        return self.classes[0] if self.classes else None

    def fields(self):
        """Trường khai báo trực tiếp trong class (không nằm trong hàm)."""
        if self._fields is not None:
            return self._fields
        out = self._fields = []
        rx = re.compile(r"(?m)^[ \t]*((?:\[[^\]\n]*\][ \t]*)*)((?:(?:public|private|protected|internal|static|"
                        r"readonly|new)\s+)*)([A-Za-z_][\w\.]*(?:<[^;=(){}]*?>)?(?:\[\])?)\s+([A-Za-z_]\w*)"
                        r"\s*(?:=\s*([^;{}]+))?;")
        for m in rx.finditer(self.skel):
            pos = m.start(4)
            if self.method_at(pos) or not self.class_at(pos):
                continue
            typ = m.group(3)
            if typ in ("return", "using", "var", "const", "event", "goto", "throw", "yield"):
                continue
            attrs, mods = m.group(1) or "", m.group(2) or ""
            serialized = "public" in mods or "SerializeField" in attrs
            if "static" in mods:
                serialized = False
            init = self.code[m.start(5):m.end(5)].strip() if m.group(5) else None
            out.append({"type": typ, "name": m.group(4), "line": self.line_of(pos), "init": init,
                        "serialized": serialized, "cls": self.class_at(pos)["name"]})
        return out


def find_condition_body(fileobj, pos):
    """Từ vị trí lời gọi Input.* (thường nằm trong if (...)), lấy thân câu lệnh được thực thi."""
    skel, n = fileobj.skel, len(fileobj.skel)
    depth, i = 0, pos
    while i < n:
        ch = skel[i]
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth < 0:
                break
        elif ch in ";{}" and depth == 0:
            return None
        i += 1
    if i >= n:
        return None
    j = i + 1
    while j < n and skel[j].isspace():
        j += 1
    if j >= n:
        return None
    if skel[j] == "{":
        d, k = 0, j
        while k < n:
            if skel[k] == "{":
                d += 1
            elif skel[k] == "}":
                d -= 1
                if d == 0:
                    break
            k += 1
        return fileobj.code[j:k + 1][:2000]
    k = skel.find(";", j)
    return fileobj.code[j:(k + 1 if k > 0 else n)][:600]


def describe_body(body):
    if not body:
        return [], None
    calls = []
    for m in re.finditer(r"([A-Za-z_][\w\.]*)\s*\(", body):
        name = m.group(1)
        last = name.split(".")[-1]
        if last in NOT_CALLS or last in CS_KEYWORDS or name.startswith("Debug."):
            continue
        label = name + "()"
        if label not in calls:
            calls.append(label)
    log = None
    m = re.search(r"Debug\.Log\w*\s*\(\s*\$?\"([^\"]*)\"", body)
    if m:
        log = m.group(1)
    if not calls and not log:
        stmt = re.split(r";|\belse\b", body.strip().strip("{}"))[0]
        stmt = " ".join(stmt.split())
        if stmt:
            calls = [stmt[:70]]
    return calls[:6], log


# ---------------------------------------------------------------------------
# Unity YAML (scene, prefab, material, asset)
# ---------------------------------------------------------------------------

DOC_RE = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?[^\n]*\n", re.M)
REF_RE = re.compile(r"\{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?(?:, type: \d+)?\}")
GUID_RE = re.compile(r"guid: ([0-9a-f]{32})")


def parse_unity_yaml(text):
    docs = {}
    ms = list(DOC_RE.finditer(text))
    for k, m in enumerate(ms):
        end = ms[k + 1].start() if k + 1 < len(ms) else len(text)
        body = text[m.end():end]
        docs[m.group(2)] = {"cls": int(m.group(1)), "id": m.group(2), "stripped": bool(m.group(3)), "body": body}
    return docs


def yget(body, key):
    m = re.search(r"(?m)^\s*" + re.escape(key) + r": ?(.*)$", body)
    return m.group(1).strip() if m else None


def yref(body, key):
    v = yget(body, key)
    if not v:
        return None, None
    m = REF_RE.search(v)
    return (m.group(1), m.group(2)) if m else (None, None)


def mono_fields(body):
    """Các trường top-level của một MonoBehaviour: tên → (giá trị thô, [guid tham chiếu])."""
    fields, cur = {}, None
    for line in body.split("\n"):
        m = re.match(r"^  ([A-Za-z_]\w*):\s?(.*)$", line)
        if m:
            cur = m.group(1)
            fields[cur] = [m.group(2), GUID_RE.findall(m.group(2))]
            continue
        if cur and (line.startswith("  - ") or line.startswith("    ")):
            fields[cur][1].extend(GUID_RE.findall(line))
    return fields


class UnityDoc:
    """Một file .unity hoặc .prefab đã parse: GameObject, component, đường dẫn hierarchy."""

    def __init__(self, path, relpath):
        self.path, self.rel = path, relpath
        self.docs = parse_unity_yaml(read_text(path))
        self.go_name, self.go_active, self.go_comps = {}, {}, defaultdict(list)
        self.tr_go, self.tr_father, self.go_tr = {}, {}, {}
        self.instances = {}
        for d in self.docs.values():
            b, c = d["body"], d["cls"]
            if c == 1001:
                src = yref(b, "m_SourcePrefab")[1]
                parent = yref(b, "m_TransformParent")[0]
                name = None
                mm = re.search(r"propertyPath: m_Name\s*\n\s*value: ([^\n]*)", b)
                if mm:
                    name = mm.group(1).strip()
                self.instances[d["id"]] = {"src": src, "parent": parent, "name": name, "body": b}
        for d in self.docs.values():
            b, c, i = d["body"], d["cls"], d["id"]
            if c == 1 and not d["stripped"]:
                self.go_name[i] = (yget(b, "m_Name") or "").strip()
                self.go_active[i] = yget(b, "m_IsActive") != "0"
            elif c in (4, 224):
                go = yref(b, "m_GameObject")[0]
                father = yref(b, "m_Father")[0]
                self.tr_go[i] = go
                self.tr_father[i] = father
                if go and go != "0":
                    self.go_tr[go] = i
                if d["stripped"]:
                    pi = yref(b, "m_PrefabInstance")[0] or yref(b, "m_PrefabInternal")[0]
                    self.tr_go[i] = ("inst", pi)
            if c not in (1, 4, 224, 1001) and not d["stripped"]:
                go = yref(b, "m_GameObject")[0]
                if go:
                    self.go_comps[go].append(d)

    def tr_name(self, tr):
        go = self.tr_go.get(tr)
        if isinstance(go, tuple):
            inst = self.instances.get(go[1], {})
            return inst.get("name") or ("(prefab) " + (inst.get("src_name") or "?")), inst.get("parent")
        return self.go_name.get(go, "?"), self.tr_father.get(tr)

    def path_of(self, go):
        names, tr, guard = [], self.go_tr.get(go), 0
        if tr is None:
            return self.go_name.get(go, "?")
        while tr and tr != "0" and guard < 64:
            name, father = self.tr_name(tr)
            names.append(name)
            tr, guard = father, guard + 1
        return "/".join(reversed(names))

    def instance_path(self, inst_id):
        inst = self.instances[inst_id]
        names = [inst.get("name") or inst.get("src_name") or "?"]
        tr, guard = inst.get("parent"), 0
        while tr and tr != "0" and guard < 64:
            name, father = self.tr_name(tr)
            names.append(name)
            tr, guard = father, guard + 1
        return "/".join(reversed(names))


# ---------------------------------------------------------------------------
# Quét project
# ---------------------------------------------------------------------------

class Project:
    def __init__(self, root):
        self.root = Path(root).resolve()
        self.guid_path = {}
        self.files = defaultdict(list)
        self.refs = defaultdict(set)       # guid → các file tham chiếu tới nó
        self.cs = {}
        self.class_file = {}
        self.docs = {}
        self.notes = []

    # -- index --------------------------------------------------------------
    def walk(self):
        bases = [self.root / "Assets"]
        pk = self.root / "Packages"
        if pk.is_dir():
            bases += [p for p in pk.iterdir() if p.is_dir() and (p / "package.json").exists()]
        for base in bases:
            for dirpath, dirnames, filenames in os.walk(base):
                dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS and not d.startswith(".")]
                for fn in filenames:
                    p = Path(dirpath) / fn
                    ext = p.suffix.lower() if p.suffix.lower() != ".meta" else ".meta"
                    if ext == ".meta":
                        m = GUID_RE.search(read_text(p)[:400])
                        if m:
                            self.guid_path[m.group(1)] = rel(str(p)[:-5], self.root)
                    else:
                        self.files[p.suffix if p.suffix in YAML_EXT else ext].append(p)

    def load(self):
        self.walk()
        for p in self.files[".cs"]:
            r = rel(p, self.root)
            f = CsFile(p, r)
            self.cs[r] = f
            for c in f.classes:
                self.class_file.setdefault(c["name"], r)
        path_guid = {v: k for k, v in self.guid_path.items()}
        for r, f in self.cs.items():
            f.guid = path_guid.get(r)
        for ext in YAML_EXT:
            for p in self.files.get(ext, []):
                r = rel(p, self.root)
                text = read_text(p)
                if not text.startswith("%YAML"):
                    continue
                for g in set(GUID_RE.findall(text)):
                    self.refs[g].add(r)
                if ext in (".unity", ".prefab"):
                    self.docs[r] = UnityDoc(p, r)
        for d in self.docs.values():
            for inst in d.instances.values():
                src = self.guid_path.get(inst["src"] or "", "")
                inst["src_path"] = src
                inst["src_name"] = Path(src).stem if src else "?"
        self.path_guid = path_guid

    def script_of(self, monodoc):
        g = yref(monodoc["body"], "m_Script")[1]
        p = self.guid_path.get(g or "")
        return (Path(p).stem if p else None), p, g

    # -- tổng quan ----------------------------------------------------------
    def overview(self):
        o = {"name": self.root.name, "path": str(self.root)}
        pv = read_text(self.root / "ProjectSettings" / "ProjectVersion.txt")
        m = re.search(r"m_EditorVersion: (\S+)", pv)
        o["unity"] = m.group(1) if m else "?"
        ps = read_text(self.root / "ProjectSettings" / "ProjectSettings.asset")
        m = re.search(r"activeInputHandler: (\d)", ps)
        o["input"] = {"0": "Input Manager cũ (Input.GetKey…)", "1": "Input System mới",
                      "2": "Cả hai (cũ + mới)"}.get(m.group(1) if m else "", "Input Manager cũ (mặc định)")
        m = re.search(r"productName: ([^\n]*)", ps)
        o["product"] = m.group(1).strip() if m else ""
        try:
            manifest = json.loads(read_text(self.root / "Packages" / "manifest.json") or "{}")
        except ValueError:
            manifest = {}
        deps = manifest.get("dependencies", {})
        o["packages"] = sorted(k for k in deps if not k.startswith("com.unity.modules."))
        gs = read_text(self.root / "ProjectSettings" / "GraphicsSettings.asset")
        rp = yref(gs, "m_CustomRenderPipeline")[1] or yref(gs, "m_SrpDefaultSettings")[1]
        if "com.unity.render-pipelines.universal" in deps or rp:
            o["pipeline"] = "URP (Universal Render Pipeline)"
        elif "com.unity.render-pipelines.high-definition" in deps:
            o["pipeline"] = "HDRP"
        else:
            o["pipeline"] = "Built-in"
        bs = read_text(self.root / "ProjectSettings" / "EditorBuildSettings.asset")
        scenes = []
        for mm in re.finditer(r"- enabled: (\d)\s*\n\s*path: ([^\n]*)", bs):
            scenes.append({"path": mm.group(2).strip(), "enabled": mm.group(1) == "1"})
        o["build_scenes"] = scenes
        o["scenes"] = sorted(r for r in self.docs if r.endswith(".unity"))
        o["counts"] = {"Script C#": len(self.cs), "Scene": len(o["scenes"]),
                       "Prefab": len([r for r in self.docs if r.endswith(".prefab")]),
                       "Material": len(self.files.get(".mat", [])),
                       "Texture/ảnh": sum(len(self.files.get(e, [])) for e in IMAGE_EXT),
                       "Âm thanh": sum(len(self.files.get(e, [])) for e in AUDIO_EXT)}
        tags = []
        lower_paths = " ".join(p.lower() for p in self.guid_path.values())
        for key, label in (("luna", "Luna Playable"), ("dotween", "DOTween"), ("joystick pack", "Joystick Pack"),
                           ("textmesh pro", "TextMesh Pro"), ("cinemachine", "Cinemachine"),
                           ("playworks", "ironSource Playworks"), ("mraid", "MRAID"), ("feel", "Feel/MMFeedbacks")):
            if key in lower_paths or any(key.replace(" ", "") in p for p in o["packages"]):
                tags.append(label)
        o["tags"] = tags
        return o

    # -- phím tắt -----------------------------------------------------------
    def input_manager(self):
        text = read_text(self.root / "ProjectSettings" / "InputManager.asset")
        axes = defaultdict(set)
        for block in re.split(r"\n  - serializedVersion: \d+\n", text)[1:]:
            name = yget(block, "m_Name")
            if not name or yget(block, "type") not in ("0", None):
                continue
            for k in ("negativeButton", "positiveButton", "altNegativeButton", "altPositiveButton"):
                v = yget(block, k)
                if v:
                    axes[name].add(pretty_keyname(v))
        return {k: sorted(v) for k, v in axes.items()}

    def keycode_overrides(self, keyfields):
        """Giá trị KeyCode đã chỉnh trong Inspector (scene/prefab) cho các trường KeyCode."""
        by_guid = defaultdict(dict)
        for f in keyfields:
            if f["guid"]:
                by_guid[f["guid"]][f["name"]] = f
        out = []
        names = {f["name"] for f in keyfields}
        for r, d in self.docs.items():
            for doc in d.docs.values():
                if doc["cls"] == 114:
                    _, _, g = self.script_of(doc)
                    if g in by_guid:
                        fl = mono_fields(doc["body"])
                        for name in by_guid[g]:
                            if name in fl and fl[name][0].strip().lstrip("-").isdigit():
                                kc = KEYCODES.get(int(fl[name][0]), fl[name][0])
                                go = yref(doc["body"], "m_GameObject")[0]
                                out.append({"field": by_guid[g][name], "key": kc, "where": r,
                                            "go": d.path_of(go)})
            for inst in d.instances.values():
                for mm in re.finditer(r"propertyPath: (\w+)\s*\n\s*value: (-?\d+)", inst["body"]):
                    if mm.group(1) in names:
                        f = next(x for x in keyfields if x["name"] == mm.group(1))
                        out.append({"field": f, "key": KEYCODES.get(int(mm.group(2)), mm.group(2)), "where": r,
                                    "go": inst.get("name") or inst.get("src_name")})
        return out

    def hotkeys(self):
        axes = self.input_manager()
        keys, mouse, axis_uses, touch = [], [], [], []
        keyfields = []
        for r, f in self.cs.items():
            if f.editor:
                continue
            fields = f.fields()
            kmap = {}
            for fl in fields:
                if fl["type"] == "KeyCode":
                    dflt = None
                    if fl["init"]:
                        mm = re.search(r"KeyCode\.(\w+)", fl["init"])
                        dflt = mm.group(1) if mm else None
                    kmap[fl["name"]] = dflt
                    keyfields.append({"name": fl["name"], "cls": fl["cls"], "file": r, "line": fl["line"],
                                      "default": dflt, "guid": f.guid, "serialized": fl["serialized"]})
            code = f.code
            pats = [
                (r"Input\s*\.\s*(GetKeyDown|GetKeyUp|GetKey)\s*\(\s*KeyCode\s*\.\s*(\w+)", "kc"),
                (r"Input\s*\.\s*(GetKeyDown|GetKeyUp|GetKey)\s*\(\s*\"([^\"]+)\"", "str"),
                (r"Input\s*\.\s*(GetKeyDown|GetKeyUp|GetKey)\s*\(\s*([A-Za-z_][\w\.\[\]]*)\s*\)", "var"),
                (r"Keyboard\s*\.\s*current\s*\.\s*(\w+?)Key\s*\.\s*(wasPressedThisFrame|isPressed|wasReleasedThisFrame)", "isys"),
                (r"Keyboard\s*\.\s*current\s*\[\s*Key\s*\.\s*(\w+)\s*\]\s*\.\s*(wasPressedThisFrame|isPressed|wasReleasedThisFrame)", "isys2"),
            ]
            for rx, kind in pats:
                for m in re.finditer(rx, code):
                    if kind == "kc":
                        how, key = m.group(1), pretty_keycode(m.group(2))
                        raw = "KeyCode." + m.group(2)
                    elif kind == "str":
                        how, key, raw = m.group(1), pretty_keyname(m.group(2)), '"%s"' % m.group(2)
                    elif kind == "var":
                        how, var = m.group(1), m.group(2)
                        if var.startswith("KeyCode."):
                            continue
                        dflt = kmap.get(var.split(".")[-1])
                        key = pretty_keycode(dflt) if dflt else "(biến %s)" % var
                        raw = var + (" = KeyCode.%s" % dflt if dflt else "")
                        hit = self._hit(f, m.start(), key=key, how=how, raw=raw)
                        hit["var"] = var.split(".")[-1]
                        keys.append(hit)
                        continue
                    else:
                        how = {"wasPressedThisFrame": "GetKeyDown", "isPressed": "GetKey",
                               "wasReleasedThisFrame": "GetKeyUp"}[m.group(2)]
                        key, raw = pretty_keyname(m.group(1)), m.group(0).replace(" ", "")
                    keys.append(self._hit(f, m.start(), key=key, how=how, raw=raw))
            for m in re.finditer(r"Input\s*\.\s*(GetMouseButtonDown|GetMouseButtonUp|GetMouseButton)\s*\(\s*(\d)", code):
                key = {"0": "Chuột trái / chạm", "1": "Chuột phải", "2": "Chuột giữa"}.get(m.group(2), m.group(2))
                mouse.append(self._hit(f, m.start(), key=key, how=m.group(1).replace("Mouse", "").replace("Button", "Key"),
                                       raw=m.group(0)))
            for m in re.finditer(r"Mouse\s*\.\s*current\s*\.\s*(leftButton|rightButton|middleButton)\s*\.\s*(wasPressedThisFrame|isPressed|wasReleasedThisFrame)", code):
                mouse.append(self._hit(f, m.start(), key=pretty_keyname(m.group(1)), how=m.group(2), raw=m.group(0)))
            for m in re.finditer(r"Input\s*\.\s*(GetButtonDown|GetButtonUp|GetButton|GetAxisRaw|GetAxis)\s*\(\s*\"([^\"]+)\"", code):
                name = m.group(2)
                bound = ", ".join(axes.get(name, [])) or "(không thấy trong InputManager)"
                hit = self._hit(f, m.start(), key="%s → %s" % (name, bound), how=m.group(1), raw=m.group(0))
                (keys if m.group(1).startswith("GetButton") else axis_uses).append(hit)
            for m in re.finditer(r"Input\s*\.\s*(touchCount|GetTouch|touches)\b", code):
                touch.append(self._hit(f, m.start(), key="Chạm màn hình", how=m.group(1), raw=m.group(0)))
        seen, uniq = set(), []
        for h in touch:
            k = (h["file"], h["method"])
            if k not in seen:
                seen.add(k)
                h["how"] = "chạm"
                uniq.append(h)
        touch = uniq
        # Input System .inputactions
        actions = []
        for p in self.files.get(".inputactions", []):
            try:
                data = json.loads(read_text(p))
            except ValueError:
                continue
            for mp in data.get("maps", []):
                per = defaultdict(list)
                for b in mp.get("bindings", []):
                    if b.get("isComposite"):
                        continue
                    path = b.get("path", "")
                    part = (" (%s)" % b["name"]) if b.get("isPartOfComposite") and b.get("name") else ""
                    per[b.get("action", "?")].append(path + part)
                for a in mp.get("actions", []):
                    paths = per.get(a.get("name"), [])
                    kb = [pretty_keyname(x.split(" (")[0]) + (x[x.find(" ("):] if " (" in x else "")
                          for x in paths if x.startswith("<Keyboard>") or x.startswith("<Mouse>")]
                    other = [x for x in paths if not (x.startswith("<Keyboard>") or x.startswith("<Mouse>"))]
                    actions.append({"file": rel(p, self.root), "map": mp.get("name"), "action": a.get("name"),
                                    "keys": kb, "other": other})
        overrides = self.keycode_overrides(keyfields)
        for h in keys:
            ov = [x for x in overrides if x["field"]["name"] == h.get("var") and x["field"]["file"] == h["file"]]
            dflt = ov[0]["field"]["default"] if ov else None
            if ov and {x["key"] for x in ov} != {dflt}:
                real = sorted({pretty_keycode(x["key"]) for x in ov})
                h["key"] = " / ".join(real)
                h["note"] = "đổi trong Inspector" + ((", code: " + pretty_keycode(ov[0]["field"]["default"]))
                                                   if ov[0]["field"]["default"] else "")
        return {"keys": keys, "mouse": mouse, "axes": axis_uses, "touch": touch, "actions": actions,
                "keyfields": keyfields, "overrides": overrides, "axis_map": axes}

    def _hit(self, f, pos, **kw):
        ln = f.line_of(pos)
        meth = f.method_at(pos)
        cls = f.class_at(pos)
        body = find_condition_body(f, pos)
        calls, log = describe_body(body)
        snippet = "\n".join(l.rstrip() for l in f.lines[ln - 1:ln + 3])
        kw.update({"file": f.rel, "line": ln, "cls": cls["name"] if cls else None,
                   "method": meth["name"] if meth else None, "calls": calls, "log": log,
                   "editor_only": ln in f.editor_only, "vendor": f.vendor, "snippet": snippet})
        return kw

    # -- joystick & UI ------------------------------------------------------
    def joystick(self):
        scripts, uses, objects, toggles, hide = [], [], [], [], []
        joy_guids = set()
        joy_classes = set()
        for r, f in self.cs.items():
            for c in f.classes:
                if JOYSTICK_RE.search(c["name"]) or (c["base"] and JOYSTICK_RE.search(c["base"])):
                    joy_classes.add(c["name"])
                    scripts.append({"cls": c["name"], "base": c["base"], "file": r, "line": c["line"],
                                    "vendor": f.vendor})
                    if f.guid:
                        joy_guids.add(f.guid)
        joy_type_re = re.compile(r"\b(%s)\b" % "|".join(sorted(joy_classes | {"Joystick"}, key=len, reverse=True)))
        for r, f in self.cs.items():
            if f.editor or r in {s["file"] for s in scripts}:
                continue
            for fl in f.fields():
                if joy_type_re.search(fl["type"]) or JOYSTICK_RE.search(fl["name"]):
                    reads = sorted(set(re.findall(r"\b%s\s*\.\s*(\w+)" % re.escape(fl["name"]), f.code)))
                    uses.append({"file": r, "line": fl["line"], "cls": fl["cls"], "field": fl["name"],
                                 "type": fl["type"], "reads": reads[:8]})
            for m in re.finditer(r"(\w*(?:joy|stick)\w*)(?:\s*\.\s*gameObject)?\s*\.\s*SetActive\s*\(([^)]*)\)",
                                 f.code, re.I):
                toggles.append(self._hit(f, m.start(), raw=m.group(0)))
        for r, d in self.docs.items():
            for go, name in d.go_name.items():
                comps = d.go_comps.get(go, [])
                cnames = []
                is_joy = bool(JOYSTICK_RE.search(name))
                for c in comps:
                    if c["cls"] == 114:
                        sname, _, g = self.script_of(c)
                        if sname:
                            cnames.append(sname)
                        if g in joy_guids or (sname and JOYSTICK_RE.search(sname)):
                            is_joy = True
                if is_joy:
                    objects.append({"where": r, "path": d.path_of(go), "active": d.go_active.get(go, True),
                                    "comps": cnames})
                elif HIDE_UI_RE.search(" ".join(split_tokens(name))) and not name.lower().startswith("eventsystem"):
                    hide.append({"where": r, "path": d.path_of(go), "active": d.go_active.get(go, True)})
            if r.endswith(".unity"):
                for iid, inst in d.instances.items():
                    src = inst.get("src_path", "")
                    sd = self.docs.get(src)
                    has_joy = JOYSTICK_RE.search(Path(src).stem or "") or (
                        sd and any(self.script_of(c)[2] in joy_guids for cs in sd.go_comps.values() for c in cs
                                   if c["cls"] == 114))
                    if has_joy:
                        objects.append({"where": r, "path": d.instance_path(iid), "active": True,
                                        "comps": ["(prefab %s)" % Path(src).name]})
        return {"scripts": scripts, "uses": uses, "objects": objects, "toggles": toggles, "hide": hide}

    # -- skin ---------------------------------------------------------------
    def asset_kind(self, guid):
        p = self.guid_path.get(guid, "")
        ext = Path(p).suffix.lower()
        if ext == ".mat":
            return "Material"
        if ext in IMAGE_EXT:
            return "Texture/Sprite"
        if ext == ".prefab":
            return "Prefab"
        if ext in (".fbx", ".obj", ".blend", ".mesh"):
            return "Model/Mesh"
        if ext in AUDIO_EXT:
            return "Audio"
        if ext == ".asset":
            return "Asset"
        return ext or "?"

    def skins(self):
        fields, methods, assets, renderers, assigned = [], [], [], [], []
        skin_field_keys = {}
        for r, f in self.cs.items():
            if f.editor or f.vendor:
                continue
            for fl in f.fields():
                toks = set(split_tokens(fl["name"]))
                if toks & SKIN_TOKENS and fl["type"] not in ("int", "float", "bool", "string", "KeyCode"):
                    fields.append({"file": r, "line": fl["line"], "cls": fl["cls"], "field": fl["name"],
                                   "type": fl["type"]})
                    if f.guid:
                        skin_field_keys[(f.guid, fl["name"])] = fl
            for m in f.methods:
                toks = set(split_tokens(m["name"]))
                if toks & SKIN_TOKENS and toks & {"set", "change", "apply", "equip", "select", "swap", "switch",
                                                  "load", "update", "next", "prev", "previous", "random",
                                                  "unlock", "show", "wear", "buy"}:
                    methods.append({"file": r, "line": m["line"], "cls": m["cls"], "method": m["name"]})
        for g, p in sorted(self.guid_path.items(), key=lambda x: x[1]):
            low = p.lower()
            if re.search(r"skin|outfit|costume|cosmetic", low) and Path(p).suffix:
                assets.append({"path": p, "kind": self.asset_kind(g)})
        for r, d in self.docs.items():
            for doc in d.docs.values():
                b = doc["body"]
                go = yref(b, "m_GameObject")[0]
                if doc["cls"] in (137, 23, 212) and not doc["stripped"]:
                    mm = re.search(r"m_Materials:\s*\n((?:[ \t]*- [^\n]*\n)*)", b)
                    mats = [self.guid_path.get(g, "(package/built-in)") for g in GUID_RE.findall(mm.group(1))] if mm else []
                    extra = None
                    if doc["cls"] == 137:
                        mg = yref(b, "m_Mesh")[1]
                        extra = self.guid_path.get(mg or "", "")
                    if doc["cls"] == 212:
                        sg = yref(b, "m_Sprite")[1]
                        extra = self.guid_path.get(sg or "", "")
                    kind = {137: "SkinnedMeshRenderer (nhân vật)", 23: "MeshRenderer", 212: "SpriteRenderer"}[doc["cls"]]
                    renderers.append({"where": r, "path": d.path_of(go), "kind": kind, "mats": mats,
                                      "extra": extra, "cls": doc["cls"]})
                if doc["cls"] == 114:
                    _, _, g = self.script_of(doc)
                    for (sg, fname), fl in skin_field_keys.items():
                        if sg == g:
                            fv = mono_fields(b).get(fname)
                            if fv and fv[1]:
                                assigned.append({"where": r, "go": d.path_of(go), "cls": fl["cls"], "field": fname,
                                                 "values": [self.guid_path.get(x, x[:8]) for x in fv[1]]})
        return {"fields": fields, "methods": methods, "assets": assets, "renderers": renderers,
                "assigned": assigned}

    # -- material & texture -------------------------------------------------
    def shader_name(self, body):
        fid, g = yref(body, "m_Shader")
        if g in KNOWN_SHADERS:
            return KNOWN_SHADERS[g]
        if g and g in self.guid_path:
            p = self.guid_path[g]
            m = re.search(r'Shader\s+"([^"]+)"', read_text(self.root / p)[:2000]) if p.endswith(".shader") else None
            return m.group(1) if m else Path(p).name
        if g == "0000000000000000f000000000000000" or (g is None and fid):
            return BUILTIN_SHADER_IDS.get(fid, "Built-in #%s" % fid)
        return "(shader trong package)"

    def materials(self):
        mats = []
        tex_users = defaultdict(list)
        for p in self.files.get(".mat", []):
            r = rel(p, self.root)
            text = read_text(p)
            slots = []
            for m in re.finditer(r"- (\w+):\s*\n\s*m_Texture: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?", text):
                if m.group(3):
                    tp = self.guid_path.get(m.group(3), "(texture trong package)")
                    slots.append({"slot": m.group(1), "tex": tp})
                    tex_users[tp].append(r)
            color = None
            mc = re.search(r"- _(?:BaseColor|Color): \{r: ([\d\.e-]+), g: ([\d\.e-]+), b: ([\d\.e-]+), a: ([\d\.e-]+)\}", text)
            if mc:
                try:
                    color = "#%02x%02x%02x" % tuple(max(0, min(255, round(float(v) * 255))) for v in mc.groups()[:3])
                except ValueError:
                    color = None
            g = self.path_guid.get(r)
            users = sorted(self.refs.get(g, set()) - {r}) if g else []
            mats.append({"path": r, "shader": self.shader_name(text), "slots": slots, "color": color,
                         "users": users, "env": bool(ENV_RE.search(r))})
        textures = []
        for ext in IMAGE_EXT:
            for p in self.files.get(ext, []):
                r = rel(p, self.root)
                meta = read_text(str(p) + ".meta")
                ttype = yget(meta, "textureType")
                kind = {"0": "Default", "1": "Normal map", "8": "Sprite (2D/UI)", "2": "Editor GUI",
                        "7": "Lightmap", "10": "Single channel", "6": "Cursor", "3": "Cubemap"}.get(ttype or "", "Default")
                g = self.path_guid.get(r)
                users = sorted(self.refs.get(g, set())) if g else []
                textures.append({"path": r, "size": image_size(p), "bytes": os.path.getsize(p), "kind": kind,
                                 "max": yget(meta, "maxTextureSize"), "mats": tex_users.get(r, []),
                                 "users": users, "env": bool(ENV_RE.search(r))})
        textures.sort(key=lambda t: t["path"])
        mats.sort(key=lambda m: m["path"])
        return {"materials": mats, "textures": textures}

    # -- âm thanh -----------------------------------------------------------
    def audio(self):
        clips = []
        for ext in AUDIO_EXT:
            for p in self.files.get(ext, []):
                r = rel(p, self.root)
                g = self.path_guid.get(r)
                clips.append({"path": r, "bytes": os.path.getsize(p), "dur": audio_duration(p),
                              "users": sorted(self.refs.get(g, set())) if g else []})
        clips.sort(key=lambda c: c["path"])
        audio_guids = {self.path_guid.get(c["path"]) for c in clips}
        sources, assigned = [], []
        for r, d in self.docs.items():
            for doc in d.docs.values():
                b = doc["body"]
                go = yref(b, "m_GameObject")[0]
                if doc["cls"] == 82 and not doc["stripped"]:
                    cg = yref(b, "m_audioClip")[1]
                    sources.append({"where": r, "path": d.path_of(go),
                                    "clip": self.guid_path.get(cg or "", "(trống)"),
                                    "awake": yget(b, "m_PlayOnAwake") == "1", "loop": yget(b, "Loop") == "1",
                                    "vol": yget(b, "m_Volume")})
                elif doc["cls"] == 114:
                    sname, _, _ = self.script_of(doc)
                    for fname, (raw, guids) in mono_fields(b).items():
                        hits = [self.guid_path[x] for x in guids if x in audio_guids]
                        if hits:
                            assigned.append({"where": r, "go": d.path_of(go), "script": sname, "field": fname,
                                             "clips": hits})
        plays, actions = [], []
        for r, f in self.cs.items():
            if f.editor or f.vendor:
                continue
            for m in re.finditer(r"(PlayOneShot|PlayClipAtPoint|PlaySound|PlaySfx|PlaySFX|PlayFx|PlayAudio|"
                                 r"(?:\w*(?:audio|sound|sfx)\w*)\s*\.\s*Play\w*)\s*\(([^;\n]*)", f.code, re.I):
                plays.append(self._hit(f, m.start(), raw=(m.group(1) + "(" + m.group(2)).strip()[:120]))
            for meth in f.methods:
                name = meth["name"]
                tl = [t for t in split_tokens(name) if t != "on"]
                toks = set(tl) | {"".join(tl)}
                if name in UNITY_LIFECYCLE or (tl and tl[0] in NOT_ACTION_PREFIX):
                    continue
                if name in UNITY_ACTION_CALLBACKS or toks & ACTION_TOKENS:
                    body = f.body(meth)
                    actions.append({"file": r, "line": meth["line"], "end": meth["end_line"], "cls": meth["cls"],
                                    "method": name, "sound": bool(SOUND_IN_BODY_RE.search(body)),
                                    "vfx": bool(VFX_IN_BODY_RE.search(body)),
                                    "callback": name in UNITY_ACTION_CALLBACKS})
            for m in re.finditer(r"(\w+)\s*\.\s*onClick\s*\.\s*AddListener\s*\(", f.code):
                arg = f.code[m.end():m.end() + 400]
                calls = [c for c in re.findall(r"([A-Za-z_][\w\.]*)\s*\(", arg) if c not in ("delegate",) + tuple(CS_KEYWORDS)]
                target = calls[0] + "()" if calls else (re.match(r"\s*([\w\.]+)", arg) or [None, "?"])[1]
                actions.append({"file": r, "line": f.line_of(m.start()), "end": f.line_of(m.start()),
                                "cls": (f.class_at(m.start()) or {}).get("name"),
                                "method": "%s.onClick → %s" % (m.group(1), target),
                                "sound": bool(SOUND_IN_BODY_RE.search(arg.split(";")[0])), "vfx": False, "callback": True})
        actions.sort(key=lambda a: (a["sound"], a["file"], a["line"]))
        return {"clips": clips, "sources": sources, "assigned": assigned, "plays": plays, "actions": actions}

    def scene_summary(self):
        out = []
        for r in sorted(self.docs):
            if not r.endswith(".unity"):
                continue
            d = self.docs[r]
            roots = []
            for go, name in d.go_name.items():
                tr = d.go_tr.get(go)
                if tr and d.tr_father.get(tr) in (None, "0"):
                    roots.append(name)
            for iid, inst in d.instances.items():
                if inst.get("parent") in (None, "0"):
                    roots.append((inst.get("name") or inst.get("src_name") or "?") + " (prefab)")
            cams = sum(1 for cs in d.go_comps.values() for c in cs if c["cls"] == 20)
            canv = sum(1 for cs in d.go_comps.values() for c in cs if c["cls"] == 223)
            out.append({"path": r, "objects": len(d.go_name), "prefabs": len(d.instances), "roots": sorted(roots),
                        "cameras": cams, "canvases": canv})
        return out

    def scan(self):
        self.load()
        return {"overview": self.overview(), "hotkeys": self.hotkeys(), "joystick": self.joystick(),
                "skins": self.skins(), "materials": self.materials(), "audio": self.audio(),
                "scenes": self.scene_summary(), "time": datetime.now().strftime("%d/%m/%Y %H:%M")}


def image_size(p):
    try:
        with open(p, "rb") as f:
            head = f.read(32)
            if head[:8] == b"\x89PNG\r\n\x1a\n":
                w, h = struct.unpack(">II", head[16:24])
                return "%d×%d" % (w, h)
            if head[:6] in (b"GIF87a", b"GIF89a"):
                w, h = struct.unpack("<HH", head[6:10])
                return "%d×%d" % (w, h)
            if head[:2] == b"BM":
                w, h = struct.unpack("<ii", head[18:26])
                return "%d×%d" % (w, abs(h))
            if head[:2] == b"\xff\xd8":
                f.seek(2)
                while True:
                    b = f.read(1)
                    while b and b != b"\xff":
                        b = f.read(1)
                    while b == b"\xff":
                        b = f.read(1)
                    if not b:
                        return ""
                    if b[0] in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF):
                        f.read(3)
                        h, w = struct.unpack(">HH", f.read(4))
                        return "%d×%d" % (w, h)
                    seg = struct.unpack(">H", f.read(2))[0]
                    f.seek(seg - 2, 1)
            if head[:4] == b"8BPS":
                h, w = struct.unpack(">II", head[14:22])
                return "%d×%d" % (w, h)
    except (OSError, struct.error):
        pass
    return ""


def audio_duration(p):
    try:
        if str(p).lower().endswith(".wav"):
            with wave.open(str(p)) as w:
                return round(w.getnframes() / float(w.getframerate()), 2)
    except (wave.Error, EOFError, OSError, ZeroDivisionError):
        pass
    return None


# ---------------------------------------------------------------------------
# Xuất báo cáo
# ---------------------------------------------------------------------------

def fmt_bytes(n):
    for unit in ("B", "KB", "MB"):
        if n < 1024:
            return ("%d %s" if unit == "B" else "%.1f %s") % (n, unit)
        n /= 1024.0
    return "%.1f GB" % n


def where(h):
    s = "%s:%d" % (h["file"], h["line"])
    ctx = ".".join(x for x in (h.get("cls"), h.get("method")) if x)
    return s, ctx


def does(h):
    bits = []
    if h.get("log"):
        bits.append('log "%s"' % h["log"])
    if h.get("calls"):
        bits.append(", ".join(h["calls"]))
    return " · ".join(bits)


def how_vi(h):
    return {"GetKeyDown": "nhấn", "GetKeyUp": "thả", "GetKey": "giữ", "GetButtonDown": "nhấn",
            "GetButtonUp": "thả", "GetButton": "giữ", "GetDown": "nhấn", "GetUp": "thả", "Get": "giữ",
            "wasPressedThisFrame": "nhấn", "isPressed": "giữ", "wasReleasedThisFrame": "thả", "chạm": "chạm"}.get(h.get("how"), h.get("how", ""))


def build_markdown(R):
    o, H, J, S, M, A = R["overview"], R["hotkeys"], R["joystick"], R["skins"], R["materials"], R["audio"]
    L = []
    add = L.append
    add("# Báo cáo Creative — %s" % o["name"])
    add("")
    add("Quét lúc %s · Unity %s · %s · Input: %s" % (R["time"], o["unity"], o["pipeline"], o["input"]))
    if o["tags"]:
        add("Thư viện nhận ra: " + ", ".join(o["tags"]))
    add("")
    add("Scene trong Build: " + (", ".join(s["path"] + ("" if s["enabled"] else " (tắt)") for s in o["build_scenes"]) or "(chưa có)"))
    add("")

    add("## 1. Phím tắt")
    add("")
    main = [h for h in H["keys"] if not h["vendor"]]
    if main:
        add("| Phím | Kiểu | Làm gì (gọi hàm / log) | Ở đâu | Chỉ Editor |")
        add("|---|---|---|---|---|")
        for h in sorted(main, key=lambda x: x["key"]):
            w, ctx = where(h)
            add("| **%s**%s | %s | %s | `%s` %s | %s |" % (h["key"], (" (%s)" % h["note"]) if h.get("note") else "",
                                                     how_vi(h), does(h) or "—", w, ctx,
                                                     "✓" if h["editor_only"] else ""))
    else:
        add("Không thấy phím tắt bàn phím nào trong code của game.")
    if H["overrides"]:
        add("")
        add("Phím KeyCode đặt trong Inspector (ưu tiên hơn giá trị trong code):")
        for ov in H["overrides"]:
            add("- `%s.%s` = **%s** — %s → %s" % (ov["field"]["cls"], ov["field"]["name"], pretty_keycode(ov["key"]),
                                                 ov["where"], ov["go"]))
    if H["actions"]:
        add("")
        add("Input System (.inputactions):")
        for a in H["actions"]:
            add("- %s / **%s**: %s%s" % (a["map"], a["action"], ", ".join(a["keys"]) or "—",
                                        (" · khác: " + ", ".join(a["other"][:4])) if a["other"] else ""))
    if H["mouse"] or H["touch"]:
        add("")
        add("Chuột / chạm:")
        for h in [x for x in H["mouse"] + H["touch"] if not x["vendor"]]:
            w, ctx = where(h)
            how = how_vi(h)
            add("- %s%s — `%s` %s %s" % (h["key"], (" (%s)" % how) if how != "chạm" else "", w, ctx,
                                        ("→ " + does(h)) if does(h) else ""))
    if H["axes"]:
        add("")
        add("Trục di chuyển (GetAxis):")
        for h in [x for x in H["axes"] if not x["vendor"]]:
            w, ctx = where(h)
            add("- %s — `%s` %s" % (h["key"], w, ctx))
    vend = [h for h in H["keys"] if h["vendor"]]
    if vend:
        add("")
        add("Trong thư viện bên thứ 3 (%d chỗ, thường không phải phím của game): " % len(vend) +
            ", ".join(sorted({h["key"] for h in vend})))
    add("")

    add("## 2. Joystick & UI cần ẩn khi quay")
    add("")
    if J["objects"]:
        for x in J["objects"]:
            add("- `%s` → **%s**%s %s" % (x["where"], x["path"], "" if x["active"] else " (đang tắt)",
                                         ("[" + ", ".join(x["comps"]) + "]") if x["comps"] else ""))
    else:
        add("Không thấy GameObject joystick trong scene/prefab.")
    if J["scripts"]:
        add("")
        add("Script joystick: " + ", ".join("`%s` (%s:%d)" % (s["cls"], s["file"], s["line"]) for s in J["scripts"]))
    if J["uses"]:
        add("")
        add("Code đọc joystick:")
        for u in J["uses"]:
            add("- `%s.%s` (%s) — %s:%d%s" % (u["cls"], u["field"], u["type"], u["file"], u["line"],
                                             (" · đọc " + ", ".join(u["reads"])) if u["reads"] else ""))
    if J["toggles"]:
        add("")
        add("Code bật/tắt joystick:")
        for t in J["toggles"]:
            add("- `%s` — %s:%d" % (t["raw"], t["file"], t["line"]))
    add("")
    add("Mẹo: muốn ẩn joystick mà vẫn điều khiển được thì đừng tắt GameObject — đặt alpha của CanvasGroup = 0 "
        "(hoặc tắt các component Image). Cửa sổ Tools ▸ Creative Recorder trong Unity có nút làm việc này.")
    if J["hide"]:
        add("")
        add("UI khác thường cần ẩn (tay hướng dẫn, CTA, logo…):")
        for x in J["hide"][:40]:
            add("- `%s` → %s%s" % (x["where"], x["path"], "" if x["active"] else " (đang tắt)"))
    add("")

    add("## 3. Skin")
    add("")
    if S["fields"]:
        add("Trường skin trong code:")
        for x in S["fields"]:
            add("- `%s.%s` (%s) — %s:%d" % (x["cls"], x["field"], x["type"], x["file"], x["line"]))
    if S["assigned"]:
        add("")
        add("Đang gán trong scene/prefab:")
        for x in S["assigned"]:
            add("- %s → %s · `%s.%s` = %s" % (x["where"], x["go"], x["cls"], x["field"], ", ".join(x["values"][:8])))
    if S["methods"]:
        add("")
        add("Hàm đổi skin: " + ", ".join("`%s.%s()` (%s:%d)" % (m["cls"], m["method"], m["file"], m["line"])
                                        for m in S["methods"]))
    chars = [x for x in S["renderers"] if x["cls"] == 137]
    if chars:
        add("")
        add("Nhân vật (SkinnedMeshRenderer) — đổi skin = đổi material/mesh ở đây:")
        for x in chars:
            add("- %s → **%s** · material: %s · mesh: %s" % (x["where"], x["path"], ", ".join(x["mats"]) or "—",
                                                            x["extra"] or "—"))
    if S["assets"]:
        add("")
        add("Asset có chữ skin/outfit/costume: " + ", ".join("`%s`" % a["path"] for a in S["assets"][:40]))
    if not any([S["fields"], S["assigned"], S["methods"], chars, S["assets"]]):
        add("Không thấy hệ thống skin riêng. Đổi skin bằng cách thay material/sprite trên renderer (mục 4).")
    add("")

    add("## 4. Material & Texture")
    add("")
    add("| Material | Shader | Texture theo slot | Dùng ở |")
    add("|---|---|---|---|")
    for m in M["materials"]:
        add("| `%s`%s | %s | %s | %s |" % (m["path"], " 🌍" if m["env"] else "", m["shader"],
                                          "<br>".join("%s: `%s`" % (s["slot"], s["tex"]) for s in m["slots"]) or
                                          (m["color"] or "—"), ", ".join(m["users"][:4]) or "(không thấy dùng)"))
    add("")
    add("Texture (%d):" % len(M["textures"]))
    for t in M["textures"]:
        add("- `%s` %s · %s · %s%s" % (t["path"], t["size"], t["kind"], fmt_bytes(t["bytes"]),
                                       (" · material: " + ", ".join(Path(x).stem for x in t["mats"])) if t["mats"] else
                                       ("" if t["users"] else " · (không thấy dùng)")))
    add("")

    add("## 5. Âm thanh")
    add("")
    add("File âm thanh (%d):" % len(A["clips"]))
    for c in A["clips"]:
        add("- `%s` %s%s%s" % (c["path"], fmt_bytes(c["bytes"]), (" · %.2fs" % c["dur"]) if c["dur"] else "",
                               "" if c["users"] else " · (chưa gán ở đâu)"))
    if A["sources"]:
        add("")
        add("AudioSource trong scene/prefab:")
        for s in A["sources"]:
            add("- %s → %s · clip %s%s%s" % (s["where"], s["path"], s["clip"], " · PlayOnAwake" if s["awake"] else "",
                                            " · loop" if s["loop"] else ""))
    if A["assigned"]:
        add("")
        add("Clip gán vào script:")
        for s in A["assigned"]:
            add("- %s → %s · `%s.%s` = %s" % (s["where"], s["go"], s["script"], s["field"], ", ".join(s["clips"])))
    if A["plays"]:
        add("")
        add("Code đang phát âm thanh:")
        for p in A["plays"]:
            w, ctx = where(p)
            add("- `%s` — %s %s" % (p["raw"], w, ctx))
    add("")
    add("Hành động có thể thêm SFX (✗ = chưa có âm thanh):")
    for a in A["actions"]:
        add("- %s `%s.%s` — %s:%d%s" % ("✓" if a["sound"] else "✗", a["cls"], a["method"], a["file"], a["line"],
                                        " · có VFX" if a["vfx"] else ""))
    add("")
    add("## 6. Scene")
    add("")
    for s in R["scenes"]:
        add("- `%s`: %d object, %d prefab, %d camera, %d canvas · gốc: %s" % (
            s["path"], s["objects"], s["prefabs"], s["cameras"], s["canvases"], ", ".join(s["roots"][:20])))
    return "\n".join(L) + "\n"


def esc(s):
    return html.escape(str(s if s is not None else ""))


def table(head, rows, cls=""):
    if not rows:
        return '<p class="empty">Không có.</p>'
    h = "".join("<th>%s</th>" % esc(x) for x in head)
    body = "".join("<tr>%s</tr>" % "".join("<td>%s</td>" % c for c in r) for r in rows)
    return '<div class="tw"><table class="%s"><thead><tr>%s</tr></thead><tbody>%s</tbody></table></div>' % (cls, h, body)


def code(s):
    return "<code>%s</code>" % esc(s)


def loc(h):
    w, ctx = where(h)
    return '<span class="loc">%s</span>%s' % (esc(w), ('<br><span class="ctx">%s</span>' % esc(ctx)) if ctx else "")


def chip(s, kind=""):
    return '<span class="chip %s">%s</span>' % (kind, esc(s))


def build_html(R, md):
    o, H, J, S, M, A = R["overview"], R["hotkeys"], R["joystick"], R["skins"], R["materials"], R["audio"]
    sec = []

    # Tổng quan
    counts = "".join('<div class="stat"><b>%s</b><span>%s</span></div>' % (v, esc(k)) for k, v in o["counts"].items())
    info = table(["", ""], [
        ["Unity", esc(o["unity"])], ["Render pipeline", esc(o["pipeline"])], ["Input", esc(o["input"])],
        ["Thư viện nhận ra", esc(", ".join(o["tags"]) or "—")],
        ["Scene trong Build", "<br>".join(code(s["path"]) + ("" if s["enabled"] else " (tắt)") for s in o["build_scenes"]) or "—"],
        ["Package", esc(", ".join(p.replace("com.unity.", "") for p in o["packages"])) or "—"],
    ], "kv")
    sec.append(("overview", "Tổng quan", '<div class="stats">%s</div>%s' % (counts, info)))

    # Phím tắt
    main = sorted([h for h in H["keys"] if not h["vendor"]], key=lambda x: x["key"])
    rows = []
    for h in main:
        rows.append(['<kbd>%s</kbd>' % esc(h["key"]) + ("<br><span class='ctx'>%s</span>" % esc(h["note"]) if h.get("note") else ""),
                     esc(how_vi(h)),
                     esc(does(h)) or '<span class="mut">—</span>',
                     loc(h), chip("Editor", "ok") if h["editor_only"] else "",
                     '<details><summary>code</summary><pre>%s</pre></details>' % esc(h["snippet"])])
    body = table(["Phím", "Kiểu", "Làm gì", "Ở đâu", "", ""], rows, "filter")
    if H["overrides"]:
        body += "<h3>Phím KeyCode đặt trong Inspector</h3><p class='mut'>Giá trị trong scene/prefab ưu tiên hơn code.</p>"
        body += table(["Trường", "Phím thực tế", "Mặc định trong code", "Ở đâu"], [
            [code("%s.%s" % (ov["field"]["cls"], ov["field"]["name"])), "<kbd>%s</kbd>" % esc(pretty_keycode(ov["key"])),
             esc(pretty_keycode(ov["field"]["default"]) if ov["field"]["default"] else "—"),
             esc(ov["where"]) + "<br><span class='ctx'>%s</span>" % esc(ov["go"])] for ov in H["overrides"]], "filter")
    if H["actions"]:
        body += "<h3>Input System (.inputactions)</h3>"
        body += table(["Map", "Action", "Bàn phím / chuột", "Khác"], [
            [esc(a["map"]), "<b>%s</b>" % esc(a["action"]), " ".join("<kbd>%s</kbd>" % esc(k) for k in a["keys"]) or "—",
             esc(", ".join(a["other"][:6]))] for a in H["actions"]], "filter")
    mt = [x for x in H["mouse"] + H["touch"] if not x["vendor"]]
    if mt:
        body += "<h3>Chuột / chạm</h3>" + table(["Nút", "Kiểu", "Làm gì", "Ở đâu"], [
            [esc(h["key"]), esc(how_vi(h)), esc(does(h)) or "—", loc(h)] for h in mt], "filter")
    ax = [x for x in H["axes"] if not x["vendor"]]
    if ax:
        body += "<h3>Trục di chuyển (GetAxis)</h3>" + table(["Trục → phím", "Ở đâu"], [[esc(h["key"]), loc(h)] for h in ax], "filter")
    vend = [h for h in H["keys"] + H["mouse"] if h["vendor"]]
    if vend:
        body += "<details class='box'><summary>Trong thư viện bên thứ 3 (%d) — thường không phải phím của game</summary>%s</details>" % (
            len(vend), table(["Phím", "Ở đâu"], [[esc(h["key"]), loc(h)] for h in vend]))
    if not main:
        body = "<p class='empty'>Không thấy phím tắt bàn phím nào trong code của game. Có thể game chỉ dùng chạm/chuột — xem bên dưới.</p>" + body
    sec.append(("hotkeys", "Phím tắt", body))

    # Joystick
    body = table(["Ở đâu", "GameObject", "Trạng thái", "Component"], [
        [esc(x["where"]), "<b>%s</b>" % esc(x["path"]), chip("bật", "ok") if x["active"] else chip("tắt"),
         esc(", ".join(x["comps"]))] for x in J["objects"]], "filter")
    body += ("<div class='tip'><b>Ẩn joystick mà vẫn điều khiển được:</b> đừng tắt GameObject (input sẽ mất). "
             "Thêm <code>CanvasGroup</code> vào object joystick rồi đặt <b>Alpha = 0</b>, hoặc tắt các component "
             "<code>Image</code>. Trong Unity, mở <b>Tools ▸ Creative Recorder</b> → tab Joystick & UI → nút "
             "<b>Ẩn (vẫn chạy)</b>.</div>")
    if J["scripts"]:
        body += "<h3>Script joystick</h3>" + table(["Class", "Kế thừa", "File"], [
            [code(s["cls"]), esc(s["base"] or ""), esc("%s:%d" % (s["file"], s["line"]))] for s in J["scripts"]])
    if J["uses"]:
        body += "<h3>Code đọc joystick</h3>" + table(["Trường", "Kiểu", "Đọc", "File"], [
            [code("%s.%s" % (u["cls"], u["field"])), esc(u["type"]), esc(", ".join(u["reads"])),
             esc("%s:%d" % (u["file"], u["line"]))] for u in J["uses"]])
    if J["toggles"]:
        body += "<h3>Code bật/tắt joystick</h3>" + table(["Lệnh", "Ở đâu"], [[code(t["raw"]), loc(t)] for t in J["toggles"]])
    if J["hide"]:
        body += "<h3>UI khác thường cần ẩn khi quay</h3><p class='mut'>Đoán theo tên: hand, tutorial, CTA, logo, endcard…</p>"
        body += table(["Ở đâu", "GameObject", "Trạng thái"], [
            [esc(x["where"]), esc(x["path"]), chip("bật", "ok") if x["active"] else chip("tắt")] for x in J["hide"]], "filter")
    sec.append(("joystick", "Joystick & UI", body))

    # Skin
    body = ""
    if S["fields"]:
        body += "<h3>Trường skin trong code</h3>" + table(["Trường", "Kiểu", "File"], [
            [code("%s.%s" % (x["cls"], x["field"])), esc(x["type"]), esc("%s:%d" % (x["file"], x["line"]))] for x in S["fields"]], "filter")
    if S["assigned"]:
        body += "<h3>Skin đang gán trong scene/prefab</h3>" + table(["Ở đâu", "Trường", "Giá trị"], [
            [esc(x["where"]) + "<br><span class='ctx'>%s</span>" % esc(x["go"]), code("%s.%s" % (x["cls"], x["field"])),
             "<br>".join(esc(v) for v in x["values"][:12])] for x in S["assigned"]], "filter")
    if S["methods"]:
        body += "<h3>Hàm đổi skin</h3>" + table(["Hàm", "File"], [
            [code("%s.%s()" % (m["cls"], m["method"])), esc("%s:%d" % (m["file"], m["line"]))] for m in S["methods"]])
    body += "<h3>Renderer (đổi skin = đổi material / mesh / sprite ở đây)</h3>" + table(
        ["Ở đâu", "GameObject", "Loại", "Material", "Mesh / Sprite"], [
            [esc(x["where"]), "<b>%s</b>" % esc(x["path"]), esc(x["kind"]), "<br>".join(esc(m) for m in x["mats"]) or "—",
             esc(x["extra"] or "—")] for x in sorted(S["renderers"], key=lambda x: (x["cls"] != 137, x["where"]))], "filter")
    if S["assets"]:
        body += "<h3>Asset có chữ skin/outfit/costume</h3>" + table(["Asset", "Loại"], [
            [esc(a["path"]), esc(a["kind"])] for a in S["assets"]], "filter")
    sec.append(("skins", "Skin", body))

    # Material & texture
    body = "<p class='mut'>🌍 = tên giống môi trường/map (ground, floor, terrain, road, bg…)</p>"
    body += "<h3>Material</h3>" + table(["Material", "Shader", "Texture theo slot", "Dùng ở"], [
        [esc(m["path"]) + (" 🌍" if m["env"] else ""), esc(m["shader"]),
         "<br>".join("<span class='mut'>%s</span> %s" % (esc(s["slot"]), esc(s["tex"])) for s in m["slots"]) or
         ('<span class="sw" style="background:%s"></span>%s' % (m["color"], m["color"]) if m["color"] else "—"),
         "<br>".join(esc(u) for u in m["users"][:6]) or "<span class='mut'>(không thấy dùng)</span>"] for m in M["materials"]], "filter")
    body += "<h3>Texture</h3>" + table(["Texture", "Kích thước", "Loại", "Dung lượng", "Material dùng", "Dùng ở"], [
        [esc(t["path"]) + (" 🌍" if t["env"] else ""), esc(t["size"]), esc(t["kind"]), esc(fmt_bytes(t["bytes"])),
         "<br>".join(esc(Path(x).stem) for x in t["mats"]) or "—",
         "<br>".join(esc(u) for u in t["users"][:4]) or "<span class='mut'>(không thấy dùng)</span>"] for t in M["textures"]], "filter")
    sec.append(("materials", "Material & Texture", body))

    # Âm thanh
    body = "<h3>Hành động — chỗ có thể thêm SFX</h3><p class='mut'>✗ = trong hàm chưa thấy lệnh phát âm thanh. Thêm SFX không cần sửa code: dùng component <code>CreativeSfxTrigger</code> (Tools ▸ Creative Recorder ▸ SFX).</p>"
    body += table(["", "Hàm / sự kiện", "File", "VFX"], [
        [chip("có", "ok") if a["sound"] else chip("✗ chưa có", "warn"), code("%s.%s" % (a["cls"], a["method"])),
         esc("%s:%d" % (a["file"], a["line"])), "✓" if a["vfx"] else ""] for a in A["actions"]], "filter")
    body += "<h3>File âm thanh</h3>" + table(["File", "Dung lượng", "Độ dài", "Dùng ở"], [
        [esc(c["path"]), esc(fmt_bytes(c["bytes"])), esc("%.2fs" % c["dur"]) if c["dur"] else "",
         "<br>".join(esc(u) for u in c["users"][:4]) or "<span class='mut'>(chưa gán)</span>"] for c in A["clips"]], "filter")
    if A["sources"]:
        body += "<h3>AudioSource trong scene/prefab</h3>" + table(["Ở đâu", "GameObject", "Clip", ""], [
            [esc(s["where"]), esc(s["path"]), esc(s["clip"]),
             (chip("PlayOnAwake", "ok") if s["awake"] else "") + (chip("loop") if s["loop"] else "")] for s in A["sources"]], "filter")
    if A["assigned"]:
        body += "<h3>Clip gán vào script</h3>" + table(["Ở đâu", "Trường", "Clip"], [
            [esc(s["where"]) + "<br><span class='ctx'>%s</span>" % esc(s["go"]), code("%s.%s" % (s["script"], s["field"])),
             "<br>".join(esc(c) for c in s["clips"])] for s in A["assigned"]], "filter")
    if A["plays"]:
        body += "<h3>Code đang phát âm thanh</h3>" + table(["Lệnh", "Ở đâu"], [[code(p["raw"]), loc(p)] for p in A["plays"]], "filter")
    sec.append(("audio", "Âm thanh", body))

    body = table(["Scene", "Object", "Prefab", "Camera", "Canvas", "Object gốc"], [
        [esc(s["path"]), str(s["objects"]), str(s["prefabs"]), str(s["cameras"]), str(s["canvases"]),
         esc(", ".join(s["roots"]))] for s in R["scenes"]])
    sec.append(("scenes", "Scene", body))

    nav = "".join('<a href="#%s">%s</a>' % (k, esc(t)) for k, t, _ in sec)
    sections = "".join('<section id="%s"><h2>%s</h2>%s</section>' % (k, esc(t), b) for k, t, b in sec)
    return HTML_TEMPLATE.replace("{{TITLE}}", esc(o["name"])).replace("{{TIME}}", esc(R["time"])) \
        .replace("{{PATH}}", esc(o["path"])).replace("{{NAV}}", nav).replace("{{SECTIONS}}", sections) \
        .replace("{{MD}}", esc(md))


HTML_TEMPLATE = """<!doctype html>
<html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Creative Scan — {{TITLE}}</title>
<style>
:root{--bg:#f7f7f5;--card:#fff;--fg:#1d1d1b;--mut:#6b6b66;--line:#e4e3de;--acc:#2f6fdb;--ok:#1f8a4c;--warn:#b5560b;--code:#f0efeb}
@media (prefers-color-scheme:dark){:root{--bg:#161615;--card:#1f1f1d;--fg:#ecebe6;--mut:#9a9992;--line:#33322f;--acc:#7aa7ff;--ok:#5cc58a;--warn:#f0a35e;--code:#2a2a27}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
@media (max-width:640px){header{position:static!important}}
header{position:sticky;top:0;z-index:5;background:var(--bg);border-bottom:1px solid var(--line);padding:12px 16px}
header h1{margin:0;font-size:18px}header .sub{color:var(--mut);font-size:12px;word-break:break-all}
.bar{display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin-top:8px}
nav{display:flex;gap:4px;flex-wrap:wrap}nav a{color:var(--fg);text-decoration:none;padding:4px 10px;border:1px solid var(--line);border-radius:99px;font-size:13px;background:var(--card)}
nav a:hover{border-color:var(--acc)}
input[type=search]{flex:1;min-width:180px;padding:6px 10px;border:1px solid var(--line);border-radius:8px;background:var(--card);color:var(--fg);font:inherit}
button{padding:6px 12px;border:1px solid var(--acc);border-radius:8px;background:var(--acc);color:#fff;font:inherit;cursor:pointer}
main{max-width:1200px;margin:0 auto;padding:8px 16px 60px}
section{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:12px 16px;margin:16px 0;scroll-margin-top:110px}
h2{margin:4px 0 8px;font-size:17px}h3{margin:18px 0 6px;font-size:14px}
.tw{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;vertical-align:top;padding:6px 8px;border-bottom:1px solid var(--line)}th{color:var(--mut);font-weight:600;white-space:nowrap}
table.kv td:first-child{color:var(--mut);width:160px}
code,pre,kbd{font-family:ui-monospace,Menlo,Consolas,monospace;font-size:12px}code{background:var(--code);padding:1px 4px;border-radius:4px;word-break:break-all}
pre{background:var(--code);padding:8px;border-radius:6px;overflow-x:auto;margin:4px 0}
kbd{display:inline-block;min-width:22px;text-align:center;padding:2px 7px;border:1px solid var(--line);border-bottom-width:3px;border-radius:6px;background:var(--card);font-weight:700;font-size:13px}
.loc{font-family:ui-monospace,Menlo,Consolas,monospace;font-size:12px;word-break:break-all}.ctx,.mut{color:var(--mut);font-size:12px}
.chip{display:inline-block;padding:0 8px;border-radius:99px;font-size:11px;border:1px solid var(--line);color:var(--mut);margin:1px;white-space:nowrap}
.chip.ok{color:var(--ok);border-color:var(--ok)}.chip.warn{color:var(--warn);border-color:var(--warn)}
.stats{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:8px}.stat{border:1px solid var(--line);border-radius:10px;padding:8px 12px;min-width:110px}
.stat b{display:block;font-size:20px}.stat span{color:var(--mut);font-size:12px}
.tip{border-left:3px solid var(--acc);padding:8px 12px;margin:12px 0;background:var(--code);border-radius:0 8px 8px 0}
.empty{color:var(--mut)}details summary{cursor:pointer;color:var(--acc);font-size:12px}details.box{margin-top:12px}
.sw{display:inline-block;width:12px;height:12px;border-radius:3px;border:1px solid var(--line);vertical-align:-2px;margin-right:4px}
tr.hid{display:none}
</style></head><body>
<header><h1>Creative Scan — {{TITLE}}</h1><div class="sub">Quét lúc {{TIME}} · {{PATH}}</div>
<div class="bar"><nav>{{NAV}}</nav><input type="search" id="q" placeholder="Lọc bảng: tên phím, object, file…"><button id="copy">Copy Markdown cho AI</button></div></header>
<main>{{SECTIONS}}</main>
<textarea id="md" hidden>{{MD}}</textarea>
<script>
const q=document.getElementById('q');
q.addEventListener('input',()=>{const t=q.value.trim().toLowerCase();
 document.querySelectorAll('table.filter tbody tr').forEach(r=>{r.classList.toggle('hid',!!t&&!r.textContent.toLowerCase().includes(t))});});
document.getElementById('copy').addEventListener('click',async e=>{const md=document.getElementById('md').value;
 try{await navigator.clipboard.writeText(md)}catch(_){const a=document.getElementById('md');a.hidden=false;a.select();document.execCommand('copy');a.hidden=true}
 e.target.textContent='Đã copy ✓';setTimeout(()=>e.target.textContent='Copy Markdown cho AI',1500);});
</script></body></html>
"""


def main():
    ap = argparse.ArgumentParser(description="Quét project Unity cho người quay creative.")
    ap.add_argument("project", nargs="?", help="Thư mục project Unity (có Assets/ và ProjectSettings/)")
    ap.add_argument("--out", help="Thư mục ghi báo cáo (mặc định: reports/<tên project>/ cạnh script)")
    ap.add_argument("--open", action="store_true", help="Mở báo cáo trên trình duyệt khi xong")
    ap.add_argument("--json", action="store_true", help="Ghi thêm creative-scan.json (dữ liệu thô)")
    a = ap.parse_args()
    proj = a.project
    if not proj:
        proj = input("Đường dẫn project Unity: ").strip().strip('"').strip("'")
    root = Path(proj.strip().strip('"')).expanduser()
    if not (root / "Assets").is_dir():
        for sub in root.iterdir() if root.is_dir() else []:
            if (sub / "Assets").is_dir() and (sub / "ProjectSettings").is_dir():
                root = sub
                break
    if not (root / "Assets").is_dir():
        sys.exit("Không thấy thư mục Assets/ trong %s — hãy chọn thư mục gốc của project Unity." % root)
    print("Đang quét %s ..." % root)
    R = Project(root).scan()
    out = Path(a.out) if a.out else Path(__file__).resolve().parent / "reports" / re.sub(r"[^\w\-. ]", "_", root.name)
    out.mkdir(parents=True, exist_ok=True)
    md = build_markdown(R)
    (out / "creative-scan.md").write_text(md, encoding="utf-8")
    page = out / "creative-scan.html"
    page.write_text(build_html(R, md), encoding="utf-8")
    if a.json:
        (out / "creative-scan.json").write_text(json.dumps(R, ensure_ascii=False, indent=1, default=str), encoding="utf-8")
    H, J, A = R["hotkeys"], R["joystick"], R["audio"]
    print("  Phím tắt: %d · Joystick: %d object · Material: %d · Texture: %d · Âm thanh: %d · Hành động chưa có SFX: %d" % (
        len([h for h in H["keys"] if not h["vendor"]]), len(J["objects"]), len(R["materials"]["materials"]),
        len(R["materials"]["textures"]), len(A["clips"]), len([x for x in A["actions"] if not x["sound"]])))
    print("Đã ghi:\n  %s\n  %s" % (page, out / "creative-scan.md"))
    if a.open:
        webbrowser.open(page.resolve().as_uri())


if __name__ == "__main__":
    main()
