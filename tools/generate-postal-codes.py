# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hirokawa-beach
"""日本郵便の全国 UTF-8 ZIP から、同梱用の郵便番号辞書を再生成する。"""

import argparse
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


def build_dictionary(archive):
    with zipfile.ZipFile(io.BytesIO(archive)) as z:
        names = [n for n in z.namelist() if n.lower().endswith(".csv")]
        if len(names) != 1 or z.getinfo(names[0]).file_size > 64 * 1024 * 1024:
            raise ValueError("Expected one nationwide CSV smaller than 64 MiB")
        raw = z.read(names[0]).decode("utf-8-sig")
    entries = {}
    count = 0
    for row in csv.reader(io.StringIO(raw), strict=True):
        count += 1
        if (len(row) != 15 or not re.fullmatch(r"[0-9]{7}", row[2])
                or not row[6] or not row[7] or not row[8]
                or any("\t" in f or "\r" in f or "\n" in f for f in row)):
            raise ValueError(f"Invalid postal record at CSV row {count}")
        town = row[8]
        note = "住所"
        if town == "以下に掲載がない場合":
            town, note = "", "町域未特定"
        elif re.fullmatch(r".+[市区町村]の次に番地がくる場合", town):
            town, note = "", "市区町村の次に番地"
        elif re.fullmatch(r".+[市区町村]一円", town):
            town, note = "", "町域なし"
        elif "（" in town or "(" in town:
            # 範囲指定・階・京都の通り名等を一律に削ると、住所を誤って特定してしまう。
            note = "条件付き住所"
        text = row[6] + row[7] + town
        entries.setdefault(row[2], set()).add((text, note))
    if not count:
        raise ValueError("Empty postal data")
    lines = [f"{code}\t{text}\t{note}\n" for code in sorted(entries)
             for text, note in sorted(entries[code])]
    payload = "".join(lines).encode("utf-8")
    compressed = io.BytesIO()
    with gzip.GzipFile(fileobj=compressed, mode="wb", filename="", mtime=0) as gz:
        gz.write(payload)
    return compressed.getvalue(), {
        "source_records": count,
        "postal_codes": len(entries),
        "address_candidates": len(lines),
        "multiple_address_codes": sum(len(v) > 1 for v in entries.values()),
        "max_candidates_per_code": max(map(len, entries.values())),
        "uncompressed_bytes": len(payload),
    }


def replace_files(files):
    # 解析・検証・圧縮がすべて成功してから書く。生成失敗で既存辞書を壊さない。
    pending = []
    try:
        for destination, content in files:
            destination.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(dir=destination.parent, delete=False) as f:
                pending.append((Path(f.name), destination))
                f.write(content)
        for temporary, destination in pending:
            os.replace(temporary, destination)
    finally:
        for temporary, _ in pending:
            temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, help="保存した全国 UTF-8 ZIP。省略時だけ公式サイトから取得")
    parser.add_argument("--source-date", required=True, help="公式ページの更新日 (YYYY-MM-DD)。取得日とは別")
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).resolve().parents[1] / "dictionaries")
    args = parser.parse_args()
    source_date = datetime.date.fromisoformat(args.source_date).isoformat()
    if args.input:
        archive = args.input.read_bytes()
    else:
        with urllib.request.urlopen(SOURCE_URL, timeout=60) as response:
            archive = response.read(32 * 1024 * 1024 + 1)
    if len(archive) > 32 * 1024 * 1024:
        raise ValueError("ZIP exceeds 32 MiB")
    compressed, stats = build_dictionary(archive)
    if stats["source_records"] < 100000:
        raise ValueError("Nationwide postal data is unexpectedly small; existing dictionary was not replaced")
    metadata = {
        "schema_version": 1,
        "source": "日本郵便株式会社 住所の郵便番号（1レコード1行、UTF-8形式）",
        "source_url": SOURCE_URL,
        "terms_url": TERMS_URL,
        "source_date": source_date,
        "source_sha256": hashlib.sha256(archive).hexdigest(),
        "dictionary_sha256": hashlib.sha256(compressed).hexdigest(),
        "compressed_bytes": len(compressed),
        **stats,
    }
    encoded = (json.dumps(metadata, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    replace_files([(args.output_dir / "postal-codes.tsv.gz", compressed),
                   (args.output_dir / "postal-codes.metadata.json", encoded)])
    print(json.dumps(metadata, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
