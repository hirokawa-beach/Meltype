# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hirokawa-beach
"""日本郵便の全国 UTF-8 ZIP から、原文を保持した同梱用郵便番号辞書を再生成する。"""

import argparse
from collections import Counter, defaultdict
import csv
import datetime
import gzip
import hashlib
import io
import json
import os
from pathlib import Path
import re
import tempfile
import urllib.request
import zipfile

SOURCE_URL = "https://www.post.japanpost.jp/service/search/zipcode/download/utf/zip/utf_ken_all.zip"
TERMS_URL = "https://www.post.japanpost.jp/service/search/zipcode/download/readme.html"
SCHEMA_VERSION = 3
LABELS = {
    "normal": "通常の町域", "fallback": "以下に掲載がない場合", "kyoto": "京都の通り名・位置説明",
    "range": "丁目・番地などの範囲指定", "restricted": "除外・限定条件", "other": "その他の特殊表記",
}
OUTER = re.compile(r"(?P<town>[^（）()]+)(?:（(?P<wide>[^（）()]+)）|\((?P<ascii>[^（）()]+)\))")
LIMIT = re.compile(r"除く|以外|限る|のみ|その他|未満|以上|以下")
RANGE = re.compile(r"丁目|番地|番|地割|号|～|〜")
SAFE_RANGE = re.compile(r"[０-９0-9一二三四五六七八九十百千、・〜～－−\-丁目番地割第号の及びからまで\s]+")
DIRECTION = re.compile(r"上る|下る|上ル|下ル|[東西南北]入")
STREET_END = re.compile(r"(?:上る|下る|上ル|下ル|[東西南北]入(?:る|ル)?|[０-９0-9一二三四五六七八九十]+(?:丁目|筋目))$")
SIDE_END = re.compile(r"(?:[０-９0-9一二三四五六七八九十]+筋目|上る|下る|上ル|下ル)[東西南北]$")
SIDE_BLOCK = re.compile(r"[東西南北][０-９0-9一二三四五六七八九十]+丁目$")


def classify(prefecture, city, town):
    """分類と整形は別。解釈できない表記の基本住所は原文のままにする。"""
    outer = OUTER.fullmatch(town)
    base = outer["town"] if outer else ""
    body = (outer["wide"] or outer["ascii"]) if outer else ""
    category, normalized, streets = "normal", "", []
    if town == "以下に掲載がない場合":
        category = "fallback"
    elif re.fullmatch(r".+[市区町村](?:の次に番地がくる場合|一円)", town):
        category = "municipality"
    elif any(c in town for c in "（）()"):
        if LIMIT.search(town):
            category = "restricted"
            # 小字の列挙に部分的な除外を含むものは原文保持。単純な数値条件・その他だけ分離。
            limit = re.fullmatch(r"(.+?)(?:を除く|以外|に限る|のみ|以上|以下|未満)", body)
            if outer and (body in ("その他", "次のビルを除く") or limit and SAFE_RANGE.fullmatch(limit[1]) and re.search(r"[０-９0-9一二三四五六七八九十]", limit[1])):
                normalized = base
        elif prefecture == "京都府" and city.startswith("京都市") and DIRECTION.search(town) and re.search(r"通|道|筋|大橋|之間|ノ間|の間", town):
            category = "kyoto"
            if outer:
                tokens = body.split("、")
                parsed = []
                for token in tokens:
                    # 道路名の列挙に見えるだけでは展開しない。方向表現と終端を検査する。
                    if not token or LIMIT.search(token) or any(c in token for c in "（）()〜～、,;"):
                        break
                    direction = DIRECTION.search(token)
                    side = SIDE_BLOCK.search(token)
                    if not (direction and direction.start() > 0 or side and side.start() > 0): break
                    join = bool(direction and STREET_END.search(token))
                    if not join and not (direction and SIDE_END.search(token) or side): break
                    pair = (token, join)
                    if pair not in parsed: parsed.append(pair)
                else:
                    if parsed:
                        normalized, streets = base, parsed
        elif RANGE.search(town) or outer and SAFE_RANGE.fullmatch(body) and re.search(r"[０-９0-9]", body):
            category = "range"
            # 数字と既知の範囲語のみ。別の地名・建物・方角が入る説明は短縮しない。
            if outer and SAFE_RANGE.fullmatch(body) and re.search(r"[０-９0-9一二三四五六七八九十]", body) and re.search(r"丁目|番地|番|地割|号", body): normalized = base
        else:
            category = "other"
    elif "〜" in town or "～" in town:
        category = "range"
    elif re.search(r"場合|一円|掲載", town):
        category = "other"
    return category, normalized, streets


def read_rows(archive):
    with zipfile.ZipFile(io.BytesIO(archive)) as z:
        names = [n for n in z.namelist() if n.lower().endswith(".csv")]
        if len(names) != 1 or z.getinfo(names[0]).file_size > 64 * 1024 * 1024:
            raise ValueError("Expected one nationwide CSV smaller than 64 MiB")
        raw = z.read(names[0]).decode("utf-8-sig")
    rows = list(csv.reader(io.StringIO(raw), strict=True))
    if not rows: raise ValueError("Empty postal data")
    for i, row in enumerate(rows, 1):
        if (len(row) != 15 or not re.fullmatch(r"[0-9]{7}", row[2]) or not all(row[6:9])
                or any("\t" in f or "\r" in f or "\n" in f or "\0" in f for f in row)):
            raise ValueError(f"Invalid postal record at CSV row {i}")
    return rows


FLOOR = re.compile(r"((?:[^（）()]+|（[^（）()]+）|\([^（）()]+\))+)(?:（([０-９0-9]+)階）|\(([０-９0-9]+)階\)|（地階・階層不明）|\(地階・階層不明\))")


def building_parts(rows):
    """同じ地域の一般町域を照合する。未知の階表記や未照合の建物は原文保持。"""
    towns = defaultdict(set)
    for row in rows:
        kind, base, _ = classify(*row[6:9])
        if kind == "normal" or base and kind in ("range", "restricted"):
            towns[tuple(row[6:8])].add(base or row[8])
    result = {}
    for row in rows:
        match = FLOOR.fullmatch(row[8])
        if not match: continue
        address = match[1]
        candidates = [t for t in towns[tuple(row[6:8])] if address.startswith(t) and len(t) < len(address)]
        if not candidates: continue
        # 天満 / 天満橋のような接頭辞の重なりでは、最長の登録町域を採る。
        town = max(candidates, key=len)
        building = address[len(town):]
        # 全角 ASCII だけを変換。日本語やローマ数字・カナは変えない。
        if re.search(r"[Ａ-Ｚａ-ｚA-Za-z]", building):
            building = building.translate({c: c - 0xFEE0 for c in range(0xFF01, 0xFF5F)})
        floor = (match[2] or match[3] or "").translate({ord(c): ord("0") + i for i, c in enumerate("０１２３４５６７８９")})
        result[tuple(row[6:9])] = (town, building, floor)
    return result


def analyze(rows):
    counts = Counter({k: 0 for k in LABELS})
    examples = defaultdict(list)
    subtypes = Counter()
    streets = 0
    buildings = building_parts(rows)
    for row in rows:
        kind, base, paths = classify(*row[6:9])
        category = "other" if kind == "municipality" else kind
        counts[category] += 1
        if len(examples[category]) < 5:
            examples[category].append({"code": row[2], "region": row[6] + row[7], "town": row[8], "base_town": base, "street_count": len(paths)})
        if tuple(row[6:9]) in buildings:
            parts = buildings[tuple(row[6:9])]
            subtypes["building_floor_separated" if parts[2] else "building_unknown_floor_separated"] += 1
        elif kind == "municipality": subtypes["municipality_only"] += 1
        elif kind == "other" and re.search(r"階|地階", row[8]): subtypes["building_floor_retained"] += 1
        elif kind == "other" and re.fullmatch(r".+[（(][東西南北][）)]", row[8]): subtypes["direction_name_retained"] += 1
        if tuple(row[6:9]) in buildings:
            pass
        elif base:
            subtypes[kind + "_separated"] += 1
        elif kind in ("kyoto", "range", "restricted", "other"):
            subtypes[kind + "_retained"] += 1
        streets += len(paths)
        subtypes["street_without_town_join"] += sum(not join for _, join in paths)
    return {"records": len(rows), "categories": dict(counts), "subtypes": dict(subtypes),
            "street_descriptions": streets, "examples": dict(examples), "labels": LABELS}


def build_dictionary(archive):
    rows = read_rows(archive)
    entries = {}
    buildings = building_parts(rows)
    for row in rows:
        kind, base, streets = classify(*row[6:9])
        paths = json.dumps(streets, ensure_ascii=False, separators=(",", ":")) if streets else ""
        building = buildings.get(tuple(row[6:9]))
        if building: kind = "building"
        structured = json.dumps(building, ensure_ascii=False, separators=(",", ":")) if building else ""
        # 7 列: 番号、都道府県+市区町村、町域原文、種別、安全に分離した町名、通り名+結合可否、建物の町域+名称+階数。
        fields = (row[6] + row[7], row[8], kind, base, paths, structured)
        entries.setdefault(row[2], set()).add(fields)
    lines = [code + "\t" + "\t".join(fields) + "\n" for code in sorted(entries) for fields in sorted(entries[code])]
    payload = "".join(lines).encode("utf-8")
    compressed = io.BytesIO()
    with gzip.GzipFile(fileobj=compressed, mode="wb", filename="", mtime=0) as gz:
        gz.write(payload)
    report = analyze(rows)
    return compressed.getvalue(), {
        "source_records": len(rows), "postal_codes": len(entries), "address_records": len(lines),
        "multiple_address_codes": sum(len(v) > 1 for v in entries.values()),
        "max_records_per_code": max(map(len, entries.values())), "uncompressed_bytes": len(payload),
        "classification": report["categories"], "classification_details": report["subtypes"],
        "street_descriptions": report["street_descriptions"],
    }


def replace_files(files):
    pending = []
    try:
        for destination, content in files:
            destination.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(dir=destination.parent, delete=False) as f:
                pending.append((Path(f.name), destination))
                f.write(content)
        for temporary, destination in pending: os.replace(temporary, destination)
    finally:
        for temporary, _ in pending: temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, help="保存した全国 UTF-8 ZIP。省略時だけ公式サイトから取得")
    parser.add_argument("--source-date", required=True, help="公式ページの更新日 (YYYY-MM-DD)")
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).resolve().parents[1] / "dictionaries")
    parser.add_argument("--report", type=Path, help="分類件数・原文の代表例を JSON で保存する")
    args = parser.parse_args()
    source_date = datetime.date.fromisoformat(args.source_date).isoformat()
    if args.input: archive = args.input.read_bytes()
    else:
        with urllib.request.urlopen(SOURCE_URL, timeout=60) as response: archive = response.read(32 * 1024 * 1024 + 1)
    if len(archive) > 32 * 1024 * 1024: raise ValueError("ZIP exceeds 32 MiB")
    compressed, stats = build_dictionary(archive)
    if stats["source_records"] < 100000: raise ValueError("Nationwide postal data is unexpectedly small; existing dictionary was not replaced")
    metadata = {"schema_version": SCHEMA_VERSION,
                "source": "日本郵便株式会社 住所の郵便番号（1レコード1行、UTF-8形式）",
                "source_url": SOURCE_URL, "terms_url": TERMS_URL, "source_date": source_date,
                "source_sha256": hashlib.sha256(archive).hexdigest(), "dictionary_sha256": hashlib.sha256(compressed).hexdigest(),
                "compressed_bytes": len(compressed), **stats}
    encoded = (json.dumps(metadata, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    files = [(args.output_dir / "postal-codes.tsv.gz", compressed), (args.output_dir / "postal-codes.metadata.json", encoded)]
    if args.report:
        report = {"source_date": source_date, "source_sha256": metadata["source_sha256"], **analyze(read_rows(archive))}
        files.append((args.report, (json.dumps(report, ensure_ascii=False, indent=2) + "\n").encode("utf-8")))
    replace_files(files)
    print(json.dumps(metadata, ensure_ascii=False, indent=2))


if __name__ == "__main__": main()
