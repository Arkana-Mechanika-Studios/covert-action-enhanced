"""Validate the oracle itself: recorded outcomes of simple CODE and GAME functions against
what their matched C and the MSC 5.1 library define.

    python -B tools/oracle/selftest.py

CODE runs from the captured workstation state (build/oracle/states/CODE/decode-v1),
GAME from its own crt0 startup (FinalProbe DOS fixtures, stopped at main).
"""
import base64
import hashlib
import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import oracle_common as oc  # noqa: E402
import run_function as rf  # noqa: E402

FAILS = []
CHECKS = [0]
SOLVED = [0, 0]


def check(what, got, want):
    CHECKS[0] += 1
    if got != want:
        FAILS.append(f"{what}: got {got!r}, want {want!r}")


def s16(v):
    v &= 0xffff
    return v - 0x10000 if v & 0x8000 else v


def cdiv(a, b):
    q = abs(a) // abs(b)
    return q if (a < 0) == (b < 0) else -q


def cmod(a, b):
    return a - cdiv(a, b) * b


def label(module, name):
    info = oc.module_info(module)
    return next(l for l, f in info["functions"].items() if f["name"] == name)


class MscRand:
    def __init__(self, seed):
        self.h = seed & 0xffffffff

    def __call__(self):
        self.h = (self.h * 214013 + 2531011) & 0xffffffff
        return (self.h >> 16) & 0x7fff


def call(base, module, name, args=(), dgroup=None, keys=()):
    res = rf.run(base, label(module, name) if not name.startswith("sub_") else name, args, dgroup=dgroup,
                 script=rf.Script(keys=keys))
    if res["status"] != "return":
        FAILS.append(f"{module} {name}{tuple(args)}: status {res['status']} {res['fault']}")
    return res


def put(dg, at, data):
    dg = bytearray(dg)
    dg[at:at + len(data)] = data
    return bytes(dg)


def cstr(dg, at):
    return dg[at:dg.index(0, at)]


def common_library(base, module, rng):
    """MSC 5.1 SLIBCR routines linked into the module."""
    S1, S2, S3 = 0xf000, 0xf100, 0xf200
    for a, b in [(b"Hello", b"World"), (b"", b"x"), (b"abc", b"abc"), (b"abd", b"abc"), (b"ab", b"abc"),
                 (b"\xe9t\xe9", b"ete")]:
        dg = put(put(base.dgroup_bytes(), S1, a + b"\0" + b"\xaa" * 8), S2, b + b"\0")
        r = call(base, module, "_strlen", [S1], dg)
        check(f"{module} strlen({a})", r["result"]["ax"], len(a))
        r = call(base, module, "_strcpy", [S3, S1], dg)
        check(f"{module} strcpy({a})", (cstr(r["dgroup_out"], S3), r["result"]["ax"]), (a, S3))
        r = call(base, module, "_strcat", [S1, S2], dg)
        check(f"{module} strcat({a},{b})", (cstr(r["dgroup_out"], S1), r["result"]["ax"]), (a + b, S1))
        if "_strcmp" in {f["name"] for f in base.info["functions"].values()}:
            r = call(base, module, "_strcmp", [S1, S2], dg)
            got = s16(r["result"]["ax"])
            check(f"{module} strcmp({a},{b}) sign", (got > 0) - (got < 0), (a > b) - (a < b))
    for v in [0, 1, -1, 5, -32768, 32767, 1234, -77]:
        r = call(base, module, "_abs", [v])
        check(f"{module} abs({v})", s16(r["result"]["ax"]), abs(v) if v != -32768 else -32768)
    for v, radix in [(0, 10), (12345, 10), (-321, 10), (-1, 16), (255, 16), (7, 2), (-32768, 10)]:
        r = call(base, module, "_itoa", [v, S3, radix])
        want = str(v) if radix == 10 else format(v & 0xffff, {16: "x", 2: "b"}[radix])
        check(f"{module} itoa({v},{radix})", cstr(r["dgroup_out"], S3).decode(), want)
    if "_strupr" in {f["name"] for f in base.info["functions"].values()}:
        dg = put(base.dgroup_bytes(), S1, b"Mixed case 12 zZ\0")
        r = call(base, module, "_strupr", [S1], dg)
        check(f"{module} strupr", cstr(r["dgroup_out"], S1), b"MIXED CASE 12 ZZ")
    # srand / rand: find holdrand by diffing, then compare the LCG sequence.
    for seed in (1, 0x1234, 0x7fff, rng.randrange(0x10000)):
        r = call(base, module, "_srand", [seed])
        diff = [i for i in range(base.dsize) if r["dgroup_in"][i] != r["dgroup_out"][i]
                and not r["stack_mask"][0] <= i < r["stack_mask"][1]]
        dg = r["dgroup_out"]
        ref = MscRand(seed)
        for k in range(5):
            r = call(base, module, "_rand", [], dg)
            dg = r["dgroup_out"]
            check(f"{module} rand #{k} after srand({seed})", r["result"]["ax"], ref())
        hold = min(diff) if diff else None
        if hold is not None:
            check(f"{module} holdrand after 5 rands", struct.unpack_from("<I", dg, hold)[0], ref.h)


def code_tests(base, rng):
    common_library(base, "CODE", rng)
    for v, lo, hi in [(5, 0, 3), (-5, 0, 3), (2, 0, 3), (7, 7, 7), (-32768, -5, 5), (100, 50, 40), (0, 1, -1)]:
        r = call(base, "CODE", "_clamp", [v, lo, hi])
        want = lo if v < lo else v
        want = hi if want > hi else want
        check(f"clamp({v},{lo},{hi})", s16(r["result"]["ax"]), want)
    # random_below(n) = (int)((long)n * rand() >> 15) with the module's holdrand
    r = call(base, "CODE", "_srand", [4242])
    dg, ref = r["dgroup_out"], MscRand(4242)
    for n in (1, 2, 26, 100, 200, 32767, 7):
        r = call(base, "CODE", "_random_below", [n], dg)
        dg = r["dgroup_out"]
        check(f"random_below({n})", s16(r["result"]["ax"]), (n * ref()) >> 15)
    # check_solution: the rule of CODE 0000:18a4 over random tables
    for t in range(40):
        n = rng.choice((26, 26, 26, 20, 5, 0))
        plain = bytes(range(0x41, 0x41 + 26))
        cipher = bytes(rng.sample(plain, 26))
        guess = bytearray(b" " * 26)
        count = [rng.choice((0, 0, 1, 3, 9)) for _ in range(26)]
        right, wrong = rng.choice(((0.6, 0.1), (0.9, 0.0), (1.0, 0.0), (0.3, 0.0)))
        for b in range(26):
            c = cipher[b] - 0x41
            roll = rng.random()
            guess[c] = plain[b] if roll < right else (rng.choice(plain) if roll < right + wrong else 0x20)
        dg = base.dgroup_bytes()
        dg = put(dg, 0x105a, cipher)
        dg = put(dg, 0x1078, plain)
        dg = put(dg, 0x1096, bytes(guess))
        dg = put(dg, 0x1abe, struct.pack("<h", n))
        dg = put(dg, 0x1ac0, struct.pack("<26h", *count))
        p = a = d = 0
        for b in range(n):
            c = cipher[b] - 0x41
            if guess[c] != 0x20 and count[c] > 0:
                if guess[c] == plain[b]:
                    a += 1
                else:
                    d += 1
            if not count[c]:
                p += 1
        want = 1 if (cdiv(p, 2) + a > 0x13 and not d) else 0
        r = call(base, "CODE", "_check_solution", [], dg)
        check(f"check_solution table {t}", r["result"]["ax"], want)
        SOLVED[want] += 1
        check(f"check_solution table {t} no writes", r["dgroup_out"] == dg[:len(r["dgroup_out"])] or
              _only_stack(r), True)
    # draw_timer: carries seconds into minutes, writes hh:mm:ss into timer_text (111e), draws it
    for es, mins in [(0, 0), (59, 0), (60, 0), (3599, 1), (0x7fff, 100), (125, 1439), (-61, 5)]:
        dg = put(put(base.dgroup_bytes(), 0x1a7a, struct.pack("<h", es)), 0x1d90, struct.pack("<h", mins))
        r = call(base, "CODE", "_draw_timer", [], dg)
        out = r["dgroup_out"]
        m2 = s16(mins + cdiv(es, 60))
        e2 = cmod(es, 60)
        text = bytearray(out[0x111e:0x1126])
        want = bytearray(text)
        want[0] = (cdiv(cdiv(m2, 60), 10) + 0x30) & 0xff
        want[1] = (cmod(cdiv(m2, 60), 10) + 0x30) & 0xff
        want[3] = (cdiv(cmod(m2, 60), 10) + 0x30) & 0xff
        want[4] = (cmod(cmod(m2, 60), 10) + 0x30) & 0xff
        want[6] = (cdiv(e2, 10) + 0x30) & 0xff
        want[7] = (cmod(e2, 10) + 0x30) & 0xff
        check(f"draw_timer({es},{mins}) globals", (s16(struct.unpack_from("<H", out, 0x1d90)[0]),
                                                   s16(struct.unpack_from("<H", out, 0x1a7a)[0])), (m2, e2))
        check(f"draw_timer({es},{mins}) text", bytes(text), bytes(want))
        draws = [c for c in r["calls"] if c["target"] == "_draw_string"]
        check(f"draw_timer({es},{mins}) draw_string args", [d["args"][1:3] for d in draws], [[0x100, 1]])
        # the text behind the pointer, captured at call time (oracle_config.DRIVER_POINTERS)
        check(f"draw_timer({es},{mins}) draw_string text", [(d["args"][3]["ptr"], base64.b64decode(d["args"][3]["str"]))
                                                          for d in draws], [(0x111e, cstr(out, 0x111e))])
    # library cut: seed_random = srand(time(0) & 0x7fff) with time recorded as ONE call, no INT 21h beneath
    for clock in ((12, 0, 0), (23, 59, 59), (0, 0, 1)):
        r = rf.run(base, label("CODE", "_seed_random"), [], script=rf.Script(clock=clock))
        check(f"seed_random {clock} calls", [c["target"] for c in r["calls"]], ["_time"])
        t = r["calls"][0]["ret"]
        check(f"seed_random {clock} time = DX:AX", (r["calls"][0]["args"], t), ([0], r["inputs"][0]))
        h = struct.unpack_from("<I", r["dgroup_out"], 0x1982)[0]
        check(f"seed_random {clock} holdrand = srand(time & 0x7fff)", h, t & 0x7fff)
        check(f"seed_random {clock} random_seed", struct.unpack_from("<H", r["dgroup_out"], 0x1d76)[0], t & 0x7fff)
    # time(&t) writes the time_t through its pointer and records it as "out"
    caller_time = rf.run(base, label("CODE", "_time"), [0xf000], script=rf.Script())
    check("time(&t) runs the library for real when it is the function under test", caller_time["status"], "return")
    check("time(&t) value", struct.unpack_from("<I", caller_time["dgroup_out"], 0xf000)[0],
          caller_time["result"]["ax"] | caller_time["result"]["dx"] << 16)
    # __aNlmul (long multiply, callee pops) and __aNlshr (DX:AX >> CL)
    for x, y in [(3, 5), (-7, 9), (0x12345, 0x10), (-1, -1), (70000, 70000)]:
        args = list(struct.unpack("<4H", struct.pack("<ii", x, y)))
        r = call(base, "CODE", "__aNlmul", args)
        got = r["result"]["ax"] | r["result"]["dx"] << 16
        check(f"__aNlmul({x},{y})", got, (x * y) & 0xffffffff)
    for x, n in [(0x12345678, 4), (-0x10000, 1), (-1, 15), (0x7fffffff, 31)]:
        u = x & 0xffffffff
        res = rf.run(base, label("CODE", "__aNlshr"), [], regs=dict(ax=u & 0xffff, dx=u >> 16, cx=n),
                     script=rf.Script())
        got = res["result"]["ax"] | res["result"]["dx"] << 16
        check(f"__aNlshr({x:#x},{n})", got, (x >> n) & 0xffffffff)
    # _read_key: ASCII keys come back as the byte, extended keys as the scan word
    for key, want in [("a", 0x61), ("ESC", 0x1b), ("F1", 0x3b00), ("ENTER", 0x0d)]:
        r = call(base, "CODE", "_read_key", [], keys=[key])
        check(f"read_key({key})", r["result"]["ax"], want)


def docode_tests(rng):
    """Whole-session _DoCode from its captured entry: decoding sets shared+09 = 1 (the original route's
    result), ESC leaves it 0; practice mode returns next module 0x11."""
    import make_vectors as mv
    bases, caps = mv.load_states("CODE")
    c = next(c for c in caps if c["label"] == "sub_code_0000_0b5a" and c["route"] == "decode-v1")
    base = bases[("decode-v1", "start")]
    sp, args = mv.stack_args(c, base, base.info["functions"]["sub_code_0000_0b5a"])
    for keys, success in [(list("uahbrcsdffegdhwinlqmlnzoypirjsktmu") + ["ENTER"], 1), (["ESC"], 0),
                          (["u", "x", "ESC"], 0)]:
        r = rf.run(base, "sub_code_0000_0b5a", args, dgroup=c["dgroup"], shared=c["shared"], sp=sp,
                   script=rf.Script(keys=keys), budget=20_000_000, max_calls=200_000)
        check(f"DoCode {''.join(keys[:3])}.. status", r["status"], "return")
        check(f"DoCode {''.join(keys[:3])}.. shared+09 success", r["shared_out"][9], success)
        check(f"DoCode {''.join(keys[:3])}.. shared+00 next module", r["shared_out"][0], 0x11)


def vector_tests():
    """Generated vectors: realistic cases stay inside the typed model (oracle_config), pointer texts present."""
    import glob
    import json
    import oracle_config as cfg
    files = glob.glob(str(oc.OUT / "CODE" / "sub_*.json"))
    if not files:
        print("no CODE vectors: vector checks skipped")
        return
    realistic = fuzzed_real = texts = 0
    for path in files:
        v = json.loads(Path(path).read_text(encoding="utf-8"))
        for c in v["cases"]:
            draws = [call for call in c["calls"] if call["target"] == "_draw_string"]
            texts += len(draws)
            if draws:
                check(f"{v['name']} {c['id']} draw_string texts captured",
                      all(isinstance(d["args"][3], dict) and "str" in d["args"][3] for d in draws), True)
            for call in c["calls"]:
                # module cuts (oracle_config.MODULE_CUTS): the slots/INTs beneath never show outside them
                if (call["target"] == "sub_code_0000_0255" and v["name"] != "_fill_rect" and v["label"] != "sub_code_0000_0255") or (
                        call["target"] == "int21.11" and v["name"] != "_find_file_on_disk"):
                    FAILS.append(f"{v['name']} {c['id']}: {call['target']} beneath a cut routine")
                if call["target"] in ("int21.2a", "int21.2c") and v["name"] not in cfg.LIBRARY_CUTS["_time"]["family"] \
                        and v["name"] != "_time":
                    FAILS.append(f"{v['name']} {c['id']}: INT 21h clock call outside the _time cut")
            if not c.get("realistic"):
                continue
            realistic += 1
            fuzzed_real += "fuzz" in c["origin"]
            if "capture" in c["origin"] and "fuzz" not in c["origin"]:
                continue        # a real session state is realistic by definition
            dg = base64.b64decode(c["dgroup_in"])
            for off, (name, count, width, allowed, _) in cfg.GLOBALS["CODE"].items():
                for k in range(count):
                    raw = int.from_bytes(dg[off + k * width:off + (k + 1) * width], "little")
                    val = raw - (1 << 8 * width) if width == 2 and raw & 0x8000 else raw
                    ok = val in allowed[1] or raw in allowed[1] if allowed[0] == "set" else allowed[0] <= val <= allowed[1]
                    if not ok and _mutated(c["origin"], name):
                            FAILS.append(f"{v['name']} {c['id']}: realistic but {name}[{k}] = {val}")
    print(f"vector checks: {realistic} realistic cases ({fuzzed_real} fuzzed within the model), "
          f"{texts} draw_string texts")


def _mutated(origin, name):
    """Only fields the fuzzer touched must be in range (a captured state may hold any value)."""
    return name in origin


def driver_tests():
    """MGRAPHIC vectors: show_page(1) puts page 1 (segment from the page table) on the screen; a
    full-screen copy_rect from the screen port to the backup port writes the screen into page 1."""
    import json
    folder = oc.OUT / "MGRAPHIC"
    if not (folder / "_show_page.json").exists():
        print("no MGRAPHIC vectors: driver checks skipped")
        return

    def apply(base, runs, origin):
        out = bytearray(base)
        for k, v in runs.items():
            data = base64.b64decode(v)
            at = int(k, 16) - origin
            out[at:at + len(data)] = data
        return bytes(out)

    def region(runs, start, size):
        buf = bytearray(size)
        have = bytearray(size)
        for k, v in runs.items():
            data = base64.b64decode(v)
            for i, b in enumerate(data):
                at = int(k, 16) + i - start
                if 0 <= at < size:
                    buf[at], have[at] = b, 1
        return bytes(buf), all(have)

    v = json.loads((folder / "_show_page.json").read_text(encoding="utf-8"))
    for c in v["cases"]:
        pages = [int(p, 16) for p in c.get("segments", v["segments"])["pages"]]
        page = c["args"][0]
        video = apply(base64.b64decode(c["video_in"]), c["video_out"], 0)
        if page == 0 or pages[page] == 0xa000:
            check(f"show_page({page}) leaves the screen", c["video_out"], {})
            continue
        src, complete = region(c["far_in"], pages[page] * 16, 64000)
        check(f"show_page({page}) read all of page {page}", complete, True)
        check(f"show_page({page}) screen = page {page}", hashlib.sha1(video).hexdigest(), hashlib.sha1(src).hexdigest())
    v = json.loads((folder / "_copy_rect.json").read_text(encoding="utf-8"))
    full = [c for c in v["cases"] if c["args"][1:5] == [0, 0, 320, 200] and c["args"][6:8] == [0, 0]
            and c["args"][0] == 0x1250 and c["args"][5] == 0x1044
            and c.get("segments", {}).get("module", "CODE") == "CODE"]
    for c in full:
        pages = [int(p, 16) for p in c.get("segments", v["segments"])["pages"]]
        dst, complete = region(c["far_out"], pages[1] * 16, 64000)
        check(f"copy_rect screen -> page 1 ({c['id']}) writes all of page 1", complete, True)
        check(f"copy_rect screen -> page 1 ({c['id']})", hashlib.sha1(dst).hexdigest(),
              hashlib.sha1(base64.b64decode(c["video_in"])).hexdigest())
    draws = json.loads((folder / "_draw_string.json").read_text(encoding="utf-8"))["cases"]
    check("draw_string cases draw pixels", any(c["video_out"] for c in draws), True)
    print(f"driver checks: {len(full)} full-screen copy_rect cases, show_page and draw_string checked")


def _only_stack(res):
    lo, hi = res["stack_mask"]
    return all(res["dgroup_in"][i] == res["dgroup_out"][i] for i in range(len(res["dgroup_in"]))
               if not lo <= i < hi)


def game_tests(base, rng):
    common_library(base, "GAME", rng)
    for v, lo, hi in [(5, 1, 4), (-5, 1, 4), (2, 1, 4), (32767, -1, 0)]:
        r = call(base, "GAME", "_clamp", [v, lo, hi])
        want = lo if v < lo else v
        want = hi if want > hi else want
        check(f"GAME clamp({v},{lo},{hi})", s16(r["result"]["ax"]), want)
    for _ in range(30):
        a, b = rng.randrange(0x10000), rng.randrange(0x10000)
        r = call(base, "GAME", "_map_distance", [a, b])
        ld = abs((a & 0xf) - (b & 0xf))
        le = abs(((a & 0xf0) >> 4) - ((b & 0xf0) >> 4))
        want = ld * 2 + le if ld > le else le * 2 + ld
        check(f"GAME map_distance({a:#x},{b:#x})", s16(r["result"]["ax"]), want)
        check(f"GAME map_distance calls", r["calls"], [])


def cut_tests():
    """Module cuts are placed by module and address (oracle_config.MODULE_CUT_AT): each lands on a function,
    and BUG's fill cut is its 0602 (_fill_rect_port), not its own corner-variant _fill_rect 6f5a."""
    import oracle_config as cfg
    for module in cfg.MODULE_CUT_AT:
        cuts = cfg.cut_labels(module, oc.module_info(module)["functions"])
        check(f"{module} module cuts all placed", sorted(n for n in cuts.values() if n in cfg.MODULE_CUTS),
              sorted(cfg.MODULE_CUTS))
    bug = cfg.cut_labels("BUG", oc.module_info("BUG")["functions"])
    check("BUG fill cut at 0000:0602", bug.get("sub_bug_0000_0602"), "_fill_rect")
    check("BUG 6f5a (corner variant) not cut", bug.get("sub_bug_00e8_6f5a"), None)


def file_tests():
    """DOS file services and the MSC 5.1 file cuts of run_function.Script (README "DOS files")."""
    text = b"ab\r\ncd\r\nef\rg\x1ahidden"
    sc = rf.Script(files={"T.TXT": text})
    mem = {0x100: b"c:/dir/t.txt\0", 0x200: b"NONE.DAT\0"}
    read = lambda off, n: (mem.get(off, b"") + bytes(n))[:n]      # noqa: E731
    check("3Dh missing file", sc.interrupt(0x21, dict(ax=0x3d00, bx=0, cx=0, dx=0x200), read),
          dict(ax=2, cf=1, file="NONE.DAT"))
    h = sc.interrupt(0x21, dict(ax=0x3d00, bx=0, cx=0, dx=0x100), read)
    check("3Dh open", h, dict(ax=5, file="T.TXT"))
    r = sc.interrupt(0x21, dict(ax=0x3f00, bx=5, cx=4, dx=0), read)
    check("3Fh read", (r["ax"], base64.b64decode(r["data"]), r["file"]), (4, b"ab\r\n", "T.TXT"))
    check("42h seek from end", sc.interrupt(0x21, dict(ax=0x4202, bx=5, cx=0xffff, dx=0xfffe), read),
          dict(ax=len(text) - 2, dx=0, file="T.TXT"))
    check("40h on a read-only handle", sc.interrupt(0x21, dict(ax=0x4000, bx=5, cx=1, dx=0), read)["ax"], 5)
    check("3Eh close", sc.interrupt(0x21, dict(ax=0x3e00, bx=5, cx=0, dx=0), read), dict(file="T.TXT"))
    check("3Fh unknown handle", sc.interrupt(0x21, dict(ax=0x3f00, bx=5, cx=4, dx=0), read), dict(ax=6, cf=1))
    # _open in text mode, _read compaction, Ctrl-Z, the end-of-file flag cleared by _lseek
    cstr = lambda ptr: mem[ptr].split(b"\0")[0]                     # noqa: E731
    h, _ = sc.library("_open", [0x100, 0], cstr)
    check("_open text", (h, sc.last_file), (5, "T.TXT"))
    ret, out = sc.library("_read", [h, 0x300, 100], cstr)
    check("_read text: CR LF -> LF, lone CR kept, stops at Ctrl-Z", (ret, out[:ret]), (10, b"ab\ncd\nef\rg"))
    check("_read text: buffer past the count keeps the raw bytes", out[ret:], text[ret:])
    check("_read after Ctrl-Z", sc.library("_read", [h, 0x300, 100], cstr), (0, None))
    check("_lseek clears end of file", sc.library("_lseek", [h, 4, 0, 0], cstr)[0], 4)
    ret, out = sc.library("_read", [h, 0x300, 3], cstr)
    check("_read ending in CR before LF: CR dropped", out[:ret], b"cd")
    check("_read: the LF comes next", sc.library("_read", [h, 0x300, 1], cstr)[1], b"\n")
    sc.library("_lseek", [h, 2, 0, 0], cstr)
    check("_read n=1 on CR LF returns LF", sc.library("_read", [h, 0x300, 1], cstr)[1], b"\n")
    check("_lseek negative", sc.library("_lseek", [h, 0xfff0, 0xffff, 0], cstr)[0], 0xffffffff)
    hb, _ = sc.library("_open", [0x100, 0x8000], cstr)
    check("_read binary", sc.library("_read", [hb, 0x300, 100], cstr), (len(text), text))
    check("_open missing", sc.library("_open", [0x200, 0], cstr)[0], 0xffff)


def config_name_tests():
    """Every SCRIPTED / DONORS / BUDGETS entry names a function of its module (a typo would silently drop it)."""
    import oracle_config as cfg
    for table in (cfg.SCRIPTED, cfg.DONORS, cfg.BUDGETS, cfg.MAX_CALLS):
        for module, entries in table.items():
            names = {f["name"] for f in oc.module_info(module)["functions"].values()}
            for name in entries:
                check(f"{module} {name} exists", name in names, True)


def far_data_tests():
    """FAR_DATA segments lie between the last function and DGROUP (data only, no code is hidden from coverage)."""
    import oracle_config as cfg
    for module, segs in cfg.FAR_DATA.items():
        info = oc.module_info(module)
        code_end = max(f["linear"] + f["size"] for f in info["functions"].values() if f["kind"] != "data")
        for a, b in segs:
            check(f"{module} far data {a:04x}..{b:04x} after the code and up to DGROUP",
                  code_end <= a * 16 < b * 16 <= info["dgroup_frame"] * 16, True)


def tracked_retf_tests():
    """unicorn RETF under a read hook (README "Emulator workaround"): the CHASE picture loader runs its
    decoder on a stack inside DGROUP; tracked and untracked runs must agree."""
    import make_vectors as mv
    bases, caps = mv.load_states("CHASE")
    fz = mv.Fuzzer("CHASE", "sub_chase_00df_4488", bases, caps, random.Random(1), 0, 8, lambda *a: None, 3)
    seed = next(iter(fz.seeds()))
    a, b = (fz.run(seed, track=t) for t in (True, False))
    check("CHASE load_picture tracked = untracked", (a["status"], a.get("calls"), a.get("dgroup_out")),
          (b["status"], b.get("calls"), b.get("dgroup_out")))
    check("CHASE load_picture reads CHASE.PIC", any(c.get("file") == "CHASE.PIC" for c in b.get("calls", [])), True)


def main():
    rng = random.Random(7)
    cut_tests()
    file_tests()
    config_name_tests()
    far_data_tests()
    tracked_retf_tests()
    code = rf.Base.from_states(oc.STATES / "CODE" / "decode-v1", "wait")
    code_tests(code, rng)
    docode_tests(rng)
    vector_tests()
    driver_tests()
    print("CODE checks", CHECKS[0], "fails", len(FAILS), "check_solution outcomes 0/1:", SOLVED, flush=True)
    game = rf.Base.startup("GAME")
    before = CHECKS[0]
    game_tests(game, rng)
    print("GAME checks", CHECKS[0] - before, "(state: GAME crt0 to", game.info["main"] + ")")
    for f in FAILS:
        print("FAIL", f)
    print("total checks", CHECKS[0], "failures", len(FAILS))
    sys.exit(1 if FAILS else 0)


if __name__ == "__main__":
    main()
