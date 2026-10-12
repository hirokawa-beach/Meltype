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
