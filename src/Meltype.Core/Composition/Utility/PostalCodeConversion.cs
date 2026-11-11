// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hirokawa-beach

using Meltype.Config;

namespace Meltype.Composition.Utility;

/// <summary>打った番号全体から、同梱辞書の住所を 1 文節の候補にする。</summary>
internal sealed class PostalCodeConversion : IUtilityConversion
{
    private static bool Accepts(CompositionText text, bool direct, bool preferJapanese) =>
        !direct && !text.KanaInput && (text.Mode is DisplayMode.Auto or DisplayMode.Hiragana) &&
        !text.HasProtectedTail && (text.PrecedingEnglish != true || preferJapanese);

    internal static bool Matches(CompositionText text, bool direct) =>
        Accepts(text, direct, preferJapanese: false) && PostalCodeDictionary.Contains(text.Raw);

    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (!Accepts(input.Text, input.Direct, input.PreferJapanese)) return null;
        var raw = input.Text.Raw;
        var settings = input.Context.Settings;
        var types = new PostalCodeDictionary.CandidateTypes(
            settings.PostalBasicAddressCandidates, settings.PostalStreetAndTownCandidates,
            settings.PostalStreetCandidates, settings.PostalFloorNumbers == PostalFloorNumberWidth.HalfWidth,
            settings.PostalKoazaCandidates);
        if (PostalCodeDictionary.Find(raw, types) is not { } addresses) return null;
        // 住所と一緒に、半角・全角の番号も選べるようにする。候補はすべて学習対象外。
        return new UtilityCandidates([raw, .. addresses.Select(a => a.Text), CompositionText.ToFullWidth(raw)], UtilityPlacement.Replace)
        {
            Reading = raw,
            Exclusive = true,
            Notes = [CandidateNote.HalfWidth, .. addresses.Select(a => a.Kind), CandidateNote.FullWidth],
            Details = addresses.Where(a => a.Detail is not null).ToDictionary(a => a.Text, a => a.Detail!, StringComparer.Ordinal),
        };
    }
}
