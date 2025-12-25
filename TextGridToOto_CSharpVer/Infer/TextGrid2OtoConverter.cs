using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TextGrid2Oto;

public enum OtoMode
{
    Cvvc = 0,
    Vcv = 1,
    Cvv = 2
}

public sealed class IniLikeConfig
{
    private readonly Dictionary<string, string> _data;

    private IniLikeConfig(Dictionary<string, string> data)
    {
        _data = data;
    }

    public static IniLikeConfig Load(string path)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;

            var idx = line.IndexOf('=');
            if (idx <= 0) continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (key.Length == 0) continue;

            data[key] = value;
        }
        return new IniLikeConfig(data);
    }

    public string? Get(string key) => _data.TryGetValue(key, out var v) ? v : null;

    public string Require(string key)
    {
        var v = Get(key);
        if (string.IsNullOrWhiteSpace(v))
        {
            throw new InvalidOperationException($"Missing config key: {key}");
        }
        return v;
    }

    public int? GetInt(string key)
    {
        var raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
        return null;
    }

    public double[]? GetCsvDoubles(string key)
    {
        var raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Trim().Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            result[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
        }
        return result;
    }
}

public readonly record struct PhoneInterval(double XMin, double XMax, double Middle, string Text);

public readonly record struct ParsedTextGrid(string WavFileName, double WavStart, double WavEnd, IReadOnlyList<PhoneInterval> Phones);

public readonly record struct RawOtoResult(IReadOnlyList<string> CvRawLines, IReadOnlyList<string> VcRawLines);

public readonly record struct OtoEntry(string WavFileName, string Alias, int Left, int Fixed, int Right, int Prevoice, int Overlap);

public sealed class DsDictionary
{
    public required HashSet<string> ValidPhones { get; init; }
    public required Dictionary<string, string> SequenceToWord { get; init; }
    public required int MaxSequenceLength { get; init; }

    public static DsDictionary Load(string path)
    {
        var valid = new HashSet<string>(StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var maxLen = 0;

        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            var seqLen = parts.Length - 1;
            if (seqLen > 0)
            {
                var key = SequenceKey(parts.AsSpan(1));
                map[key] = parts[0];
                if (seqLen > maxLen) maxLen = seqLen;
            }

            switch (parts.Length)
            {
                case 2:
                    valid.Add(parts[1]);
                    break;
                case 3:
                    valid.Add(parts[1]);
                    valid.Add(parts[2]);
                    break;
                case 4:
                    valid.Add(parts[1]);
                    valid.Add(parts[2]);
                    valid.Add(parts[3]);
                    break;
                case 5:
                    valid.Add(parts[1]);
                    valid.Add(parts[2]);
                    valid.Add(parts[3]);
                    valid.Add(parts[4]);
                    break;
                default:
                    for (var i = 1; i < parts.Length; i++) valid.Add(parts[i]);
                    break;
            }
        }

        return new DsDictionary
        {
            ValidPhones = valid,
            SequenceToWord = map,
            MaxSequenceLength = maxLen
        };
    }

    public static string SequenceKey(ReadOnlySpan<string> phones)
    {
        if (phones.Length == 1) return phones[0];
        return string.Join('\u0001', phones.ToArray());
    }
}

public sealed class PresampMappings
{
    public required Dictionary<string, string> CvV { get; init; }
    public required Dictionary<string, string> CvC { get; init; }
    public required HashSet<string> VV { get; init; }

    public static PresampMappings Load(string path)
    {
        var cvV = new Dictionary<string, string>(StringComparer.Ordinal);
        var cvC = new Dictionary<string, string>(StringComparer.Ordinal);

        string? section = null;
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith(';')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if (!string.Equals(section, "VOWEL", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(section, "CONSONANT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split('=', StringSplitOptions.TrimEntries);
            if (parts.Length == 0) continue;

            if (string.Equals(section, "VOWEL", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 3) continue;
                var v0 = parts[0];
                var syllables = parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var s in syllables)
                {
                    cvV[s] = v0;
                }
            }
            else
            {
                if (parts.Length < 2) continue;
                var c0 = parts[0];
                var syllables = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var s in syllables)
                {
                    cvC[s] = c0;
                }
            }
        }

        var vv = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in cvV.Keys)
        {
            if (!cvC.ContainsKey(k)) vv.Add(k);
        }

        return new PresampMappings
        {
            CvV = cvV,
            CvC = cvC,
            VV = vv
        };
    }
}

public static class TextGridParser
{
    private static readonly Regex GlobalXRegex = new(@"(?:\bxmin\b|\bxmax\b)\s*=\s*(\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex TierRegex = new(@"name\s*=\s*""phones""", RegexOptions.Compiled);
    private static readonly Regex IntervalRegex = new(@"intervals\s*\[(\d+)\]\s*:\s*xmin\s*=\s*([\d.]+)\s*xmax\s*=\s*([\d.]+)\s*text\s*=\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.Singleline);

    public static ParsedTextGrid ParseFile(string path)
    {
        var content = ReadAllTextWithBom(path);

        var globalMatches = GlobalXRegex.Matches(content);
        var wavStart = globalMatches.Count >= 1 ? double.Parse(globalMatches[0].Groups[1].Value, CultureInfo.InvariantCulture) : 0d;
        var wavEnd = globalMatches.Count >= 2 ? double.Parse(globalMatches[1].Groups[1].Value, CultureInfo.InvariantCulture) : 0d;

        var tierMatch = TierRegex.Match(content);
        var slice = tierMatch.Success ? content[tierMatch.Index..] : content;

        var intervals = new List<(int Index, PhoneInterval Phone)>();
        foreach (Match m in IntervalRegex.Matches(slice))
        {
            var idx = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var xmin = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var xmax = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            var text = m.Groups[4].Value;
            intervals.Add((idx, new PhoneInterval(xmin, xmax, xmin, text)));
        }

        intervals.Sort((a, b) => a.Index.CompareTo(b.Index));

        var wavFileName = Path.GetFileName(path).Replace(".TextGrid", ".wav", StringComparison.OrdinalIgnoreCase);
        return new ParsedTextGrid(wavFileName, wavStart, wavEnd, intervals.Select(x => x.Phone).ToArray());
    }

    private static string ReadAllTextWithBom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes);
            if (bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes);
        }
        if (bytes.Length >= 3)
        {
            if (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes);
        }
        return Encoding.UTF8.GetString(bytes);
    }
}

public sealed class TextGrid2OtoConverter
{
    private readonly DsDictionary _dsDict;
    private readonly PresampMappings _presamp;
    private readonly HashSet<string> _ignore;
    private readonly OtoMode _mode;
    private readonly double[] _cvSum;
    private readonly double[] _vcSum;
    private readonly double[] _vvSum;

    public TextGrid2OtoConverter(
        string dsDictPath,
        string presampPath,
        string ignoreCsv,
        OtoMode mode,
        double[] cvSum,
        double[] vcSum,
        double[] vvSum)
    {
        _dsDict = DsDictionary.Load(dsDictPath);
        _presamp = PresampMappings.Load(presampPath);
        _ignore = ignoreCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        _mode = mode;
        _cvSum = cvSum;
        _vcSum = vcSum;
        _vvSum = vvSum;
    }

    public RawOtoResult ConvertTextGridDirectory(string textGridDir)
    {
        var cvLines = new List<string>();
        var vcLines = new List<string>();

        foreach (var file in Directory.EnumerateFiles(textGridDir, "*.TextGrid", SearchOption.AllDirectories))
        {
            var tg = TextGridParser.ParseFile(file);
            var phones = FilterPhones(tg.Phones, tg.WavEnd);
            if (phones.Count == 0) continue;

            var wordPhones = PhonesToWords(phones);

            switch (_mode)
            {
                case OtoMode.Cvvc:
                    cvLines.AddRange(OtoGenerator.GenerateCvvcCv(tg.WavFileName, wordPhones, _cvSum, _ignore));
                    vcLines.AddRange(OtoGenerator.GenerateCvvcVc(tg.WavFileName, wordPhones, _presamp, _vcSum, _vvSum, _ignore));
                    break;
                case OtoMode.Vcv:
                    cvLines.AddRange(OtoGenerator.GenerateVcvCv(tg.WavFileName, wordPhones, _cvSum, _ignore));
                    vcLines.AddRange(OtoGenerator.GenerateVcvVc(tg.WavFileName, wordPhones, _presamp, _vcSum, _ignore));
                    break;
                case OtoMode.Cvv:
                    cvLines.AddRange(OtoGenerator.GenerateCvvCv(tg.WavFileName, wordPhones, _cvSum, _ignore));
                    var vvCross = _vvSum.Length >= 5 ? _vvSum[4] : 0d;
                    cvLines = OtoGenerator.ApplyCvvVCross(cvLines, _presamp.VV, vvCross);
                    vcLines.AddRange(OtoGenerator.GenerateCvvVc(tg.WavFileName, wordPhones, _presamp.CvV, _vcSum, _ignore));
                    break;
                default:
                    break;
            }
        }

        return new RawOtoResult(cvLines, vcLines);
    }

    private List<PhoneInterval> FilterPhones(IReadOnlyList<PhoneInterval> phones, double wavEnd)
    {
        var list = new List<PhoneInterval>(phones.Count);
        foreach (var p in phones)
        {
            if (_dsDict.ValidPhones.Contains(p.Text))
            {
                list.Add(p);
                continue;
            }

            if (_ignore.Contains(p.Text))
            {
                list.Add(p with { Text = "R" });
                continue;
            }
        }

        if (list.Count == 0) return list;

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < list.Count - 1; i++)
            {
                var a = list[i].Text;
                var b = list[i + 1].Text;
                if ((a == "R" || a == "-") && (b == "R" || b == "-"))
                {
                    var mergedNext = list[i + 1] with { XMin = list[i].XMin };
                    list[i + 1] = mergedNext;
                    list.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }

        if (list.Count == 0) return list;

        if (list[0].Text != "R")
        {
            list.Insert(0, new PhoneInterval(0d, list[0].XMin, 0d, "R"));
        }

        if (list.Count > 0)
        {
            var last = list[^1];
            if (last.Text != "R" && last.XMax < wavEnd)
            {
                list.Add(new PhoneInterval(last.XMax, wavEnd, last.XMax, "R"));
            }
        }

        return list;
    }

    private List<PhoneInterval> PhonesToWords(IReadOnlyList<PhoneInterval> phones)
    {
        var result = new List<PhoneInterval>();
        var i = 0;
        while (i < phones.Count)
        {
            var cur = phones[i];
            if (cur.Text is "-" or "R")
            {
                result.Add(new PhoneInterval(cur.XMin, cur.XMax, cur.XMin, cur.Text));
                i++;
                continue;
            }

            var maxPossible = Math.Min(_dsDict.MaxSequenceLength, phones.Count - i);
            var matched = false;
            for (var k = maxPossible; k >= 1; k--)
            {
                var seq = new string[k];
                for (var j = 0; j < k; j++) seq[j] = phones[i + j].Text;
                var key = DsDictionary.SequenceKey(seq);

                if (_dsDict.SequenceToWord.TryGetValue(key, out var word))
                {
                    var start = phones[i];
                    var end = phones[i + k - 1];
                    var middle = start.XMax == end.XMax ? start.XMin : start.XMax;
                    result.Add(new PhoneInterval(start.XMin, end.XMax, middle, word));
                    i += k;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                result.Add(new PhoneInterval(cur.XMin, cur.XMax, cur.XMin, cur.Text));
                i++;
            }
        }
        return result;
    }
}

public static class OtoGenerator
{
    public static List<string> GenerateCvvcCv(string wavFileName, IReadOnlyList<PhoneInterval> phones, double[] sum, HashSet<string> ignore)
    {
        var oto = new List<string>();
        var i = 0;
        while (i < phones.Count)
        {
            var cont = phones[i];
            if (i + 1 < phones.Count)
            {
                if (ignore.Contains(cont.Text) && ignore.Contains(phones[i + 1].Text))
                {
                    i += 1;
                    continue;
                }
            }

            if (ignore.Contains(cont.Text) && i < phones.Count - 1)
            {
                var cont2 = phones[i + 1];
                var phoneName = "- " + cont2.Text;
                var left = cont2.XMin * 1000 / sum[0];
                var prevoice = (cont2.Middle - cont2.XMin) * 1000 / sum[3];
                var right = (cont2.XMax - cont2.Middle) * 1000 / sum[2] + prevoice;
                var fixedV = sum[1] == 0 ? prevoice : (cont2.XMax - cont2.Middle) * 1000 / sum[1] + prevoice;
                var cross = prevoice / sum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 2;
                continue;
            }

            {
                var phoneName = cont.Text;
                var left = cont.XMin * 1000 / sum[0];
                var prevoice = (cont.Middle - cont.XMin) * 1000 / sum[3];
                if (sum[3] != 1)
                {
                    left = left + ((cont.Middle - cont.XMin) * 1000 - prevoice);
                }
                var right = (cont.XMax - cont.Middle) * 1000 / sum[2] + prevoice;
                var fixedV = sum[1] == 0 ? prevoice : (cont.XMax - cont.Middle) * 1000 / sum[1] + prevoice;
                var cross = prevoice / sum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 1;
            }
        }
        return oto;
    }

    public static List<string> GenerateCvvcVc(
        string wavFileName,
        IReadOnlyList<PhoneInterval> phones,
        PresampMappings presamp,
        double[] vcSum,
        double[] vvSum,
        HashSet<string> ignore)
    {
        var oto = new List<string>();
        var i = 0;
        while (i < phones.Count - 1)
        {
            var cont = phones[i];
            var cont1 = phones[i + 1];
            if (cont.Text is "R" or "-")
            {
                i += 1;
                continue;
            }

            if (ignore.Contains(cont1.Text) && presamp.CvV.TryGetValue(cont.Text, out var v0))
            {
                var phoneName = v0 + " " + cont1.Text;
                var left = cont.Middle * 1000 + ((cont.XMax - cont.Middle) * 1000 / vcSum[0]);
                var prevoice = cont1.Middle * 1000 - left / vcSum[3];
                var right = (cont1.XMax - cont1.Middle) * 1000 / vcSum[2] + prevoice;
                var fixedV = vcSum[1] == 0 ? prevoice : prevoice + (cont1.XMax - cont1.Middle) * 1000 / vcSum[1];
                var cross = (cont.XMax * 1000 - left) / vcSum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 1;
                continue;
            }

            if (presamp.VV.Contains(cont1.Text) && presamp.CvV.TryGetValue(cont.Text, out var v1))
            {
                var phoneName = v1 + " " + cont1.Text;
                var left = cont.Middle * 1000 + ((cont.XMax - cont.Middle) * 1000 / vvSum[0]);
                var prevoice = cont1.Middle * 1000 - left / vvSum[3];
                var right = (cont1.XMax - cont1.Middle) * 1000 / vvSum[2] + prevoice;
                var fixedV = vvSum[1] == 0 ? prevoice : prevoice + (cont1.XMax - cont1.Middle) * 1000 / vvSum[1];
                var cross = (cont.XMax * 1000 - left) / vvSum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 1;
                continue;
            }

            if (presamp.CvC.TryGetValue(cont1.Text, out var c0) && presamp.CvV.TryGetValue(cont.Text, out var v2))
            {
                var phoneName = v2 + " " + c0;
                var left = cont.Middle * 1000 + ((cont.XMax - cont.Middle) * 1000 / vcSum[0]);
                var prevoice = cont1.XMin * 1000 - left / vcSum[3];
                var right = (cont1.Middle - cont1.XMin) * 1000 / vcSum[2] + prevoice;
                if (Math.Abs(prevoice - right) < 1e-9)
                {
                    if (prevoice <= 20) right += 20;
                    else prevoice -= 20;
                }
                var fixedV = vcSum[1] == 0 ? prevoice : prevoice + (cont1.Middle - cont1.XMin) * 1000 / vcSum[1];
                var cross = (cont.XMax * 1000 - left) / vcSum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 1;
                continue;
            }

            i += 1;
        }
        return oto;
    }

    public static List<string> GenerateVcvCv(string wavFileName, IReadOnlyList<PhoneInterval> phones, double[] sum, HashSet<string> ignore)
    {
        var oto = new List<string>();
        var i = 0;
        while (i < phones.Count - 1)
        {
            var cont = phones[i];
            var cont2 = phones[i + 1];
            if (ignore.Contains(cont.Text) && !ignore.Contains(cont2.Text))
            {
                var phoneName = "- " + cont2.Text;
                var left = cont2.XMin * 1000 / sum[0];
                var prevoice = (cont2.Middle - cont2.XMin) * 1000 / sum[3];
                var right = (cont2.XMax - cont2.Middle) * 1000 / sum[2] + prevoice;
                var fixedV = sum[1] == 0 ? prevoice : prevoice + (right - prevoice) / sum[1];
                var cross = prevoice / sum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 2;
                continue;
            }
            i += 1;
        }
        return oto;
    }

    public static List<string> GenerateVcvVc(
        string wavFileName,
        IReadOnlyList<PhoneInterval> phones,
        PresampMappings presamp,
        double[] sum,
        HashSet<string> ignore)
    {
        var oto = new List<string>();
        var cvV = new Dictionary<string, string>(presamp.CvV, StringComparer.Ordinal);
        cvV["R"] = "R";

        var i = 0;
        while (i < phones.Count - 1)
        {
            var cont = phones[i];
            var cont1 = phones[i + 1];

            if (ignore.Contains(cont.Text))
            {
                i += 1;
                continue;
            }

            if (ignore.Contains(cont1.Text) && cvV.TryGetValue(cont.Text, out var v0))
            {
                var phoneName = v0 + " " + cont1.Text;
                var left = cont.Middle * 1000 + ((cont.XMax - cont.Middle) * 1000 / sum[0]);
                var prevoice = cont1.Middle * 1000 - left / sum[3];
                var right = (cont1.XMax - cont1.Middle) * 1000 / sum[2] + prevoice;
                var fixedV = sum[1] == 0 ? prevoice : prevoice + (cont1.XMax - cont1.Middle) * 1000 / sum[1];
                var cross = (cont.XMax - cont.Middle) * 1000 / sum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 1;
                continue;
            }

            if (cvV.TryGetValue(cont.Text, out var v1) && cvV.TryGetValue(cont1.Text, out _))
            {
                var phoneName = v1 + " " + cont1.Text;
                var left = cont.Middle * 1000 + ((cont.XMax - cont.Middle) * 1000 / sum[0]);
                var prevoice = cont1.Middle * 1000 - left / sum[3];
                var right = (cont1.XMax - cont1.Middle) * 1000 / sum[2] + prevoice;
                var fixedV = sum[1] == 0 ? prevoice : prevoice + (cont1.XMax - cont1.Middle) * 1000 / sum[1];
                var cross = (cont.XMax - cont.Middle) * 1000 / sum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 1;
                continue;
            }

            i += 1;
        }

        return oto;
    }

    public static List<string> GenerateCvvCv(string wavFileName, IReadOnlyList<PhoneInterval> phones, double[] sum, HashSet<string> ignore)
    {
        var oto = new List<string>();
        if (phones.Count == 0) return oto;

        var last = phones[^1];
        if (!ignore.Contains(last.Text))
        {
            var phoneName = last.Text;
            var left = last.XMin * 1000 / sum[0];
            var prevoice = (last.Middle - last.XMin) * 1000 / sum[3];
            var right = (last.XMax - last.Middle) * 1000 / sum[2] + prevoice;
            var fixedV = sum[1] == 0 ? prevoice : prevoice + (right - prevoice) / sum[1];
            var cross = prevoice / sum[4];
            oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
        }

        var i = 0;
        while (i < phones.Count - 1)
        {
            var cont = phones[i];
            if (ignore.Contains(cont.Text))
            {
                var cont2 = phones[i + 1];
                var phoneName = cont2.Text;
                var left = cont2.XMin * 1000 / sum[0];
                var prevoice = (cont2.Middle - cont2.XMin) * 1000 / sum[3];
                var right = (cont2.XMax - cont2.Middle) * 1000 / sum[2] + prevoice;
                var fixedV = sum[1] == 0 ? prevoice : prevoice + (right - prevoice) / sum[1];
                var cross = prevoice / sum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 2;
                continue;
            }

            {
                var phoneName = cont.Text;
                var left = cont.XMin * 1000 / sum[0];
                var prevoice = (cont.Middle - cont.XMin) * 1000 / sum[3];
                var right = (cont.XMax - cont.Middle) * 1000 / sum[2] + prevoice;
                var fixedV = sum[1] == 0 ? prevoice : prevoice + (right - prevoice) / sum[1];
                var cross = prevoice / sum[4];
                oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                i += 1;
            }
        }

        return oto;
    }

    public static List<string> ApplyCvvVCross(List<string> otoLines, HashSet<string> vv, double crossSum)
    {
        if (crossSum == 0) return otoLines;
        var result = new List<string>(otoLines.Count);
        foreach (var line in otoLines)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                result.Add(line);
                continue;
            }

            var wav = line[..eq];
            var rest = line[(eq + 1)..].Trim();
            var parts = rest.Split(',', StringSplitOptions.None);
            if (parts.Length < 6)
            {
                result.Add(line);
                continue;
            }

            var alias = parts[0];
            if (!vv.Contains(alias))
            {
                result.Add(line);
                continue;
            }

            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fixedV))
            {
                result.Add(line);
                continue;
            }

            var cross = fixedV / crossSum;
            parts[5] = cross.ToString("0.########", CultureInfo.InvariantCulture);
            result.Add(wav + "=" + string.Join(',', parts) + "\n");
        }
        return result;
    }

    public static List<string> GenerateCvvVc(
        string wavFileName,
        IReadOnlyList<PhoneInterval> phones,
        Dictionary<string, string> cV,
        double[] vcSum,
        HashSet<string> ignore)
    {
        var oto = new List<string>();
        var normalized = new List<PhoneInterval>(phones.Count);
        foreach (var p in phones)
        {
            if (ignore.Contains(p.Text)) normalized.Add(p with { Text = "R" });
            else normalized.Add(p);
        }

        var i = 0;
        while (i < normalized.Count - 1)
        {
            var cont = normalized[i];
            if (cV.TryGetValue(cont.Text, out var v0))
            {
                {
                    var phoneName = v0 + " " + "R";
                    var left = cont.Middle * 1000 + ((cont.XMax - cont.Middle) * 1000 / vcSum[0]);
                    var prevoice = cont.XMax * 1000 - left / vcSum[3];
                    var right = prevoice + 50;
                    var fixedV = vcSum[1] == 0 ? prevoice : prevoice + (prevoice - right) * 1000 / vcSum[1];
                    var cross = (cont.XMax - cont.Middle) * 1000 / vcSum[4];
                    oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                }

                {
                    var phoneName = "_" + v0;
                    var left = cont.Middle * 1000 + ((cont.XMax - cont.Middle) * 1000 / vcSum[0]);
                    var right = cont.XMax * 1000 - left;
                    var prevoice = right / 4;
                    var fixedV = (right - prevoice) / 4 + prevoice;
                    var cross = prevoice / 2;
                    oto.Add(FormatRawLine(wavFileName, phoneName, left, fixedV, -right, prevoice, cross));
                }

                i += 1;
                continue;
            }

            i += 1;
        }
        return oto;
    }

    private static string FormatRawLine(string wavFileName, string alias, double left, double fixedV, double right, double prevoice, double cross)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{wavFileName}={alias},{left:0.########},{fixedV:0.########},{right:0.########},{prevoice:0.########},{cross:0.########}\n");
    }
}

public static class OtoPostProcessor
{
    public static bool IsZeroOffset(double[] offset)
    {
        if (offset.Length < 5) return true;
        for (var i = 0; i < 5; i++)
        {
            if (Math.Abs(offset[i]) > 1e-9) return false;
        }
        return true;
    }

    public static List<OtoEntry> ParseRawLines(IReadOnlyList<string> lines)
    {
        var result = new List<OtoEntry>(lines.Count);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var wav = line[..eq];
            var rightPart = line[(eq + 1)..];
            var parts = rightPart.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 6) continue;

            var alias = parts[0];
            var left = Round(parts[1]);
            var fixedV = Round(parts[2]);
            var right = Round(parts[3]);
            var prevoice = Round(parts[4]);
            var cross = Round(parts[5]);

            result.Add(new OtoEntry(wav, alias, left, fixedV, right, prevoice, cross));
        }
        return result;
    }

    public static List<OtoEntry> ApplyRepeat(List<OtoEntry> entries, int repeat)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<OtoEntry>(entries.Count);

        foreach (var e in entries)
        {
            var phone = e.Alias;
            if (!counts.TryGetValue(phone, out var count))
            {
                counts[phone] = 1;
                result.Add(e);
                continue;
            }

            if (count < repeat)
            {
                result.Add(e with { Alias = $"{phone}_{count}" });
            }
            counts[phone] = count + 1;
        }

        return result;
    }

    public static List<OtoEntry> ApplyOffset(List<OtoEntry> entries, double[] offset)
    {
        var result = new List<OtoEntry>(entries.Count);
        foreach (var e in entries)
        {
            var leftSum = e.Left + offset[0];
            var fixedSum = e.Fixed + offset[1];
            var rightPosSum = (-e.Right) + offset[2];
            var prevoiceSum = e.Prevoice + offset[3];
            var overlapSum = e.Overlap + offset[4];

            var left = e.Left;
            var fixedV = e.Fixed;
            var right = e.Right;
            var prevoice = e.Prevoice;
            var overlap = e.Overlap;

            if (leftSum >= 0) left = Round(leftSum);

            if (overlapSum >= 0) overlap = Round(overlapSum);
            else overlap = 20;

            if (prevoiceSum >= 0) prevoice = Round(prevoiceSum);

            if (fixedSum >= prevoice) fixedV = Round(fixedSum);
            else fixedV = prevoice;

            if (rightPosSum >= fixedV) right = -Round(rightPosSum);
            else right = -(fixedV + 10);

            result.Add(e with { Left = left, Fixed = fixedV, Right = right, Prevoice = prevoice, Overlap = overlap });
        }
        return result;
    }

    private static int Round(string s) => Round(double.Parse(s, CultureInfo.InvariantCulture));
    private static int Round(double d) => (int)Math.Round(d, MidpointRounding.ToEven);
}

public static class OtoWriter
{
    public static void WriteRawOtoLines(string path, IReadOnlyList<string> lines, string cover)
    {
        var outPath = (cover.Equals("Y", StringComparison.OrdinalIgnoreCase) || cover.Equals("y", StringComparison.OrdinalIgnoreCase))
            ? path
            : UniquePath(path);

        File.WriteAllText(outPath, string.Concat(lines), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void WriteFinalOto(string path, IEnumerable<OtoEntry> entries, string pitch, string cover)
    {
        var outPath = (cover.Equals("Y", StringComparison.OrdinalIgnoreCase) || cover.Equals("y", StringComparison.OrdinalIgnoreCase))
            ? path
            : UniquePath(path);

        var encodings = new[]
        {
            Encoding.UTF8,
            Encoding.GetEncoding("shift-jis")
        };

        var builder = new StringBuilder();
        foreach (var e in entries)
        {
            builder.Append(e.WavFileName);
            builder.Append('=');
            builder.Append(e.Alias);
            builder.Append(pitch);
            builder.Append(',');
            builder.Append(e.Left);
            builder.Append(',');
            builder.Append(e.Fixed);
            builder.Append(',');
            builder.Append(e.Right);
            builder.Append(',');
            builder.Append(e.Prevoice);
            builder.Append(',');
            builder.Append(e.Overlap);
            builder.Append('\n');
        }

        foreach (var enc in encodings)
        {
            try
            {
                File.WriteAllText(outPath, builder.ToString(), enc);
                return;
            }
            catch (EncoderFallbackException)
            {
                continue;
            }
        }

        File.WriteAllText(outPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; i < 100; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i:00}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }
}
