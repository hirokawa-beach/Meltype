// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hirokawa-beach
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

internal static class PostalBuildingTests
{
    [Test]
    public static void Building_KnownAndUnknownFloorsAndOrdinaryTown()
    {
        Assert.Equal("東京都千代田区霞が関 霞が関ビル", PostalCodeDictionary.Lookup("1006090").Single().Text);
        Assert.Equal("東京都千代田区霞が関 霞が関ビル３６階", PostalCodeDictionary.Lookup("1006036").Single().Text);
        Assert.Equal("愛知県名古屋市中村区名駅 ミッドランドスクエア（高層棟）１階", PostalCodeDictionary.Lookup("4506201").Single().Text);
        Assert.Equal("東京都千代田区霞が関", PostalCodeDictionary.Lookup("1000013").Single().Text);
        Assert.True(PostalCodeDictionary.Lookup("1006090").Single().Detail!.Contains("地階・階層不明"), "不明は地下階も含む原文を保持");
        Assert.True(PostalCodeDictionary.Lookup("1000013").Single().Detail!.Contains("次のビルを除く"), "除外条件を保持");
        Assert.Equal(PostalCodeDictionary.Lookup("1006036").Single().Text, PostalCodeDictionary.Lookup("100-6036").Single().Text);
    }

    [Test]
    public static void Building_FloorWidthAndEnglishNamesAreIndependent()
    {
        var half = new PostalCodeDictionary.CandidateTypes(true, false, false, true);
        Assert.Equal("東京都千代田区霞が関 霞が関ビル36階", PostalCodeDictionary.Lookup("1006036", half).Single().Text);
        Assert.Equal("東京都千代田区大手町 JAビル", PostalCodeDictionary.Lookup("1006890").Single().Text);
        Assert.Equal("東京都品川区大崎 ThinkParkTower１階", PostalCodeDictionary.Lookup("1416001").Single().Text);
        Assert.Equal("東京都品川区大崎 ThinkParkTower1階", PostalCodeDictionary.Lookup("1416001", half).Single().Text);
        var records = new[] { new PostalCodeDictionary.Record("東京都甲市", "甲町ＳＳ３０ビル（１２階）", "building", "", [], new("甲町", "SS30ビル", "12")) };
        Assert.Equal("東京都甲市甲町 SS30ビル１２階", PostalCodeDictionary.Generate(records, PostalCodeDictionary.CandidateTypes.Default).Single().Text);
        Assert.Equal("東京都甲市甲町 SS30ビル12階", PostalCodeDictionary.Generate(records, half).Single().Text);
        Assert.Equal(0, PostalCodeDictionary.Lookup("1006036", new(false, true, true, true)).Count);
    }

    [Test]
    public static void Building_WidthChangeAffectsConversionWithoutLearning()
    {
        var flags = PostalCodeDictionary.CandidateTypes.Default;
        var history = new ConversionHistory(null);
        var phrases = new PhraseHistory(null);
        var k = new CompositionTests.Keyboard(history: history, predictor: new Predictor(phrases, new UserDictionary(null), history), postalCandidateTypes: () => flags);
        k.Type("1006036 ");
        Assert.Equal("1006036", k.Host.View!.Candidates[0]);
        Assert.Equal("東京都千代田区霞が関 霞が関ビル３６階", k.Host.View.Candidates[1]);
        k.Press(VirtualKeys.Escape);
        flags = flags with { HalfWidthFloorNumbers = true };
        k.Press(VirtualKeys.Space);
        k.Controller.SelectCandidate(1);
        k.Press(VirtualKeys.Return);
        Assert.Equal("東京都千代田区霞が関 霞が関ビル36階", k.Host.Document);
        Assert.True(history.Get("1006036") is null, "住所を学習しない");
        Assert.Equal(0, phrases.Count);
    }

    [Test]
    public static void Building_WidthSettingUsesExistingSettingsUi()
    {
        var property = System.ComponentModel.TypeDescriptor.GetProperties(typeof(Settings))[nameof(Settings.PostalFloorNumbers)]!;
        Assert.Equal("1. 全般", property.Category);
        Assert.Equal(typeof(PostalFloorNumberWidth), property.PropertyType);
        Assert.Equal("郵便番号: 階数の数字", property.DisplayName);
    }
}
