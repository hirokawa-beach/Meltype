# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hirokawa-beach

import csv
import gzip
import importlib.util
import io
import json
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
    with zipfile.ZipFile(result, "w") as z: z.writestr("utf_ken_all.csv", text.getvalue().encode("utf-8"))
    return result.getvalue()


def row(code, town, prefecture="東京都", city="千代田区", koaza=False):
    return ["13101", "100", code, "トウキョウト", "チヨダク", "", prefecture, city, town, "0", "1" if koaza else "0", "0", "0", "0", "0"]


def records(rows):
    payload, stats = generator.build_dictionary(archive(rows))
    return [line.split("\t") for line in gzip.decompress(payload).decode().splitlines()], stats


class PostalGeneratorTests(unittest.TestCase):
    def test_multiple_addresses_and_exact_duplicates(self):
        data, stats = records([row("0010001", "甲"), row("0010001", "乙"), row("0010001", "甲")])
        self.assertEqual(stats["address_records"], 2)
        self.assertEqual(stats["multiple_address_codes"], 1)
        self.assertEqual({r[2] for r in data}, {"甲", "乙"})
        self.assertTrue(all(r[0] == "0010001" for r in data))

    def test_placeholders_keep_original_but_use_municipality(self):
        data, _ = records([row("1000000", "以下に掲載がない場合"), row("1000001", "千代田区の次に番地がくる場合"), row("1000002", "千代田区一円")])
        self.assertEqual([r[3] for r in data], ["fallback", "municipality", "municipality"])
        self.assertEqual(data[0][2], "以下に掲載がない場合")
        self.assertTrue(all(not r[4] for r in data))

    def test_kyoto_multiple_streets_are_structured(self):
        town = "北町（上の下立売通天神道西入上る、上の下立売通御前西入）"
        data, _ = records([row("6028368", town, "京都府", "京都市上京区")])
        self.assertEqual(data[0][2], town)
        self.assertEqual(data[0][3:5], ["kyoto", "北町"])
        self.assertEqual(json.loads(data[0][5]), [["上の下立売通天神道西入上る", True], ["上の下立売通御前西入", True]])

    def test_uncertain_street_join_is_disabled(self):
        town = "七軒町（三条大橋東４丁目、三条大橋東入４丁目）"
        data, _ = records([row("6050011", town, "京都府", "京都市東山区")])
        self.assertEqual(json.loads(data[0][5]), [["三条大橋東４丁目", False], ["三条大橋東入４丁目", True]])

    def test_unknown_kyoto_description_does_not_partially_expand(self):
        town = "北町（丸太町通大宮東入、位置不明の説明）"
        data, _ = records([row("6028368", town, "京都府", "京都市上京区")])
        self.assertEqual(data[0][2], town)
        self.assertEqual(data[0][4:], ["", "", "", ""])

    def test_ranges_and_exclusions_are_not_streets(self):
        for town, category, base in [("大通西（１〜１９丁目）", "range", "大通西"), ("藤野（４００、４００−２番地）", "range", "藤野"), ("曙町（１丁目を除く）", "restricted", "曙町"), ("常盤（その他）", "restricted", "常盤")]:
            with self.subTest(town=town):
                data, _ = records([row("1000001", town)])
                self.assertEqual(data[0][2], town)
                self.assertEqual(data[0][3:5], [category, base])
                self.assertEqual(data[0][5], "")

    def test_nested_or_named_conditions_are_retained(self):
        for town in ["甲（乙（１番地）を除く）", "犬落瀬（内金矢、下久保「１７４を除く」）", "音江町（国見２３〜２９８番地）", "俣落（１２３−１、４５６−２）", "南郷通（南）", "霞が関ビル（１階）", "大桑町（ア、イ、ウ）", "甲（閉じ括弧がない"]:
            with self.subTest(town=town):
                data, _ = records([row("1000001", town)])
                self.assertEqual(data[0][2], town)
                self.assertEqual(data[0][4:], ["", "", "", ""])

    def test_same_town_different_codes_and_conditions_are_not_merged(self):
        data, stats = records([row("0600001", "北一条西（１〜１９丁目）"), row("0640821", "北一条西（２０〜２８丁目）"), row("0600001", "北一条西（３０丁目）")])
        self.assertEqual(stats["postal_codes"], 2)
        self.assertEqual(len(data), 3)
        self.assertEqual([r[0] for r in data].count("0600001"), 2)

    def test_source_towns_and_regions_survive_roundtrip(self):
        source = [row("1000001", "千代田"), row("1000002", "千代田(1,2)"), row("1000003", "甲（乙（１番地）を除く）"), row("6028368", "北町（上の下立売通御前西入）", "京都府", "京都市上京区")]
        data, _ = records(source)
        self.assertEqual({(r[2], r[6] + r[7], r[8]) for r in source}, {(r[0], r[1], r[2]) for r in data})

    def test_generation_and_classification_are_deterministic(self):
        rows = [row("1000002", "乙"), row("1000001", "甲")]
        first, first_stats = generator.build_dictionary(archive(rows))
        second, second_stats = generator.build_dictionary(archive(rows[::-1]))
        self.assertEqual(first, second)
        self.assertEqual(first_stats, second_stats)
        self.assertEqual(sum(first_stats["classification"].values()), len(rows))

    def test_buildings_keep_source_and_separate_floor_and_english(self):
        source = [row("1000013", "霞が関（次のビルを除く）"), row("1006090", "霞が関霞が関ビル（地階・階層不明）"), row("1006036", "霞が関霞が関ビル（３６階）"), row("1006001", "霞が関ＳＳ３０ビル(1階)")]
        data, stats = records(source)
        by_code = {r[0]: r for r in data}
        self.assertEqual(by_code["1000013"][4], "霞が関")
        self.assertEqual(json.loads(by_code["1006090"][6]), ["霞が関", "霞が関ビル", ""])
        self.assertEqual(json.loads(by_code["1006036"][6]), ["霞が関", "霞が関ビル", "36"])
        self.assertEqual(json.loads(by_code["1006001"][6]), ["霞が関", "SS30ビル", "1"])
        self.assertEqual(by_code["1006036"][2], source[2][8])
        self.assertEqual(stats["classification_details"]["building_floor_separated"], 2)
        first, first_stats = generator.build_dictionary(archive(source))
        second, second_stats = generator.build_dictionary(archive(source[::-1]))
        self.assertEqual(first, second)
        self.assertEqual(first_stats, second_stats)

    def test_buildings_require_matching_locality_and_known_floor_format(self):
        source = [row("1000000", "天満"), row("1000001", "天満橋"), row("1000002", "天満橋ＯＡＰタワー（１２階）"), row("1000003", "未知町ビル（１２階）"), row("1000004", "天満橋ビル（地下１階）"), row("1000005", "天満橋ビル（１階、２階）"), row("1000006", "天満橋ビル（北）"), row("1000007", "天満橋ビル（北棟）（１階）"), row("1000008", "天満橋ビル（北棟（１階）")]
        data, _ = records(source)
        self.assertEqual(json.loads(data[2][6]), ["天満橋", "OAPタワー", "12"])
        self.assertTrue(all(r[6] == "" for r in data[3:7] + data[8:]))
        self.assertEqual(json.loads(data[7][6]), ["天満橋", "ビル（北棟）", "1"])
        self.assertEqual({(r[2], r[6]+r[7], r[8]) for r in source}, {(r[0],r[1],r[2]) for r in data})
        # 地域が違う同名町域を利用しない。
        data, _ = records([row("1000000", "甲町", city="甲区"), row("1000001", "甲町ビル（１階）", city="乙区")])
        self.assertEqual(data[1][6], "")

    def test_koaza_flag_and_names_keep_source_and_order(self):
        source = [row("9692701", "桧原（秋元、裏磐梯、簗部沢山、秋元）", koaza=True), row("9660401", "桧原（雄子沢）", koaza=True)]
        data, stats = records(source)
        by_code = {r[0]: r for r in data}
        self.assertEqual(by_code["9692701"][2], source[0][8])
        self.assertEqual(by_code["9692701"][3:5], ["koaza", "桧原"])
        self.assertEqual(json.loads(by_code["9692701"][7]), ["秋元", "裏磐梯", "簗部沢山"])
        self.assertEqual(json.loads(by_code["9660401"][7]), ["雄子沢"])
        self.assertEqual(stats["classification_details"]["koaza_separated"], 2)
        self.assertEqual(stats["classification_details"]["koaza_names"], 4)
        first, a = generator.build_dictionary(archive(source))
        second, b = generator.build_dictionary(archive(source[::-1]))
        self.assertEqual(first, second)
        self.assertEqual(a, b)

    def test_koaza_requires_flag_and_entire_list_to_be_safe(self):
        towns = ["桧原（秋元、その他）", "桧原（秋元、裏磐梯を除く）", "桧原（秋元、）", "桧原（秋元（南）、裏磐梯）", "桧原（秋元、地区）", "種市第１地割〜第３地割（八木南町、八木北町）", "南郷通（南）", "共和町（キョウワマチ）", "大山（小牧大山）", "桧原（秋元、字１２）", "桧原（秋元から裏磐梯まで）", "桧原（秋元及び裏磐梯）"]
        for town in towns:
            with self.subTest(town=town):
                data, _ = records([row("1000001", town, koaza=True)])
                self.assertEqual(data[0][7], "")
                self.assertEqual(data[0][2], town)
        data, _ = records([row("1000001", "桧原（秋元、裏磐梯）")])
        self.assertEqual(data[0][7], "")
        data, _ = records([row("6028368", "北町（上の下立売通天神道西入上る、上の下立売通御前西入）", "京都府", "京都市上京区", koaza=True)])
        self.assertEqual(data[0][3], "kyoto")
        self.assertEqual(data[0][7], "")

    def test_koaza_numbers_and_bracket_variants_are_ignored(self):
        for bracket in ["[施設、説明]", "［施設、説明］", "〔施設、説明〕", "[施設[説明]]"]:
            town = "毛萱（前川原２３２〜２４４、３１１、３１２、３３７〜８６２番地" + bracket + "）"
            data, _ = records([row("9790622", town, koaza=True)])
            self.assertEqual(json.loads(data[0][7]), ["前川原"])
            self.assertEqual(data[0][2], town)
        data, _ = records([row("1000001", "甲（乙１番地、乙２番地、丙５〜１０番地）", koaza=True)])
        self.assertEqual(json.loads(data[0][7]), ["乙", "丙"])
        for town in ["甲（１２３、４５６番地）", "甲（乙１番地不明）", "甲（乙[説明）", "甲（乙〔説明）", "甲（乙１番地丙）"]:
            data, _ = records([row("1000001", town, koaza=True)])
            self.assertEqual(data[0][7], "")

    def test_koaza_quotes_use_spaces_and_keep_numeric_conditions_in_source(self):
        data, _ = records([row("3703311", "岡崎（烏帽子「榛名湖畔」）")])
        self.assertEqual(json.loads(data[0][7]), ["烏帽子 榛名湖畔"])
        data, _ = records([row("1000001", "甲（乙「１番地、２番地を除く」、丙「湖畔」）", koaza=True)])
        self.assertEqual(json.loads(data[0][7]), ["乙", "丙 湖畔"])
        self.assertTrue("２番地を除く" in data[0][2])
        for town in ["甲（乙「湖畔）", "甲（乙「湖畔「説明」」）", "甲（乙「１番地不明」）", "甲（１２３番地「峠」）"]:
            data, _ = records([row("1000001", town, koaza=True)])
            self.assertEqual(data[0][7], "")

    def test_forest_block_number_and_name_are_preserved(self):
        source = [row("3703321", "泉沢（烏帽子「榛名湖畔」、烏帽子国有林７７林班）"), row("9492141", "関山（熊堂山、妙高山国有林第２８班）")]
        data, _ = records(source)
        self.assertEqual(json.loads(data[0][7]), ["烏帽子 榛名湖畔", "烏帽子国有林７７林班"])
        self.assertEqual(json.loads(data[1][7]), ["熊堂山", "妙高山国有林第２８班"])
        self.assertEqual(data[0][2], source[0][8])
        for town in ["泉沢（烏帽子「榛名湖畔」、国有林の未確認な説明）", "関山（熊堂山、妙高山国有林第２８班の北側）"]:
            data, _ = records([row("1000001", town)])
            self.assertEqual(data[0][7], "")

    def test_airport_interior_and_pasture_have_distinct_spacing(self):
        source = [row("2820011", "三里塚（御料牧場・成田国際空港内）"), row("2820010", "吉岡（成田国際空港内）")]
        data, stats = records(source)
        self.assertEqual(data[1][3:5], ["airport", "三里塚"])
        self.assertEqual(json.loads(data[1][7]), ["御料牧場", " 成田国際空港内"])
        self.assertEqual(json.loads(data[0][7]), [" 成田国際空港内"])
        self.assertEqual(data[1][2], source[0][8])
        self.assertEqual(stats["classification_details"]["airport_separated"], 2)
        self.assertEqual(stats["classification_details"]["airport_variants"], 3)
        first, a = generator.build_dictionary(archive(source))
        second, b = generator.build_dictionary(archive(source[::-1]))
        self.assertEqual(first, second)
        self.assertEqual(a, b)

    def test_airport_unknown_mixed_annotations_are_not_partially_expanded(self):
        for town in ["三里塚（御料牧場・成田国際空港内・その他）", "三里塚（御料牧場・成田国際空港内・未確認の説明）", "三里塚（空港内）"]:
            data, _ = records([row("1000001", town)])
            self.assertEqual(data[0][7], "")
            self.assertEqual(data[0][2], town)

    def test_explicit_facilities_are_separated_with_a_space(self):
        for town, expected in [("内田町（石見空港）", [" 石見空港"]), ("岩神町（足助高等学校）", [" 足助高等学校"]), ("関和久（八雲神社）", [" 八雲神社"]), ("黒瀬町南方（賀茂精神医療センター）", [" 賀茂精神医療センター"]), ("下増田（南原無番地、仙台空港関係施設）", ["南原", " 仙台空港関係施設"]), ("杉名畑４４地割（湯田ダム管理事務所、後口山、当楽）", [" 湯田ダム管理事務所", "後口山", "当楽"]), ("甲（第２研究所７番地）", [" 第２研究所"]), ("芦田町福田（３７６−１０「聖宝寺」）", [" 聖宝寺"])]:
            with self.subTest(town=town):
                data, _ = records([row("1000001", town)])
                self.assertEqual(data[0][3], "facility")
                self.assertEqual(json.loads(data[0][7]), expected)
                self.assertEqual(data[0][2], town)

    def test_facility_like_town_names_conditions_and_ignored_notes_are_retained(self):
        for town in ["甲（寺前）", "甲（観音寺）", "甲（長楽寺）", "甲（大学村）", "甲（内園）", "甲（施設１番地・未確認の説明）", "甲（病院を除く）", "甲（石見空港、未確認の説明）", "甲（乙〔東京電力福島第二原子力発電所構内〕）", "第１地割〜第３地割（石見空港）"]:
            with self.subTest(town=town):
                data, _ = records([row("1000001", town)])
                self.assertEqual(data[0][7], "")
                self.assertEqual(data[0][2], town)
        data, _ = records([row("1000001", "甲（寺前、観音寺）", koaza=True)])
        self.assertEqual(json.loads(data[0][7]), ["寺前", "観音寺"])

    def test_ambiguous_facility_names_are_scoped_to_verified_region(self):
        data, _ = records([row("1000001", "甲（たかね荘）", "山梨県", "北杜市")])
        self.assertEqual(json.loads(data[0][7]), [" たかね荘"])
        data, _ = records([row("1000001", "甲（たかね荘）")])
        self.assertEqual(data[0][7], "")

    def test_invalid_records_and_empty_data_are_rejected(self):
        for rows in ([], [row("100000", "千代田")], [row("１０００００１", "千代田")], [["1000001"]], [row("1000001", "町\n域")]):
            with self.subTest(rows=rows), self.assertRaises(ValueError): generator.build_dictionary(archive(rows))


if __name__ == "__main__": unittest.main()
