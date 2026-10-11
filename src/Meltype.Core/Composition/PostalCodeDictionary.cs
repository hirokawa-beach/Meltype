// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hirokawa-beach

using System.IO.Compression;
using System.Text;

namespace Meltype.Composition;

/// <summary>郵便番号から住所を引く、全 OS 共通のオフライン辞書。</summary>
public static class PostalCodeDictionary
{
    public sealed record Address(string Text, string Note);

    private sealed record Index(byte[] Text, int[] Codes, int[] Starts, int[] Lines);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    // 全住所の文字列・リストを作らず、UTF-8 本体と索引だけを全セッションで共有する。
    private static readonly Lazy<Index> Entries = new(Load);
    private static readonly Lazy<Task> Preparation = new(() => Task.Run(() => { _ = Entries.Value; }));

    /// <summary>起動時に索引を裏で準備する。キー入力の処理は待たせない。</summary>
    public static void WarmUp() => _ = Preparation.Value;

    /// <summary>7 桁、または 3 桁-4 桁のみ受け付ける。番号の先頭の 0 は保持する。</summary>
    public static string? Normalize(string input)
    {
        if (input.Length == 8 && input[3] == '-') input = input[..3] + input[4..];
        return input.Length == 7 && input.All(char.IsAsciiDigit) ? input : null;
    }

    /// <summary>入力を送信せず、同梱データから完全一致の全住所候補を返す。</summary>
    public static IReadOnlyList<Address> Lookup(string input)
    {
        if (Normalize(input) is not { } normalized) return [];
        var data = Entries.Value;
        // 外向きの番号は常に 7 桁の文字列。固定桁の検索索引だけ整数にする。
        var key = 0;
        foreach (var digit in normalized) key = key * 10 + digit - '0';
        var index = Array.BinarySearch(data.Codes, key);
        if (index < 0) return [];
        var start = data.Starts[index];
        var end = index + 1 < data.Starts.Length ? data.Starts[index + 1] : data.Lines.Length - 1;
        var addresses = new Address[end - start];
        for (var row = start; row < end; row++)
        {
            var line = data.Text.AsSpan(data.Lines[row], data.Lines[row + 1] - data.Lines[row] - 1);
            var tab = 8 + line[8..].IndexOf((byte)'\t');
            addresses[row - start] = new Address(Utf8.GetString(line[8..tab]), Utf8.GetString(line[(tab + 1)..]));
        }
        return addresses;
    }

    private static Index Load()
    {
        try
        {
            using var resource = typeof(PostalCodeDictionary).Assembly.GetManifestResourceStream("Meltype.dictionaries.postal-codes.tsv.gz")
                ?? throw new InvalidDataException("郵便番号辞書が見つかりません。");
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            gzip.CopyTo(buffer);
            var text = buffer.ToArray();
            Utf8.GetCharCount(text); // すべての UTF-8 を検査してから候補を使う。
            var codes = new List<int>();
            var starts = new List<int>();
            var lines = new List<int>();
            var previous = -1;
            for (var position = 0; position < text.Length;)
            {
                var length = text.AsSpan(position).IndexOf((byte)'\n');
                if (length < 11) throw new InvalidDataException("郵便番号辞書の行が正しくありません。");
                var line = text.AsSpan(position, length);
                if (line[7] != '\t') throw new InvalidDataException("郵便番号辞書の番号が正しくありません。");
                var code = 0;
                foreach (var digit in line[..7])
                {
                    if (digit is < (byte)'0' or > (byte)'9') throw new InvalidDataException("郵便番号辞書の番号が正しくありません。");
                    code = code * 10 + digit - '0';
                }
                var tab = line[8..].IndexOf((byte)'\t');
                if (tab < 1 || line[(9 + tab)..].IndexOf((byte)'\t') >= 0 || code < previous)
                    throw new InvalidDataException("郵便番号辞書の形式・並び順が正しくありません。");
                if (code != previous)
                {
                    codes.Add(code);
                    starts.Add(lines.Count);
                    previous = code;
                }
                lines.Add(position);
                position += length + 1;
            }
            if (codes.Count == 0) throw new InvalidDataException("郵便番号辞書が空です。");
            lines.Add(text.Length);
            return new Index(text, codes.ToArray(), starts.ToArray(), lines.ToArray());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DecoderFallbackException)
        {
            // 読めない辞書の一部だけを候補に出さず、通常の数字入力・変換は使い続けられるようにする。
            Diagnostics.Log.Warn($"郵便番号辞書を読めませんでした: {ex.Message}");
            return new Index([], [], [], []);
        }
    }
}
