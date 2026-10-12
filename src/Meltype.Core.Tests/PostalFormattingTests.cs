// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hirokawa-beach

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

internal static class PostalFormattingTests
{
    private const string KyotoBase = "京都府京都市上京区北町";
    private const string KyotoStreet = "京都府京都市上京区上の下立売通天神道西入上る";

    [Test]
    public static void Kyoto_DefaultBasicAddressKeepsWholeSourceInDetail()
    {
        var rows = PostalCodeDictionary.Records("6028368");
        Assert.Equal(1, rows.Count);
        Assert.Equal(21, rows[0].Streets.Count);
        var candidate = PostalCodeDictionary.Lookup("6028368").Single();
        Assert.Equal(KyotoBase, candidate.Text);
        Assert.Equal("基本住所", candidate.Note);
        Assert.Equal("町域原文: " + rows[0].Town, candidate.Detail);
        Assert.True(candidate.Detail!.Contains("御前通西裏下立売上る"), "末尾の説明も保存");
    }

    [Test]
    public static void Kyoto_AllEightSettingCombinationsAreIndependent()
    {
        foreach (var basic in new[] { false, true })
        foreach (var joined in new[] { false, true })
        foreach (var street in new[] { false, true })
        {
            var candidates = PostalCodeDictionary.Lookup("6028368", new(basic, joined, street));
            Assert.Equal((basic ? 1 : 0) + (joined ? 21 : 0) + (street ? 21 : 0), candidates.Count);
            Assert.Equal(basic, candidates.Any(c => c.Text == KyotoBase));
            Assert.Equal(joined, candidates.Any(c => c.Text == KyotoStreet + "北町"));
            Assert.Equal(street, candidates.Any(c => c.Text == KyotoStreet));
            Assert.Equal(candidates.Count, candidates.Select(c => c.Text).Distinct().Count());
        }
    }

    [Test]
    public static void Kyoto_UncertainTownJoinIsOmittedButStreetRemainsSelectable()
    {
        var candidates = PostalCodeDictionary.Lookup("6050011", new(true, true, true));
        Assert.True(candidates.Any(c => c.Text == "京都府京都市東山区三条大橋東４丁目"), "通り名のみは保持");
        Assert.True(!candidates.Any(c => c.Text == "京都府京都市東山区三条大橋東４丁目七軒町"), "町名を無理に結合しない");
        Assert.True(candidates.Any(c => c.Text == "京都府京都市東山区三条大橋東入４丁目七軒町"), "確認できる形は結合");
    }

    [Test]
    public static void Range_SameTownDifferentCodesKeepDifferentConditions()
    {
        var first = PostalCodeDictionary.Lookup("0600001").Single();
        var second = PostalCodeDictionary.Lookup("0640821").Single();
        Assert.Equal("北海道札幌市中央区北一条西", first.Text);
        Assert.Equal(first.Text, second.Text);
        Assert.True(first.Detail!.Contains("１〜１９丁目"), "最初の番号の範囲");
        Assert.True(second.Detail!.Contains("２０〜２８丁目"), "別番号の範囲");
        Assert.Equal(1, PostalCodeDictionary.Lookup("0600001", new(true, true, true)).Count, "範囲を通り名として展開しない");
    }

    [Test]
    public static void FloorAndDirectionNames_AreNotDiscarded()
    {
        var floor = PostalCodeDictionary.Lookup("1006001").Single();
        Assert.Equal("東京都千代田区霞が関 霞が関ビル１階", floor.Text);
        Assert.Equal("建物・階", floor.Note);
        Assert.Equal("北海道札幌市白石区南郷通（南）", PostalCodeDictionary.Lookup("0030022").Single().Text);
    }

    [Test]
    public static void SameInsertedAddress_MergesConditionsWithoutDroppingSourceRecords()
    {
        var rows = new PostalCodeDictionary.Record[] {
            new("東京都甲市", "北町（１丁目）", "range", "北町", []),
            new("東京都甲市", "北町（２丁目）", "range", "北町", []),
        };
        var merged = PostalCodeDictionary.Generate(rows, PostalCodeDictionary.CandidateTypes.Default).Single();
        Assert.Equal("東京都甲市北町", merged.Text);
        Assert.True(merged.Detail!.Contains(rows[0].Town) && merged.Detail.Contains(rows[1].Town), "両方の条件が残る");
        Assert.Equal(2, rows.Length, "元レコードは変更しない");
    }

    [Test]
    public static void UnsupportedComplexCondition_PreservesRawText()
    {
        var town = "甲（乙（１番地）を除く）";
        var rows = new[] { new PostalCodeDictionary.Record("東京都甲市", town, "restricted", "", []) };
        var candidate = PostalCodeDictionary.Generate(rows, new(true, true, true)).Single();
        Assert.Equal("東京都甲市" + town, candidate.Text);
        Assert.Equal("町域原文: " + town, candidate.Detail);
    }

    [Test]
    public static void Exclusion_IsVisibleEvenWhenDictionaryMeaningsAreOff()
    {
        var settings = new Settings { ShowCandidateMeanings = false };
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions {
            CandidateMeanings = () => settings.ShowCandidateMeanings,
        }, () => settings);
        foreach (var c in "0600001") session.HandleKey(c, c, false, false, false, false);
        var view = session.HandleKey(VirtualKeys.Space, null, false, false, false, false).View!;
        session.SelectCandidate(1);
        var selected = session.HandleKey(VirtualKeys.Left, null, false, false, false, false).View!;
        Assert.Equal("範囲条件あり", view.Notes![1]);
        Assert.True(selected.Meaning!.Contains("１〜１９丁目"), "辞書の意味表示 OFF でも郵便番号の条件を確認可能");
    }

    [Test]
    public static void AllTypesOff_StillKeepsNumberAndDoesNotAskExternalEngine()
    {
        var k = new CompositionTests.Keyboard(postalCandidateTypes: () => new(false, false, false),
            moreCandidates: _ => throw new InvalidOperationException("郵便番号の候補を外部エンジンに問い合わせた"));
        k.Type("6028368 ");
        Assert.True(k.Host.View is { Converting: true }, "番号の候補は維持");
        Assert.Equal("6028368", k.Showing);
        Assert.Equal(2, k.Host.View!.Candidates.Count, "番号と全角番号だけ");
        k.Press(VirtualKeys.Return);
        Assert.Equal("6028368", k.Host.Document);
    }

    [Test]
    public static void SettingsChanged_AffectNextConversionAndStreetChoiceIsNotLearned()
    {
        var flags = PostalCodeDictionary.CandidateTypes.Default;
        var history = new ConversionHistory(null);
        var phrases = new PhraseHistory(null);
        var k = new CompositionTests.Keyboard(history: history, predictor: new Predictor(phrases, new UserDictionary(null), history), postalCandidateTypes: () => flags);
        k.Type("6028368 ");
        Assert.Equal(3, k.Host.View!.Candidates.Count, "初期値は番号・基本住所・全角番号");
        k.Press(VirtualKeys.Escape);
        flags = new(true, true, true);
        k.Press(VirtualKeys.Space);
        Assert.Equal(45, k.Host.View!.Candidates.Count, "設定変更で全種類を生成");
        k.Controller.SelectCandidate(k.Host.View.Candidates.ToList().IndexOf(KyotoStreet + "北町"));
        k.Press(VirtualKeys.Return);
        Assert.Equal(KyotoStreet + "北町", k.Host.Document);
        Assert.True(history.Get("6028368") is null, "通り名の選択も学習しない");
        Assert.Equal(0, phrases.Count);
    }

    [Test]
    public static void Settings_DefaultsPersistenceAndProfiles()
    {
        var settings = new Settings();
        Assert.Equal(PostalFloorNumberWidth.FullWidth, settings.PostalFloorNumbers);
        Assert.True(settings.PostalBasicAddressCandidates && !settings.PostalStreetAndTownCandidates && !settings.PostalStreetCandidates, "初期設定");
        var path = Path.Combine(Path.GetTempPath(), "meltype-postal-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            settings.PostalFloorNumbers = PostalFloorNumberWidth.HalfWidth;
            settings.PostalBasicAddressCandidates = false;
            settings.PostalStreetAndTownCandidates = true;
            settings.PostalStreetCandidates = true;
            settings.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal(PostalFloorNumberWidth.HalfWidth, loaded.PostalFloorNumbers);
            Assert.True(!loaded.PostalBasicAddressCandidates && loaded.PostalStreetAndTownCandidates && loaded.PostalStreetCandidates, "JSON の保存・読み込み (NativeAOT 用 metadata)");
            var profile = new Settings().Normalize().AddProfile("通り名")!;
            profile.PostalFloorNumbers = PostalFloorNumberWidth.HalfWidth;
            profile.PostalStreetCandidates = true;
            var standard = profile.SwitchProfile("標準");
            Assert.Equal(PostalFloorNumberWidth.FullWidth, standard.PostalFloorNumbers);
            Assert.Equal(PostalFloorNumberWidth.HalfWidth, standard.SwitchProfile("通り名").PostalFloorNumbers);
            Assert.True(!standard.PostalStreetCandidates, "標準プロファイル");
            Assert.True(standard.SwitchProfile("通り名").PostalStreetCandidates, "通り名プロファイル");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public static void Settings_AppearWithExistingCategoryAndDisplayNames()
    {
        foreach (var name in new[] { nameof(Settings.PostalBasicAddressCandidates), nameof(Settings.PostalStreetAndTownCandidates), nameof(Settings.PostalStreetCandidates) })
        {
            var property = System.ComponentModel.TypeDescriptor.GetProperties(typeof(Settings))[name]!;
            Assert.Equal("1. 全般", property.Category);
            Assert.True(property.DisplayName.StartsWith("郵便番号:"), "既存の設定画面が取り込める属性");
            Assert.Equal(typeof(bool), property.PropertyType);
        }
    }
}
