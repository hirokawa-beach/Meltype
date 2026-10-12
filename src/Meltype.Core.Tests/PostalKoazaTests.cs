// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hirokawa-beach
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

internal static class PostalKoazaTests
{
    private const string Oaza = "福島県耶麻郡北塩原村桧原";

    [Test]
    public static void Koaza_DefaultShowsOnlySmallAndKeepsOazaInSource()
    {
        var candidates=PostalCodeDictionary.Lookup("9692701");
        var candidate=candidates[0];
        Assert.Equal(17,candidates.Count);
        Assert.Equal(Oaza+"秋元",candidate.Text);
        Assert.Equal("小字込み",candidate.Note);
        Assert.True(!candidates.Any(c=>c.Text==Oaza),"小字ONでは大字のみの候補を出さない");
        Assert.True(candidate.Detail!.Contains("秋元") && candidate.Detail.Contains("簗部沢山"),"全小字を原文に保持");
        Assert.Equal(Oaza+"秋元",PostalCodeDictionary.Lookup("969-2701")[0].Text);
        Assert.Equal(17,PostalCodeDictionary.Records("9692701").Single().Koaza!.Count);
    }

    [Test]
    public static void Koaza_AllFlagCombinationsAndLastName()
    {
        foreach(var basic in new[]{false,true})
        foreach(var small in new[]{false,true}) {
            var candidates=PostalCodeDictionary.Lookup("9692701",new(basic,true,true,false,small));
            Assert.Equal(small?17:basic?1:0,candidates.Count);
            Assert.Equal(basic && !small,candidates.Any(c=>c.Text==Oaza));
            Assert.Equal(small,candidates.Any(c=>c.Text==Oaza+"秋元"));
            Assert.Equal(small,candidates.Any(c=>c.Text==Oaza+"簗部沢山"));
            if(small) {
                var lastSmall=candidates[16];
                Assert.Equal(Oaza+"簗部沢山",lastSmall.Text);
                Assert.Equal("小字込み",lastSmall.Note);
                Assert.True(lastSmall.Detail!.Contains("選択した小字: 簗部沢山"),"選んだ小字を確認できる");
            }
        }
    }

    [Test]
    public static void Koaza_SameOazaDifferentCodesDoNotShareChildren()
    {
        var options=new PostalCodeDictionary.CandidateTypes(true,false,false,false,true);
        var first=PostalCodeDictionary.Lookup("9692701",options);
        var other=PostalCodeDictionary.Lookup("9660401",options);
        Assert.True(!first.Any(c=>c.Text==Oaza) && !other.Any(c=>c.Text==Oaza),"小字ONでは両番号とも大字だけを出さない");
        Assert.True(other.Any(c=>c.Text==Oaza+"雄子沢"),"別番号の小字");
        Assert.True(!other.Any(c=>c.Text==Oaza+"秋元"),"同じ大字でも別番号の小字は混ぜない");
        Assert.True(!first.Any(c=>c.Text==Oaza+"雄子沢"),"逆向きも混ぜない");
        Assert.Equal("岩手県一関市山目大槻",PostalCodeDictionary.Lookup("0210063")[0].Text);
        Assert.True(PostalCodeDictionary.Lookup("0210063",options).Any(c=>c.Text=="岩手県一関市山目大槻"),"単一小字も展開");
    }

    [Test]
    public static void Koaza_ConditionsAndDirectionAreNotFlattened()
    {
        var options=new PostalCodeDictionary.CandidateTypes(true,true,true,true,true);
        foreach(var code in new[]{"0287915","6308037","4740057","4850026","0030022"}) {
            var records=PostalCodeDictionary.Records(code);
            Assert.True(records.All(r=>r.Koaza is null),"除外・範囲・読み・別称・方角は小字にしない: "+code);
            Assert.True(PostalCodeDictionary.Lookup(code,options).Any(c=>c.Text==records[0].Region+records[0].Town),"原文保持: "+code);
        }
        Assert.Equal("東京都千代田区霞が関 霞が関ビル36階",PostalCodeDictionary.Lookup("1006036",options).Single().Text);
    }

    [Test]
    public static void Koaza_LastCandidateCommitsWithoutLearningAndChangesLive()
    {
        var flags=new PostalCodeDictionary.CandidateTypes(true,false,false,false,false);
        var history=new ConversionHistory(null);
        var phrases=new PhraseHistory(null);
        var languages=new LanguageMemory(null);
        var k=new CompositionTests.Keyboard(history:history,languages:languages,predictor:new Predictor(phrases,new UserDictionary(null),history),postalCandidateTypes:()=>flags);
        k.Type("9692701 ");
        Assert.Equal(3,k.Host.View!.Candidates.Count);
        Assert.Equal(Oaza,k.Host.View.Candidates[1]);
        k.Press(VirtualKeys.Escape);
        flags=flags with { Koaza=true };
        k.Press(VirtualKeys.Space);
        Assert.Equal(19,k.Host.View!.Candidates.Count,"番号、17小字、全角番号");
        k.Controller.SelectCandidate(17);
        k.Press(VirtualKeys.Return);
        Assert.Equal(Oaza+"簗部沢山",k.Host.Document);
        Assert.True(history.Get("9692701") is null,"学習しない");
        Assert.Equal(0,phrases.Count);
        Assert.Equal(0,languages.Count);
        Assert.Equal(0,languages.Version);
    }

    [Test]
    public static void Koaza_NumbersBracketsAndQuotedNamesAreFormatted()
    {
        var numbered=PostalCodeDictionary.Lookup("9790622");
        Assert.Equal("福島県双葉郡富岡町毛萱前川原",numbered[0].Text);
        Assert.Equal(1,numbered.Count);
        Assert.True(numbered[0].Detail!.Contains("２３２〜２４４") && numbered[0].Detail!.Contains("東京電力福島第二原子力発電所構内"),"省いた番地・角括弧を原文保持");
        var quoted=PostalCodeDictionary.Lookup("3703311");
        Assert.Equal("群馬県吾妻郡東吾妻町岡崎烏帽子 榛名湖畔",quoted[0].Text);
        Assert.True(quoted[0].Detail!.Contains("烏帽子「榛名湖畔」"),"引用の原文保持");
        Assert.Equal("山形県鶴岡市添川渡戸沢 筍沢温泉",PostalCodeDictionary.Lookup("9996652")[0].Text);
        Assert.Equal("群馬県吾妻郡東吾妻町岡崎",PostalCodeDictionary.Lookup("3703311",new(true,false,false)).Single().Text);
        var excluded=PostalCodeDictionary.Lookup("0330071");
        var shimokubo=excluded.Single(c=>c.Text=="青森県上北郡六戸町犬落瀬下久保");
        Assert.True(shimokubo.Detail!.Contains("１７４を除く"),"数字付き引用の除外は詳細に保持");
        Assert.True(excluded.All(c=>!c.Text.Contains("を除く") && !c.Text.Contains("１７４")),"条件を挿入しない");
    }

    [Test]
    public static void Koaza_QuotedNameAndForestBlockAreBothSelectableWithoutOaza()
    {
        foreach(var code in new[]{"3703311","3703321","3703322"}) {
            var town=code=="3703311"?"岡崎":code=="3703321"?"泉沢":"川戸";
            var region="群馬県吾妻郡東吾妻町"+town;
            var on=PostalCodeDictionary.Lookup(code);
            Assert.Equal(code=="3703321"?2:1,on.Count);
            Assert.Equal(region+"烏帽子 榛名湖畔",on[0].Text);
            Assert.True(!on.Any(c=>c.Text==region),"大字のみを出さない");
            Assert.Equal(region,PostalCodeDictionary.Lookup(code,new(true,false,false)).Single().Text);
            if(code=="3703321") {
                Assert.Equal(region+"烏帽子国有林７７林班",on[1].Text);
                Assert.True(on[1].Detail!.Contains("７７林班"),"林班番号は候補と原文詳細に保持");
                Assert.Equal("新潟県妙高市関山妙高山国有林第２８班",PostalCodeDictionary.Lookup("9492141")[1].Text);
            }
        }
        var k=new CompositionTests.Keyboard(postalCandidateTypes:()=>PostalCodeDictionary.CandidateTypes.Default);
        k.Type("3703322 ");
        Assert.Equal(3,k.Host.View!.Candidates.Count,"元番号、小字、全角番号だけ");
        Assert.Equal("群馬県吾妻郡東吾妻町川戸烏帽子 榛名湖畔",k.Host.View.Candidates[1]);
        Assert.Equal("３７０３３２２",k.Host.View.Candidates[2]);
    }

    [Test]
    public static void Koaza_UnparsedAndOtherAddressRecordsRemainAvailable()
    {
        var rows=new[] {
            new PostalCodeDictionary.Record("東京都甲市","甲（乙）","koaza","甲",[],Koaza:["乙"]),
            new PostalCodeDictionary.Record("東京都甲市","丙","normal","",[]),
            new PostalCodeDictionary.Record("東京都甲市","丁（未解釈の説明）","other","",[]),
        };
        var candidates=PostalCodeDictionary.Generate(rows,PostalCodeDictionary.CandidateTypes.Default);
        Assert.True(candidates.Any(c=>c.Text=="東京都甲市甲乙"),"小字");
        Assert.True(!candidates.Any(c=>c.Text=="東京都甲市甲"),"この行の大字を出さない");
        Assert.True(candidates.Any(c=>c.Text=="東京都甲市丙"),"別の通常住所を維持");
        Assert.True(candidates.Any(c=>c.Text=="東京都甲市丁（未解釈の説明）"),"未解釈の住所を維持");
    }

    [Test]
    public static void Airport_MixedTownAndInteriorAreSeparateCandidates()
    {
        var candidates=PostalCodeDictionary.Lookup("2820011");
        Assert.Equal(2,candidates.Count);
        Assert.Equal("千葉県成田市三里塚御料牧場",candidates[0].Text);
        Assert.Equal("千葉県成田市三里塚 成田国際空港内",candidates[1].Text);
        Assert.Equal("空港内",candidates[1].Note);
        Assert.True(candidates.All(c=>c.Detail!.Contains("三里塚（御料牧場・成田国際空港内）")),"原文を両候補に保持");
        Assert.Equal("千葉県成田市三里塚",PostalCodeDictionary.Lookup("2820011",new(true,false,false)).Single().Text);
        Assert.Equal(candidates[1].Text,PostalCodeDictionary.Lookup("282-0011")[1].Text);
        Assert.Equal(0,PostalCodeDictionary.Lookup("2820011",new(false,true,true)).Count);
        Assert.Equal(2,PostalCodeDictionary.Lookup("2820011",new(false,false,false,false,true)).Count);
    }

    [Test]
    public static void Airport_SingleInteriorAndOtherMunicipalitiesUseSameSpacing()
    {
        Assert.Equal("千葉県成田市吉岡 成田国際空港内",PostalCodeDictionary.Lookup("2820010").Single().Text);
        Assert.Equal("千葉県香取郡多古町一鍬田 成田国際空港内",PostalCodeDictionary.Lookup("2820031").Single().Text);
        Assert.Equal("千葉県山武郡芝山町岩山 成田国際空港内",PostalCodeDictionary.Lookup("2820035").Single().Text);
        Assert.Equal("東京都大田区羽田空港",PostalCodeDictionary.Lookup("1440041").Single().Text);
        Assert.Equal("空港内",PostalCodeDictionary.Lookup("2820035").Single().Note);
    }

    [Test]
    public static void Airport_LastCandidateCommitsWithoutBareTownOrLearning()
    {
        var history=new ConversionHistory(null);
        var phrases=new PhraseHistory(null);
        var k=new CompositionTests.Keyboard(history:history,predictor:new Predictor(phrases,new UserDictionary(null),history),postalCandidateTypes:()=>PostalCodeDictionary.CandidateTypes.Default);
        k.Type("2820011 ");
        Assert.Equal(4,k.Host.View!.Candidates.Count,"番号、牧場、空港内、全角番号");
        Assert.True(!k.Host.View.Candidates.Contains("千葉県成田市三里塚"),"町域だけを追加しない");
        k.Controller.SelectCandidate(2);
        k.Press(VirtualKeys.Return);
        Assert.Equal("千葉県成田市三里塚 成田国際空港内",k.Host.Document);
        Assert.True(history.Get("2820011") is null,"学習しない");
        Assert.Equal(0,phrases.Count);
    }

    [Test]
    public static void Facility_NamesUseSpacesAndRawDetailsRemain()
    {
        foreach(var item in new[]{
            ("6980051","島根県益田市内田町 石見空港"),
            ("4442451","愛知県豊田市岩神町 足助高等学校"),
            ("7392621","広島県東広島市黒瀬町南方 賀茂精神医療センター"),
            ("9690106","福島県西白河郡泉崎村関和久 八雲神社"),
            ("7200845","広島県福山市芦田町福田 聖宝寺"),
        }) {
            var candidate=PostalCodeDictionary.Lookup(item.Item1).Single();
            Assert.Equal(item.Item2,candidate.Text);
            Assert.Equal("施設",candidate.Note);
            Assert.Equal("町域原文: "+PostalCodeDictionary.Records(item.Item1).Single().Town,candidate.Detail!.Split('\n')[1]);
        }
        Assert.Equal("島根県益田市内田町",PostalCodeDictionary.Lookup("6980051",new(true,false,false)).Single().Text);
        Assert.True(PostalCodeDictionary.Lookup("7200845").Single().Detail!.Contains("３７６−１０"),"省いた番地を保持");
    }

    [Test]
    public static void Facility_MixedLocalitiesFacilitiesAndHeadNumbersRemainDistinct()
    {
        var mixed=PostalCodeDictionary.Lookup("9892401");
        Assert.Equal(2,mixed.Count);
        Assert.Equal("宮城県名取市下増田南原",mixed[0].Text);
        Assert.Equal("宮城県名取市下増田 仙台空港関係施設",mixed[1].Text);
        var numbered=PostalCodeDictionary.Lookup("4070311");
        Assert.Equal(4,numbered.Count);
        Assert.Equal("山梨県北杜市大泉町西井出８２４０−１美森",numbered[0].Text);
        Assert.Equal("山梨県北杜市大泉町西井出８２４０−１ たかね荘",numbered[1].Text);
        Assert.Equal("山梨県北杜市大泉町西井出８２４０−１ 清泉寮",numbered[2].Text);
        Assert.Equal("山梨県北杜市大泉町西井出８２４０−１ サンメドウズスキー場",numbered[3].Text);
        Assert.True(PostalCodeDictionary.Lookup("0240341").Any(c=>c.Text=="岩手県和賀郡西和賀町杉名畑４４地割 湯田ダム管理事務所"),"町域の地割番号を保持");
        Assert.True(PostalCodeDictionary.Lookup("6511102").Any(c=>c.Text=="兵庫県神戸市北区山田町下谷上 再度公園"),"公園名の前に空白");
    }

    [Test]
    public static void Facility_DoesNotReclassifyTempleTownNamesOrEarlierExamples()
    {
        Assert.Equal("岩手県一関市山目寺前",PostalCodeDictionary.Lookup("0210062").Single().Text);
        Assert.True(PostalCodeDictionary.Lookup("4838258").Any(c=>c.Text=="愛知県江南市上奈良町観音寺"),"小字名として扱う");
        Assert.Equal("富山県南砺市北野（長楽寺）",PostalCodeDictionary.Lookup("9391801").Single().Text);
        Assert.Equal("長野県上水内郡信濃町野尻（大学村）",PostalCodeDictionary.Lookup("3891306").Single().Text);
        Assert.Equal("千葉県成田市三里塚御料牧場",PostalCodeDictionary.Lookup("2820011")[0].Text);
        Assert.Equal("千葉県成田市三里塚 成田国際空港内",PostalCodeDictionary.Lookup("2820011")[1].Text);
        Assert.Equal("群馬県吾妻郡東吾妻町泉沢烏帽子国有林７７林班",PostalCodeDictionary.Lookup("3703321")[1].Text);
        Assert.Equal("福島県双葉郡富岡町毛萱前川原",PostalCodeDictionary.Lookup("9790622").Single().Text);
    }

    [Test]
    public static void Facility_ChoiceDoesNotLearnOrAddBareTown()
    {
        var history=new ConversionHistory(null);
        var phrases=new PhraseHistory(null);
        var k=new CompositionTests.Keyboard(history:history,predictor:new Predictor(phrases,new UserDictionary(null),history),postalCandidateTypes:()=>PostalCodeDictionary.CandidateTypes.Default);
        k.Type("6980051 ");
        Assert.Equal(3,k.Host.View!.Candidates.Count);
        Assert.True(!k.Host.View.Candidates.Contains("島根県益田市内田町"),"町域だけの候補を混ぜない");
        k.Controller.SelectCandidate(1);k.Press(VirtualKeys.Return);
        Assert.Equal("島根県益田市内田町 石見空港",k.Host.Document);
        Assert.True(history.Get("6980051") is null,"学習しない");
        Assert.Equal(0,phrases.Count);
    }

    [Test]
    public static void Koaza_SettingsDefaultPersistenceProfilesAndUiMetadata()
    {
        var settings=new Settings();
        Assert.True(settings.PostalKoazaCandidates,"初期ON");
        var path=Path.Combine(Path.GetTempPath(),"meltype-koaza-"+Guid.NewGuid().ToString("N")+".json");
        try {
            settings.PostalKoazaCandidates=false;settings.Save(path);
            Assert.True(!Settings.Load(path).PostalKoazaCandidates,"保存読込");
            var profile=new Settings().Normalize().AddProfile("小字")!;profile.PostalKoazaCandidates=false;
            var standard=profile.SwitchProfile("標準");
            Assert.True(standard.PostalKoazaCandidates && !standard.SwitchProfile("小字").PostalKoazaCandidates,"プロファイル");
        } finally { File.Delete(path); }
        var property=System.ComponentModel.TypeDescriptor.GetProperties(typeof(Settings))[nameof(Settings.PostalKoazaCandidates)]!;
        Assert.Equal("1. 全般",property.Category);
        Assert.Equal("郵便番号: 小字込みの候補",property.DisplayName);
        Assert.Equal(typeof(bool),property.PropertyType);
    }
}
