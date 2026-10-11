# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hirokawa-beach

import csv
import gzip
import importlib.util
import io
from pathlib import Path
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("postal_generator", Path(__file__).with_name("generate-postal-codes.py"))
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


def archive(rows):
    text = io.StringIO(newline="")
    csv.writer(text).writerows(rows)
    result = io.BytesIO()
    with zipfile.ZipFile(result, "w") as z:
        z.writestr("utf_ken_all.csv", text.getvalue().encode("utf-8"))
    return result.getvalue()


def row(code, town):
    return ["13101", "100", code, "トウキョウト", "チヨダク", "", "東京都", "千代田区", town, "0", "0", "0", "0", "0", "0"]


class PostalGeneratorTests(unittest.TestCase):
    def test_multiple_addresses_and_duplicates(self):
        result, stats = generator.build_dictionary(archive([row("0010001", "甲"), row("0010001", "乙"), row("0010001", "甲")]))
        self.assertEqual(stats["address_candidates"], 2)
        self.assertEqual(stats["multiple_address_codes"], 1)
        self.assertEqual(set(gzip.decompress(result).decode().splitlines()), {"0010001\t東京都千代田区甲\t住所", "0010001\t東京都千代田区乙\t住所"})

    def test_placeholders_are_not_inserted_as_town_names(self):
        result, _ = generator.build_dictionary(archive([row("1000000", "以下に掲載がない場合"), row("1000001", "千代田区の次に番地がくる場合"), row("1000002", "千代田区一円")]))
        text = gzip.decompress(result).decode()
        self.assertNotIn("以下に掲載", text)
        self.assertNotIn("千代田区一円", text)
        self.assertIn("1000000\t東京都千代田区\t町域未特定", text)
        self.assertIn("1000001\t東京都千代田区\t市区町村の次に番地", text)

    def test_conditions_and_csv_commas_are_preserved(self):
        result, _ = generator.build_dictionary(archive([row("1000001", "千代田（１、２丁目）"), row("1000002", "千代田(1,2)")]))
        text = gzip.decompress(result).decode()
        self.assertIn("千代田（１、２丁目）\t条件付き住所", text)
        self.assertIn("千代田(1,2)\t条件付き住所", text)

    def test_generation_is_deterministic_and_order_independent(self):
        rows = [row("1000002", "乙"), row("1000001", "甲")]
        self.assertEqual(generator.build_dictionary(archive(rows)), generator.build_dictionary(archive(rows[::-1])))

    def test_rejects_invalid_records_and_empty_data(self):
        for rows in ([], [row("100000", "千代田")], [row("１０００００１", "千代田")], [["1000001"]], [row("1000001", "町\n域")]):
            with self.subTest(rows=rows), self.assertRaises(ValueError):
                generator.build_dictionary(archive(rows))


if __name__ == "__main__":
    unittest.main()
