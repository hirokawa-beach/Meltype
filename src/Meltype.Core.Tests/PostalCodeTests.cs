// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hirokawa-beach

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

internal static class PostalCodeTests
{
    [Test]
    public static void NationwideData_ExactLookupAndLeadingZero()
    {
        Assert.Equal("東京都千代田区千代田", PostalCodeDictionary.Lookup("1000001").Single().Text);
        Assert.Equal(PostalCodeDictionary.Lookup("1000001").Single(), PostalCodeDictionary.Lookup("100-0001").Single());
        Assert.True(PostalCodeDictionary.Lookup("0600001").Any(a => a.Text.StartsWith("北海道札幌市中央区")), "先頭ゼロの番号を保持");
        Assert.Equal(0, PostalCodeDictionary.Lookup("0000000").Count, "未登録番号");
        Assert.Equal(0, PostalCodeDictionary.Lookup("1008798").Count, "事業所番号は一般住所辞書に含まない");
    }

    [Test]
    public static void InvalidInputs_DoNotBecomePostalCodes()
    {
        foreach (var input in new[] { "", "100000", "10000011", "1000001 ", " 1000001", "1000001x", "100/0001", "100--0001", "10-00001", "１０００００１", "1+000001", "https://1000001" })
            Assert.Equal(0, PostalCodeDictionary.Lookup(input).Count, input);
    }

    [Test]
    public static void FallbackCandidates_AreAllPreservedWithoutFakeTownNames()
    {
        var addresses = PostalCodeDictionary.Lookup("0040000");
        Assert.Equal(2, addresses.Count);
        Assert.True(addresses.Any(a => a.Text == "北海道札幌市厚別区" && a.Note == "町域未特定"), "厚別区");
        Assert.True(addresses.Any(a => a.Text == "北海道札幌市清田区" && a.Note == "町域未特定"), "清田区");
        Assert.Equal(addresses.Count, addresses.Select(a => a.Text).Distinct().Count(), "重複なし");
    }

    [Test]
    public static void Space_OffersAddressWithoutReplacingNumberAutomatically()
    {
        foreach (var input in new[] { "1000001", "100-0001" })
        foreach (var live in new[] { false, true })
        {
            var k = new CompositionTests.Keyboard(live: live, converter: new UnavailableConverter());
            k.Type(input);
            Assert.Equal(input, k.Showing, "入力中は番号を保持");
            k.Press(VirtualKeys.Space);
            Assert.True(k.Host.View is { Converting: true }, "変換を開く");
            Assert.Equal(input, k.Showing, "最初の候補は番号");
            Assert.Equal(1, k.Host.View!.Clauses!.Count, "番号を 1 文節に保持");
            Assert.True(k.Host.View.Candidates.Contains("東京都千代田区千代田"), "エンジンが使えなくても住所が出る");
            k.Controller.SelectCandidate(k.Host.View.Candidates.ToList().IndexOf("東京都千代田区千代田"));
            k.Press(VirtualKeys.Return);
            Assert.Equal("東京都千代田区千代田", k.Host.Document, "選んだ住所を入力");
        }
    }

    [Test]
    public static void EnterAndEscape_PreserveOriginalInput()
    {
        foreach (var input in new[] { "1000001", "100-0001" })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(input);
            k.Press(VirtualKeys.Return);
            Assert.Equal(input, k.Host.Document, "Enter は番号を確定");
            k = new CompositionTests.Keyboard();
            k.Type(input + " ");
            k.Press(VirtualKeys.Escape);
            Assert.Equal(input, k.Showing, "Esc は元入力に戻る");
            k.Press(VirtualKeys.Return);
            Assert.Equal(input, k.Host.Document);
        }
    }

    [Test]
    public static void EnglishAndDirectInput_KeepExistingSpaceBehavior()
    {
        foreach (var input in new[] { "1000001", "100-0001" })
        {
            var k = new CompositionTests.Keyboard();
            k.Host.PrecedingText = "I have ";
            k.Type(input + " ");
            Assert.Equal(input + " ", k.Host.Document, "英文では確定＋空白");
            k = new CompositionTests.Keyboard(direct: true);
            k.Type(input + " ");
            Assert.True(k.Host.View is not { Converting: true }, "直接入力では住所変換しない");
        }
    }

    [Test]
    public static void ExplicitJapaneseConversion_WorksInEnglishContext()
    {
        var k = new CompositionTests.Keyboard();
        k.Host.PrecedingText = "Address: ";
        k.Type("100-0001");
        k.Host.PhysicalShift = true;
        k.Press(VirtualKeys.Space);
        k.Host.PhysicalShift = false;
        Assert.True(k.Host.View!.Candidates.Contains("東京都千代田区千代田"), "Shift+Space で明示変換");
    }

    [Test]
    public static void TabAndClauseResize_KeepPostalCodeAtomic()
    {
        var k = new CompositionTests.Keyboard(tabConversion: true);
        k.Type("100-0001");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View is { Converting: true }, "Tab でも変換");
        k.TypeKeys((VirtualKeys.Left, true));
        Assert.Equal(1, k.Host.View!.Clauses!.Count, "Shift+左で番号を分割しない");
        Assert.True(k.Host.View.Candidates.Contains("東京都千代田区千代田"), "候補を保持");
    }

    [Test]
    public static void PostalChoice_IsNotLearnedAsAutomaticConversionOrPrediction()
    {
        var history = new ConversionHistory(null);
        var phrases = new PhraseHistory(null);
        var predictor = new Predictor(phrases, new UserDictionary(null), history);
        var k = new CompositionTests.Keyboard(history: history, predictor: predictor);
        k.Type("1000001 ");
        k.Controller.SelectCandidate(k.Host.View!.Candidates.ToList().IndexOf("東京都千代田区千代田"));
        k.Press(VirtualKeys.Return);
        Assert.True(history.Get("1000001") is null, "データ変更後に古い住所を優先しない");
        Assert.Equal(0, phrases.Count, "住所を予測用に保存しない");
        k.Type("1000001 ");
        Assert.Equal("1000001", k.Showing, "次も数字が最初の候補");
    }

    [Test]
    public static void ManyAddresses_AllCandidatesRemainSelectable()
    {
        var addresses = PostalCodeDictionary.Lookup("4520961");
        Assert.Equal(66, addresses.Count, "同梱版の最多候補");
        var k = new CompositionTests.Keyboard();
        k.Type("4520961 ");
        var last = addresses[^1].Text;
        Assert.True(k.Host.View!.Candidates.Contains(last), "9 件を超えても全件保持");
        k.Controller.SelectCandidate(k.Host.View.Candidates.ToList().IndexOf(last));
        k.Press(VirtualKeys.Return);
        Assert.Equal(last, k.Host.Document, "末尾の候補も入力できる");
    }

    [Test]
    public static void NativeSession_OffersNotesAndCommitsAddress()
    {
        // Mac/Linux の Swift・Python・C++ 側から呼ばれる共通セッションでも動作する。
        var session = new MeltypeSession(CompositionTests.Detector, new UnavailableConverter(), new CompositionOptions(), () => new Meltype.Config.Settings());
        foreach (var c in "004-0000")
            session.HandleKey(c == '-' ? VirtualKeys.OemMinus : c, c, false, false, false, false);
        var result = session.HandleKey(VirtualKeys.Space, null, false, false, false, false);
        Assert.True(result.Consumed && result.View is { Converting: true }, "セッションで変換を開く");
        var index = result.View!.Candidates.ToList().IndexOf("北海道札幌市厚別区");
        Assert.True(index >= 0, "住所候補");
        Assert.Equal("町域未特定", result.View.Notes![index], "注記を OS に渡す");
        Assert.True(result.ToJson().Contains("町域未特定"), "ネイティブ連携 JSON にも注記がある");
        session.SelectCandidate(index);
        var committed = session.HandleKey(VirtualKeys.Return, null, false, false, false, false);
        Assert.Equal("北海道札幌市厚別区", committed.Commits.Single().Text);
    }

    [Test]
    public static void CandidateNumber_CommitsChosenAddress()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("1000001 ");
        k.Press('2');
        Assert.Equal("東京都千代田区千代田", k.Host.Document, "2 番目の候補を数字キーで選べる");
    }

    private sealed class UnavailableConverter : IKanjiConverter
    {
        public string? Convert(string reading) => null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string reading, string? context = null) => null;
    }
}
