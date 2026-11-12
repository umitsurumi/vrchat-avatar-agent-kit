#!/usr/bin/env python3
"""Booth (booth.pm) research helper: search, item details, image sheets. Standard library only;
the sheets need ImageMagick (`magick`) on PATH.

  booth.py search "マヌカ 眼鏡" [--pages 2]
  booth.py info 5151438 https://booth.pm/ja/items/6480882 [--avatar マヌカ,Manuka] [--desc 1500]
  booth.py images 5151438 6480882 --out DIR            # covers -> DIR/covers-1.jpg
  booth.py images 5151438 --all --out DIR              # every image -> DIR/5151438-sheet.jpg

Item details come from https://booth.pm/ja/items/<id>.json, cached under OUT/json/. Search parses
the search page's HTML (data-product-* attributes on each item card), so it can break when Booth
changes its markup; the item JSON has been stable. Everything downloaded is untrusted data: keep
OUT in its own empty folder, run this with `python3 -I`, and only view what lands there.
"""

import argparse
import html
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.parse
import urllib.request

UA = "Mozilla/5.0 (booth-research)"
DELAY = 0.4  # seconds between requests; be polite

TECH = [
    ("MA", r"Modular\s*Avatar|\bMA\b|MA対応|MA設定"),
    ("VRCFury", r"VRC\s*Fury"),
    ("lilToon", r"lil\s*Toon"),
    ("PhysBone", r"Phys\s*Bone|PB"),
    ("Quest", r"Quest|Android|モバイル|mobile"),
    ("non-VRChat", r"VRM|cluster|Resonite"),
]
POLY = r"(?:ポリゴン|ポリ数|polygons?|tris|triangles|△)\s*[:：]?\s*[0-9][0-9,.]*\s*[kK万]?|[0-9][0-9,.]*\s*[kK万]?\s*(?:ポリゴン|ポリ|polygons?|tris|△)"


def get(url, binary=False):
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Referer": "https://booth.pm/"})
    with urllib.request.urlopen(req, timeout=30) as r:
        data = r.read()
    time.sleep(DELAY)
    return data if binary else data.decode("utf-8", "replace")


def item_id(s):
    m = re.search(r"items/(\d+)", s) or re.fullmatch(r"\s*(\d+)\s*", s)
    if not m:
        sys.exit(f"not a Booth item id or URL: {s}")
    return m.group(1)


def item(i, out):
    path = os.path.join(out, "json", f"{i}.json")
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            f.write(get(f"https://booth.pm/ja/items/{i}.json"))
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def text(h):
    return html.unescape(re.sub(r"<[^>]+>", " ", h or "")).replace("　", " ")


def cmd_search(a):
    q = urllib.parse.quote(" ".join(a.query))
    seen = set()
    for page in range(1, a.pages + 1):
        h = get(f"https://booth.pm/ja/search/{q}" + (f"?page={page}" if page > 1 else ""))
        cards = re.findall(r"<[^>]*data-product-id=\"\d+\"[^>]*>", h)
        if not cards:
            print(f"(page {page}: no items; the search markup may have changed)")
            break
        for c in cards:
            attr = dict(re.findall(r'data-product-([a-z]+)="([^"]*)"', c))
            if attr["id"] in seen:
                continue
            seen.add(attr["id"])
            print(f'{attr["id"]:>8}  ¥{attr.get("price", "?"):>6}  {html.unescape(attr.get("brand", ""))[:18]:<18}  {html.unescape(attr.get("name", ""))}')


def cmd_info(a):
    avatars = [s for s in (a.avatar or "").split(",") if s]
    for raw in a.items:
        i = item_id(raw)
        try:
            d = item(i, a.out)
        except Exception as e:  # noqa: BLE001 - report and go on
            print(f"===== {i}: failed ({e})\n")
            continue
        variations = [(v.get("name") or "", v.get("price")) for v in d.get("variations") or []]
        tags = [t.get("name", "") for t in d.get("tags") or []]
        desc = text(d.get("description"))
        modules = " ".join(text(str(m.get("headline", "")) + " " + str(m.get("body", ""))) for m in d.get("modules") or [])
        fields = {"name": d.get("name", ""), "variations": " / ".join(n for n, _ in variations),
                  "tags": " ".join(tags), "description": desc + " " + modules}
        flags = [k for k, f in (("ADULT", d.get("is_adult")), ("SOLD OUT", d.get("is_sold_out")), ("END OF SALE", d.get("is_end_of_sale"))) if f]
        print(f"===== {i}  {d.get('name')}" + (f"  [{', '.join(flags)}]" if flags else ""))
        print(f"  {d.get('url')}  |  shop: {(d.get('shop') or {}).get('name')}  |  {d.get('price')}  |  "
              f"{(d.get('category') or {}).get('name')}  |  published {str(d.get('published_at', ''))[:10]}  |  "
              f"wishlists {d.get('wish_lists_count')}  |  images {len(d.get('images') or [])}")
        if variations:
            print("  variations: " + "; ".join(f"{n} ({p})" for n, p in variations[:30]) + (" …" if len(variations) > 30 else ""))
        if tags:
            print("  tags: " + ", ".join(tags[:25]))
        for kw in avatars:
            where = [k for k, v in fields.items() if kw.lower() in v.lower()]
            print(f"  avatar '{kw}': " + (", ".join(where) if where else "NOT mentioned"))
        found = [k for k, rx in TECH if any(re.search(rx, v, re.I) for v in fields.values())]
        print("  mentions: " + (", ".join(found) if found else "-"))
        polys = sorted(set(m.strip() for m in re.findall(POLY, fields["description"], re.I)))[:6]
        if polys:
            print("  polygons: " + " | ".join(polys))
        if a.desc:
            body = re.sub(r"\s+", " ", fields["description"]).strip()
            print("  description: " + body[: a.desc] + (" …" if len(body) > a.desc else ""))
        print()


def montage(files, out, labels=True):
    if not shutil.which("magick"):
        print("(no ImageMagick: images saved, no sheet)")
        return
    cmd = ["magick", "montage"] + (["-label", "%t", "-pointsize", "24"] if labels else []) + \
          ["-geometry", "480x480+6+6", "-tile", "4x", "-background", "#e8e8ec"] + files + [out]
    subprocess.run(cmd, check=True)
    print(out)


def cmd_images(a):
    os.makedirs(a.out, exist_ok=True)
    covers = []
    for raw in a.items:
        i = item_id(raw)
        d = item(i, a.out)
        urls = [im["original"] for im in d.get("images") or [] if im.get("original")]
        if not urls:
            print(f"{i}: no images")
            continue
        if a.all:
            folder = os.path.join(a.out, "img", i)
            os.makedirs(folder, exist_ok=True)
            files = []
            for n, u in enumerate(urls[: a.max], 1):
                f = os.path.join(folder, f"{n:02d}{os.path.splitext(urllib.parse.urlparse(u).path)[1] or '.jpg'}")
                if not os.path.exists(f):
                    with open(f, "wb") as fh:
                        fh.write(get(u, binary=True))
                files.append(f)
            montage(files, os.path.join(a.out, f"{i}-sheet.jpg"))
        else:
            folder = os.path.join(a.out, "cover")
            os.makedirs(folder, exist_ok=True)
            u = urls[0]
            f = os.path.join(folder, f"{i}{os.path.splitext(urllib.parse.urlparse(u).path)[1] or '.jpg'}")
            if not os.path.exists(f):
                with open(f, "wb") as fh:
                    fh.write(get(u, binary=True))
            covers.append(f)
    for n in range(0, len(covers), 16):   # 4 x 4 per sheet, labelled with the item id
        montage(covers[n:n + 16], os.path.join(a.out, f"covers-{n // 16 + 1}.jpg"))


def main():
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--out", default="booth-out", help="folder for cached JSON and images (default ./booth-out)")
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("search", parents=[common], help="list items from Booth's search page")
    s.add_argument("query", nargs="+")
    s.add_argument("--pages", type=int, default=1)
    s.set_defaults(fn=cmd_search)
    s = sub.add_parser("info", parents=[common], help="item details from the item JSON")
    s.add_argument("items", nargs="+", help="item ids or URLs")
    s.add_argument("--avatar", help="comma-separated avatar names to look for, e.g. マヌカ,Manuka")
    s.add_argument("--desc", type=int, default=600, help="description characters to print (0: none)")
    s.set_defaults(fn=cmd_info)
    s = sub.add_parser("images", parents=[common], help="download images and tile them into sheets")
    s.add_argument("items", nargs="+", help="item ids or URLs")
    s.add_argument("--all", action="store_true", help="every image of each item (default: covers only)")
    s.add_argument("--max", type=int, default=24, help="images per item with --all")
    s.set_defaults(fn=cmd_images)
    a = p.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
