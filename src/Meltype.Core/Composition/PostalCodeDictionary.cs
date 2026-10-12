// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hirokawa-beach

using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Meltype.Composition;

/// <summary>原文・条件・通り名を保持する、全 OS 共通のオフライン郵便番号辞書。</summary>
public static class PostalCodeDictionary
{
    public sealed record Address(string Text, string Note, string? Detail = null);
    public readonly record struct CandidateTypes(bool BasicAddress, bool StreetAndTown, bool StreetOnly, bool HalfWidthFloorNumbers = false, bool Koaza = false)
    {
        public static CandidateTypes Default => new(true, false, false, false, true);
    }
    internal sealed record Street(string Text, bool AppendTown);
    internal sealed record Building(string Town, string Name, string Floor);
    internal sealed record Record(string Region, string Town, string Kind, string BaseTown, IReadOnlyList<Street> Streets, Building? Building = null, IReadOnlyList<string>? Koaza = null);
    private sealed record Index(byte[] Text, int[] Codes, int[] Starts, int[] Lines);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Lazy<Index> Entries = new(Load);
    private static readonly Lazy<Task> Preparation = new(() => Task.Run(() => { _ = Entries.Value; }));

    /// <summary>起動時に UTF-8 本体と索引を裏で準備する。全住所の文字列は作らない。</summary>
    public static void WarmUp() => _ = Preparation.Value;

    public static string? Normalize(string input)
    {
        if (input.Length == 8 && input[3] == '-') input = input[..3] + input[4..];
        return input.Length == 7 && input.All(char.IsAsciiDigit) ? input : null;
    }

    public static IReadOnlyList<Address> Lookup(string input, CandidateTypes? types = null) => Find(input, types ?? CandidateTypes.Default) ?? [];

    // null は対象番号なし、空のリストは対象番号あり・住所の種類がすべて OFF。
    internal static IReadOnlyList<Address>? Find(string input, CandidateTypes types)
    {
        var records = Records(input);
        return records.Count == 0 ? null : Generate(records, types);
    }

    internal static IReadOnlyList<Record> Records(string input)
    {
        if (Normalize(input) is not { } normalized) return [];
        var data = Entries.Value;
        var key = 0;
        foreach (var digit in normalized) key = key * 10 + digit - '0';
        var index = Array.BinarySearch(data.Codes, key);
        if (index < 0) return [];
        var start = data.Starts[index];
        var end = index + 1 < data.Starts.Length ? data.Starts[index + 1] : data.Lines.Length - 1;
        var records = new Record[end - start];
        for (var row = start; row < end; row++)
        {
            var fields = Utf8.GetString(data.Text.AsSpan(data.Lines[row], data.Lines[row + 1] - data.Lines[row] - 1)).Split('\t');
            var streets = new List<Street>();
            if (fields[5].Length > 0)
            {
                // JsonDocument は reflection を使わず、NativeAOT でも同じデータを読める。
                using var json = JsonDocument.Parse(fields[5]);
                foreach (var street in json.RootElement.EnumerateArray()) streets.Add(new Street(street[0].GetString()!, street[1].GetBoolean()));
            }
            Building? building = null;
            if (fields[6].Length > 0)
            {
                using var json = JsonDocument.Parse(fields[6]);
                building = new Building(json.RootElement[0].GetString()!, json.RootElement[1].GetString()!, json.RootElement[2].GetString()!);
            }
            List<string>? koaza = null;
            if (fields[7].Length > 0)
            {
                using var json = JsonDocument.Parse(fields[7]);
                koaza = json.RootElement.EnumerateArray().Select(n => n.GetString()!).ToList();
            }
            records[row - start] = new Record(fields[1], fields[2], fields[3], fields[4], streets, building, koaza);
        }
        return records;
    }

    internal static IReadOnlyList<Address> Generate(IReadOnlyList<Record> records, CandidateTypes types)
    {
        var result = new List<Address>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string text, string note, string? detail)
        {
            if (!positions.TryGetValue(text, out var index))
            {
                positions[text] = result.Count;
                result.Add(new Address(text, note, detail));
                return;
            }
            // 同じ挿入文字列の条件・原文は結合する。条件が違う行を無言で捨てない。
            var old = result[index];
            if (detail is not null && !(old.Detail?.Split("\n\n").Contains(detail) ?? false))
                result[index] = old with { Note = old.Note == note ? note : "条件・複数表記", Detail = old.Detail is null ? detail : old.Detail + "\n\n" + detail };
        }
        static bool HasKoaza(Record row) => row.Kind == "koaza" && row.BaseTown.Length > 0 && row.Koaza is { Count: > 0 };
        if (types.Koaza)
        {
            foreach (var row in records.Where(HasKoaza))
            foreach (var name in row.Koaza!)
                Add(row.Region + row.BaseTown + name, "小字込み", "選択した小字: " + name + "\n町域原文: " + row.Town);
        }
        // 小字がある行は ON なら小字だけ、OFF なら大字までを出す。郵便番号自体は呼び出し側が先頭に残す。
        if (types.BasicAddress)
        {
            foreach (var row in records)
            {
                if (types.Koaza && HasKoaza(row)) continue;
                if (row.Building is { } building)
                {
                    var floor = building.Floor;
                    if (!types.HalfWidthFloorNumbers)
                        floor = string.Concat(floor.Select(c => char.IsAsciiDigit(c) ? (char)(c + 0xFEE0) : c));
                    Add(row.Region + building.Town + " " + building.Name + (floor.Length > 0 ? floor + "階" : ""),
                        floor.Length > 0 ? "建物・階" : "建物（地階・階層不明）", "町域原文: " + row.Town);
                    continue;
                }
                var town = row.Kind is "fallback" or "municipality" ? "" : row.BaseTown.Length > 0 ? row.BaseTown : row.Town;
                var note = row.Kind switch
                {
                    "normal" => "住所", "fallback" => "町域未特定",
                    "municipality" => row.Town.EndsWith("一円", StringComparison.Ordinal) ? "町域なし" : "市区町村の次に番地",
                    "koaza" when row.BaseTown.Length > 0 => "大字・町域まで",
                    "kyoto" when row.BaseTown.Length > 0 => "基本住所",
                    "range" when row.BaseTown.Length > 0 => "範囲条件あり",
                    "restricted" when row.BaseTown.Length > 0 => "除外・限定条件",
                    _ => "原文保持",
                };
                Add(row.Region + town, note, row.Kind == "normal" ? null : "町域原文: " + row.Town);
            }
        }
        if (types.StreetAndTown || types.StreetOnly)
        {
            foreach (var row in records.Where(r => r.Kind == "kyoto" && r.BaseTown.Length > 0))
            foreach (var street in row.Streets)
            {
                var detail = "選択した通り名・位置説明: " + street.Text + "\n町域原文: " + row.Town;
                if (types.StreetAndTown && street.AppendTown && !street.Text.EndsWith(row.BaseTown, StringComparison.Ordinal))
                    Add(row.Region + street.Text + row.BaseTown, "通り名＋町名", detail);
                if (types.StreetOnly) Add(row.Region + street.Text, "通り名のみ", detail);
            }
        }
        return result;
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
            Utf8.GetCharCount(text);
            var codes = new List<int>();
            var starts = new List<int>();
            var lines = new List<int>();
            var previous = -1;
            for (var position = 0; position < text.Length;)
            {
                var length = text.AsSpan(position).IndexOf((byte)'\n');
                if (length < 13) throw new InvalidDataException("郵便番号辞書の行が正しくありません。");
                var line = text.AsSpan(position, length);
                if (line[7] != '\t') throw new InvalidDataException("郵便番号辞書の番号が正しくありません。");
                var code = 0;
                foreach (var digit in line[..7])
                {
                    if (digit is < (byte)'0' or > (byte)'9') throw new InvalidDataException("郵便番号辞書の番号が正しくありません。");
                    code = code * 10 + digit - '0';
                }
                var tabs = 0;
                foreach (var character in line) if (character == '\t') tabs++;
                if (tabs != 7 || code < previous) throw new InvalidDataException("郵便番号辞書の形式・並び順が正しくありません (schema 4)。");
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
            Diagnostics.Log.Warn($"郵便番号辞書を読めませんでした: {ex.Message}");
            return new Index([], [], [], []);
        }
    }
}
