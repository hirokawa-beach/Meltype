// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

namespace Meltype.Composition;

/// <summary>候補に付ける注記の種類。</summary>
public enum CandidateNote
{
    None,
    HalfWidth,
    FullWidth,
    Translation,
    PostalAddress,
    PostalKoaza,
    PostalAirport,
    PostalFacility,
    PostalBuildingFloor,
    PostalBuilding,
    PostalUnspecifiedTown,
    PostalNoTown,
    PostalLotAfterMunicipality,
    PostalTown,
    PostalBasic,
    PostalRange,
    PostalRestriction,
    PostalOriginal,
    PostalStreetAndTown,
    PostalStreet,
    PostalMultiple,
}

public static class CandidateNoteExtensions
{
    /// <summary>注記を表示用の文字列へ変換する。None は null を返す。</summary>
    public static string? ToDisplayText(this CandidateNote note) => note switch
    {
        CandidateNote.None => null,
        CandidateNote.HalfWidth => "半角",
        CandidateNote.FullWidth => "全角",
        CandidateNote.Translation => "英訳",
        CandidateNote.PostalAddress => "住所",
        CandidateNote.PostalKoaza => "小字込み",
        CandidateNote.PostalAirport => "空港内",
        CandidateNote.PostalFacility => "施設",
        CandidateNote.PostalBuildingFloor => "建物・階",
        CandidateNote.PostalBuilding => "建物（地階・階層不明）",
        CandidateNote.PostalUnspecifiedTown => "町域未特定",
        CandidateNote.PostalNoTown => "町域なし",
        CandidateNote.PostalLotAfterMunicipality => "市区町村の次に番地",
        CandidateNote.PostalTown => "大字・町域まで",
        CandidateNote.PostalBasic => "基本住所",
        CandidateNote.PostalRange => "範囲条件あり",
        CandidateNote.PostalRestriction => "除外・限定条件",
        CandidateNote.PostalOriginal => "原文保持",
        CandidateNote.PostalStreetAndTown => "通り名＋町名",
        CandidateNote.PostalStreet => "通り名のみ",
        CandidateNote.PostalMultiple => "条件・複数表記",
        _ => throw new ArgumentOutOfRangeException(nameof(note), note, "候補の注記が不正です。"),
    };
}
