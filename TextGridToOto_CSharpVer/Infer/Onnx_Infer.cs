using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TextGridToOto_CSharpVer.Infer;

public static class FaAutoAnnotator
{
    public static void RunFolder(
        string wavFolder,
        string labFolder,
        string modelFolder,
        string language = "zh",
        string g2p = "dictionary",
        string? dictionaryPath = null,
        string nonLexicalPhonemes = "AP,EP",
        int padTimes = 1,
        double padLength = 5.0,
        Action<int, int, string>? progressCallback = null)
    {
        if (string.IsNullOrWhiteSpace(wavFolder) || !Directory.Exists(wavFolder))
        {
            throw new DirectoryNotFoundException($"Wav folder does not exist: {wavFolder}");
        }
        if (string.IsNullOrWhiteSpace(labFolder) || !Directory.Exists(labFolder))
        {
            throw new DirectoryNotFoundException($"Lab folder does not exist: {labFolder}");
        }
        if (string.IsNullOrWhiteSpace(modelFolder) || !Directory.Exists(modelFolder))
        {
            throw new DirectoryNotFoundException($"Model folder does not exist: {modelFolder}");
        }

        var modelPath = Path.Combine(modelFolder, "model.onnx");
        if (!File.Exists(modelPath))
        {
            var fallback = Directory.EnumerateFiles(modelFolder, "*.onnx", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (fallback is null)
            {
                throw new FileNotFoundException($"Cannot find any .onnx model in: {modelFolder}");
            }
            modelPath = fallback;
        }

        var inference = new InferenceOnnx(modelPath);
        inference.LoadConfig();
        inference.InitDecoder();
        inference.LoadModel();
        inference.GetDatasetFromLabFolder(wavFolder, labFolder, language, g2p, dictionaryPath);
        inference.Infer(nonLexicalPhonemes, padTimes, padLength, progressCallback);
        inference.Export(wavFolder, new[] { "textgrid" });
    }
}

internal static class OnnxCli
{
    public static int Execute(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  infer --onnx_path <model.onnx> [--wav_folder <segments>] [--out_path <out>] [--g2p dictionary|phoneme] [--non_lexical_phonemes AP,EP] [--language zh] [--dictionary <path>] [--pad_times 1] [--pad_length 5]");
                Console.WriteLine("  export-model --nll_path <nll.ckpt> --fa_path <fa.ckpt> [--hubert_path <dir>] --out_folder <out>");
                return 0;
            }

            var mode = args[0];
            var modeArgs = args.Skip(1).ToArray();

            if (string.Equals(mode, "infer", StringComparison.OrdinalIgnoreCase))
            {
                return RunInfer(modeArgs);
            }

            if (string.Equals(mode, "export-model", StringComparison.OrdinalIgnoreCase))
            {
                return RunExportModel(modeArgs);
            }

            Console.Error.WriteLine($"Unknown mode: {mode}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static int RunInfer(string[] args)
    {
        var parsed = CliArgs.Parse(args);
        var onnxPath = parsed.GetRequiredPath("--onnx_path", "-m");
        var wavFolder = parsed.GetPath("--wav_folder", "-wf") ?? Path.Combine(Environment.CurrentDirectory, "segments");
        var outPath = parsed.GetPath("--out_path", "-o");
        var g2p = parsed.Get("--g2p", "-g") ?? "dictionary";
        var nonLexicalPhonemes = parsed.Get("--non_lexical_phonemes", "-np") ?? "AP";
        var language = parsed.Get("--language", "-l") ?? "zh";
        var dictionaryPath = parsed.GetPath("--dictionary", "-d");
        var padTimes = parsed.GetInt("--pad_times", "-pt") ?? 1;
        var padLength = parsed.GetDouble("--pad_length", "-pl") ?? 5.0;

        if (!File.Exists(onnxPath) || !string.Equals(Path.GetExtension(onnxPath), ".onnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException($"Path {onnxPath} does not exist or is not a onnx file.");
        }

        var inference = new InferenceOnnx(onnxPath);
        inference.LoadConfig();
        inference.InitDecoder();
        inference.LoadModel();
        inference.GetDataset(wavFolder, language, g2p, dictionaryPath);
        inference.Infer(nonLexicalPhonemes, padTimes, padLength);
        inference.Export(outPath is null ? wavFolder : outPath, new[] { "textgrid" });
        return 0;
    }

    private static int RunExportModel(string[] args)
    {
        var parsed = CliArgs.Parse(args);
        var nllPath = parsed.GetRequiredPath("--nll_path", "-nll");
        var faPath = parsed.GetRequiredPath("--fa_path", "-fa");
        var hubertPath = parsed.GetPath("--hubert_path", "-h");
        var outFolder = parsed.GetRequiredPath("--out_folder", "-o");

        var repoRoot = FindRepoRoot(Environment.CurrentDirectory);
        var exporterScript = Path.Combine(repoRoot, "scripts", "onnx_exporter.py");
        if (!File.Exists(exporterScript))
        {
            throw new FileNotFoundException($"Cannot find exporter script at: {exporterScript}");
        }

        var argBuilder = new List<string>
        {
            Quote(exporterScript),
            "--nll_path", Quote(nllPath),
            "--fa_path", Quote(faPath),
            "--out_folder", Quote(outFolder)
        };
        if (!string.IsNullOrWhiteSpace(hubertPath))
        {
            argBuilder.Add("--hubert_path");
            argBuilder.Add(Quote(hubertPath));
        }

        return ProcessUtils.Run("python", string.Join(" ", argBuilder), repoRoot);
    }

    private static string FindRepoRoot(string startDir)
    {
        var current = new DirectoryInfo(startDir);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "onnx_infer.py")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return startDir;
    }

    private static string Quote(string value)
    {
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\\\"");
        }
        return $"\"{value}\"";
    }
}

sealed class CliArgs
{
    private readonly Dictionary<string, string?> _args;

    private CliArgs(Dictionary<string, string?> args)
    {
        _args = args;
    }

    public static CliArgs Parse(string[] args)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                dict[token] = args[i + 1];
                i++;
            }
            else
            {
                dict[token] = "true";
            }
        }
        return new CliArgs(dict);
    }

    public string? Get(params string[] keys)
    {
        foreach (var k in keys)
        {
            if (_args.TryGetValue(k, out var v))
            {
                return v;
            }
        }
        return null;
    }

    public string? GetPath(params string[] keys)
    {
        var v = Get(keys);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    public string GetRequiredPath(params string[] keys)
    {
        var v = GetPath(keys);
        if (string.IsNullOrWhiteSpace(v))
        {
            throw new ArgumentException($"Missing required argument: {string.Join("/", keys)}");
        }
        return v!;
    }

    public int? GetInt(params string[] keys)
    {
        var v = Get(keys);
        if (string.IsNullOrWhiteSpace(v))
        {
            return null;
        }
        return int.Parse(v, CultureInfo.InvariantCulture);
    }

    public double? GetDouble(params string[] keys)
    {
        var v = Get(keys);
        if (string.IsNullOrWhiteSpace(v))
        {
            return null;
        }
        return double.Parse(v, CultureInfo.InvariantCulture);
    }
}

static class ProcessUtils
{
    public static int Run(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();
        return proc.ExitCode;
    }
}

sealed class InferenceOnnx : InferenceBase
{
    private readonly string _onnxPath;
    private InferenceSession? _session;

    public InferenceOnnx(string onnxPath)
    {
        _onnxPath = onnxPath;
        ModelFolder = Path.GetDirectoryName(_onnxPath) ?? Environment.CurrentDirectory;
    }

    public string ModelFolder { get; }

    public override void LoadConfig()
    {
        var vocabPath = Path.Combine(ModelFolder, "vocab.json");
        var configPath = Path.Combine(ModelFolder, "config.json");
        var versionPath = Path.Combine(ModelFolder, "VERSION");

        if (!File.Exists(vocabPath)) throw new FileNotFoundException($"{vocabPath} does not exist");
        if (!File.Exists(configPath)) throw new FileNotFoundException($"{configPath} does not exist");
        if (!File.Exists(versionPath)) throw new FileNotFoundException($"{versionPath} does not exist");

        var versionText = File.ReadAllText(versionPath, Encoding.UTF8).Split('\n', '\r')[0].Trim();
        if (!int.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version != 5)
        {
            throw new InvalidOperationException("onnx model version must be 5.");
        }

        Vocab = ModelJson.LoadVocab(vocabPath);
        MelCfg = ModelJson.LoadMelConfig(configPath);
        VocabFolder = ModelFolder;

        var dictionaries = Vocab.Dictionaries;
        foreach (var (lang, dictName) in dictionaries)
        {
            if (string.IsNullOrWhiteSpace(dictName)) continue;
            var dictPath = Path.Combine(ModelFolder, dictName);
            if (!File.Exists(dictPath))
            {
                throw new FileNotFoundException($"{Path.GetFullPath(dictPath)} does not exist");
            }
        }
    }

    public override void LoadModel()
    {
        var modelPath = Path.Combine(ModelFolder, "model.onnx");
        if (!File.Exists(modelPath))
        {
            modelPath = _onnxPath;
        }

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        _session = new InferenceSession(modelPath, options);
    }

    protected override (WordList Words, List<List<Word>> NonLexicalWords) InferCore(
        float[] paddedWav,
        int paddedFrames,
        List<string> wordSeq,
        List<string> phSeq,
        List<int> phIdxToWordIdx,
        double wavLength,
        List<string> nonLexicalPhonemes)
    {
        if (_session is null) throw new InvalidOperationException("Model is not loaded.");

        var inputTensor = new DenseTensor<float>(new[] { 1, paddedWav.Length });
        for (var i = 0; i < paddedWav.Length; i++)
        {
            inputTensor[0, i] = paddedWav[i];
        }

        var input = NamedOnnxValue.CreateFromTensor("waveform", inputTensor);
        using var outputs = _session.Run(new[] { input });

        Tensor<float> GetOutput(string name)
        {
            var match = outputs.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.Ordinal));
            if (match is null) throw new InvalidOperationException($"Missing output: {name}");
            return match.AsTensor<float>();
        }

        var phFrameLogitsT = GetOutput("ph_frame_logits");
        var phEdgeLogitsT = GetOutput("ph_edge_logits");
        var cvntLogitsT = GetOutput("cvnt_logits");

        var phFrameLogits = TensorUtils.ExtractPhFrameLogits(phFrameLogitsT, paddedFrames);
        var phEdgeLogits = TensorUtils.ExtractPhEdgeLogits(phEdgeLogitsT, paddedFrames);
        var cvntLogits = TensorUtils.ExtractCvntLogits(cvntLogitsT, paddedFrames);

        var words = FaDecoder.Decode(
            phFrameLogits,
            phEdgeLogits,
            wavLength,
            phSeq,
            wordSeq,
            phIdxToWordIdx,
            ignoreSp: true);

        var nonLexicalWords = NllDecoder.Decode(
            cvntLogits,
            wavLength,
            nonLexicalPhonemes);

        return (words, nonLexicalWords);
    }
}

abstract class InferenceBase
{
    public VocabConfig Vocab { get; protected set; } = new();
    public MelSpecConfig MelCfg { get; protected set; } = new();
    public string VocabFolder { get; protected set; } = Environment.CurrentDirectory;

    protected AlignmentDecoder FaDecoder { get; private set; } = default!;
    protected NonLexicalDecoder NllDecoder { get; private set; } = default!;

    private readonly List<(string WavPath, List<string> PhSeq, List<string> WordSeq, List<int> PhIdxToWordIdx)> _dataset = new();
    private readonly List<(string WavPath, double WavLength, WordList Words)> _predictions = new();

    public abstract void LoadConfig();
    public abstract void LoadModel();

    public void InitDecoder()
    {
        NllDecoder = new NonLexicalDecoder(Vocab, new[] { "None" }.Concat(Vocab.NonLexicalPhonemes).ToList(), MelCfg.SampleRate, MelCfg.HopSize);
        FaDecoder = new AlignmentDecoder(Vocab, MelCfg.SampleRate, MelCfg.HopSize);
    }

    public void GetDataset(string wavFolder, string language, string g2p, string? dictionaryPath, string inFormat = "lab")
    {
        dictionaryPath ??= Path.Combine(VocabFolder, Vocab.Dictionaries.TryGetValue(language, out var v) ? v : "");
        var langPrefix = Vocab.LanguagePrefix ? language : null;

        BaseG2P g2pImpl = g2p switch
        {
            "dictionary" => new DictionaryG2P(langPrefix, dictionaryPath),
            "phoneme" => new PhonemeG2P(langPrefix),
            _ => throw new ArgumentException($"g2p - {g2p} is not supported, which should be 'dictionary' or 'phoneme'.")
        };

        if (!Directory.Exists(wavFolder))
        {
            throw new DirectoryNotFoundException($"Input folder does not exist: {wavFolder}");
        }

        foreach (var wavPath in Directory.EnumerateFiles(wavFolder, "*.wav", SearchOption.AllDirectories))
        {
            var labPath = Path.ChangeExtension(wavPath, "." + inFormat);
            if (!File.Exists(labPath))
            {
                continue;
            }

            var labText = File.ReadAllText(labPath, Encoding.UTF8).Trim();
            var (phSeq, wordSeq, phIdxToWordIdx) = g2pImpl.Convert(labText);
            _dataset.Add((wavPath, phSeq, wordSeq, phIdxToWordIdx));
        }

        Console.WriteLine($"Loaded {_dataset.Count} samples.");
    }

    public void GetDatasetFromLabFolder(string wavFolder, string labFolder, string language, string g2p, string? dictionaryPath)
    {
        dictionaryPath ??= Path.Combine(VocabFolder, Vocab.Dictionaries.TryGetValue(language, out var v) ? v : "");
        var langPrefix = Vocab.LanguagePrefix ? language : null;

        BaseG2P g2pImpl = g2p switch
        {
            "dictionary" => new DictionaryG2P(langPrefix, dictionaryPath),
            "phoneme" => new PhonemeG2P(langPrefix),
            _ => throw new ArgumentException($"g2p - {g2p} is not supported, which should be 'dictionary' or 'phoneme'.")
        };

        if (!Directory.Exists(wavFolder))
        {
            throw new DirectoryNotFoundException($"Input wav folder does not exist: {wavFolder}");
        }
        if (!Directory.Exists(labFolder))
        {
            throw new DirectoryNotFoundException($"Input lab folder does not exist: {labFolder}");
        }

        var wavMap = Directory.EnumerateFiles(wavFolder, "*.wav", SearchOption.TopDirectoryOnly)
            .ToDictionary(p => Path.GetFileNameWithoutExtension(p)!, p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var labPath in Directory.EnumerateFiles(labFolder, "*.lab", SearchOption.TopDirectoryOnly))
        {
            var baseName = Path.GetFileNameWithoutExtension(labPath);
            if (!wavMap.TryGetValue(baseName, out var wavPath))
            {
                continue;
            }

            var labText = File.ReadAllText(labPath, Encoding.UTF8).Trim();
            if (labText.Length == 0)
            {
                continue;
            }

            var (phSeq, wordSeq, phIdxToWordIdx) = g2pImpl.Convert(labText);
            _dataset.Add((wavPath, phSeq, wordSeq, phIdxToWordIdx));
        }

        Console.WriteLine($"Loaded {_dataset.Count} samples.");
    }

    public void Infer(string nonLexicalPhonemes, int padTimes = 1, double padLength = 5.0, Action<int, int, string>? progressCallback = null)
    {
        var nonLexicalList = nonLexicalPhonemes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!nonLexicalList.All(p => Vocab.NonLexicalPhonemes.Contains(p)))
        {
            throw new ArgumentException("The non_lexical_phonemes contain elements that are not included in the vocab.");
        }

        var padLengths = padTimes > 1
            ? Enumerable.Range(0, padTimes).Select(i => Math.Round(padLength / padTimes * i, 1)).ToList()
            : new List<double> { 0.0 };

        for (var i = 0; i < _dataset.Count; i++)
        {
            var (wavPath, phSeq, wordSeq, phIdxToWordIdx) = _dataset[i];

            var wav = AudioUtils.LoadMonoResampledFloat(wavPath, MelCfg.SampleRate);
            var wavLength = wav.Length / (double)MelCfg.SampleRate;

            var wordsList = new List<WordList>();
            foreach (var pl in padLengths)
            {
                var paddedSamples = (int)(pl * MelCfg.SampleRate);
                var paddedFrames = (int)(paddedSamples / (double)MelCfg.HopSize);

                var paddedWav = new float[wav.Length + paddedSamples];
                Array.Copy(wav, 0, paddedWav, paddedSamples, wav.Length);

                var (words, nonLexicalWords) = InferCore(
                    paddedWav,
                    paddedFrames,
                    wordSeq,
                    phSeq,
                    phIdxToWordIdx,
                    wavLength,
                    nonLexicalList
                );

                foreach (var tagWords in nonLexicalWords)
                {
                    foreach (var w in tagWords)
                    {
                        words.AddAP(w);
                    }
                }

                words.ClearLanguagePrefix();
                wordsList.Add(words);
            }

            var phLists = wordsList.Select(w => w.Phonemes).ToList();
            var keepIndices = InferenceMath.FindAllDuplicatePhonemes(phLists);
            wordsList = keepIndices.Select(idx => wordsList[idx]).ToList();

            var phonemesAll = new List<Phoneme>();
            var resultWord = new WordList();

            for (var wIdx = 0; wIdx < wordsList[0].Count; wIdx++)
            {
                var word0 = wordsList[0][wIdx];
                var phonemes = new List<Phoneme>();
                for (var phIdx = 0; phIdx < word0.Phonemes.Count; phIdx++)
                {
                    var starts = wordsList.Select(words => words[wIdx].Phonemes[phIdx].Start).ToList();
                    var ends = wordsList.Select(words => words[wIdx].Phonemes[phIdx].End).ToList();

                    var phStart = InferenceMath.RemoveOutliersPerPosition(new List<List<double>> { starts })[0];
                    var phEnd = InferenceMath.RemoveOutliersPerPosition(new List<List<double>> { ends })[0];

                    phStart = Math.Max(phStart, phonemesAll.Count > 0 ? phonemesAll[^1].End : 0);
                    phEnd = Math.Max(phStart + 0.0001, phEnd);

                    var text = word0.Phonemes[phIdx].Text;
                    var ph = new Phoneme(phStart, phEnd, text);
                    phonemes.Add(ph);
                    phonemesAll.Add(ph);
                }

                var word = new Word(phonemes[0].Start, phonemes[^1].End, word0.Text);
                foreach (var ph in phonemes)
                {
                    word.AppendPhoneme(ph);
                }
                resultWord.Append(word);
            }

            resultWord.FillSmallGaps(wavLength);
            resultWord.AddSP(wavLength, "SP");
            _predictions.Add((wavPath, wavLength, resultWord));

            progressCallback?.Invoke(i + 1, _dataset.Count, Path.GetFileNameWithoutExtension(wavPath));
        }
    }

    public void Export(string outputFolder, IReadOnlyList<string>? outputFormat = null)
    {
        outputFormat ??= new[] { "textgrid" };
        if (outputFormat.Contains("textgrid", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Saving TextGrids...");
            foreach (var (wavPath, wavLength, words) in _predictions)
            {
                var outDir = Path.Combine(outputFolder, "TextGrid");
                Directory.CreateDirectory(outDir);
                var tgPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(wavPath) + ".TextGrid");
                TextGridWriter.Write(tgPath, wavLength, words);
            }
        }

        Console.WriteLine("Output files are saved to the same folder as the input wav files.");
    }

    protected abstract (WordList Words, List<List<Word>> NonLexicalWords) InferCore(
        float[] paddedWav,
        int paddedFrames,
        List<string> wordSeq,
        List<string> phSeq,
        List<int> phIdxToWordIdx,
        double wavLength,
        List<string> nonLexicalPhonemes);
}

static class AudioUtils
{
    public static float[] LoadMonoResampledFloat(string wavPath, int targetSampleRate)
    {
        using var reader = new AudioFileReader(wavPath);
        ISampleProvider provider = reader;

        if (provider.WaveFormat.Channels == 2)
        {
            provider = new StereoToMonoSampleProvider(provider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }
        else if (provider.WaveFormat.Channels != 1)
        {
            throw new NotSupportedException($"Only mono/stereo audio is supported. Channels={provider.WaveFormat.Channels}");
        }

        if (provider.WaveFormat.SampleRate != targetSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, targetSampleRate);
        }

        var buffer = new float[targetSampleRate];
        var samples = new List<float>(targetSampleRate * 10);
        while (true)
        {
            var read = provider.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            for (var i = 0; i < read; i++)
            {
                samples.Add(buffer[i]);
            }
        }

        return samples.ToArray();
    }
}

static class ModelJson
{
    public static VocabConfig LoadVocab(string vocabPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(vocabPath, Encoding.UTF8));
        var root = doc.RootElement;

        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var prop in root.GetProperty("vocab").EnumerateObject())
        {
            vocab[prop.Name] = prop.Value.GetInt32();
        }

        var nonLexical = root.GetProperty("non_lexical_phonemes").EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList();
        var dictionaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.GetProperty("dictionaries").EnumerateObject())
        {
            dictionaries[prop.Name] = prop.Value.GetString() ?? "";
        }

        var languagePrefix = root.TryGetProperty("language_prefix", out var lp) && lp.ValueKind == JsonValueKind.True;
        var vocabSize = root.TryGetProperty("vocab_size", out var vs) ? vs.GetInt32() : vocab.Count;

        return new VocabConfig
        {
            Vocab = vocab,
            VocabSize = vocabSize,
            NonLexicalPhonemes = nonLexical,
            Dictionaries = dictionaries,
            LanguagePrefix = languagePrefix
        };
    }

    public static MelSpecConfig LoadMelConfig(string configPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath, Encoding.UTF8));
        var root = doc.RootElement;
        var mel = root.GetProperty("mel_spec_config");
        return new MelSpecConfig
        {
            SampleRate = mel.GetProperty("sample_rate").GetInt32(),
            HopSize = mel.GetProperty("hop_size").GetInt32()
        };
    }
}

sealed class VocabConfig
{
    public Dictionary<string, int> Vocab { get; init; } = new(StringComparer.Ordinal);
    public int VocabSize { get; init; }
    public List<string> NonLexicalPhonemes { get; init; } = new();
    public Dictionary<string, string> Dictionaries { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool LanguagePrefix { get; init; } = true;
}

sealed class MelSpecConfig
{
    public int SampleRate { get; init; } = 44100;
    public int HopSize { get; init; } = 441;
}

abstract class BaseG2P
{
    protected readonly string? Language;

    protected BaseG2P(string? language)
    {
        Language = language;
    }

    protected abstract (List<string> PhSeq, List<string> WordSeq, List<int> PhIdxToWordIdx) G2P(string inputText);

    public (List<string> PhSeq, List<string> WordSeq, List<int> PhIdxToWordIdx) Convert(string text)
    {
        var (phSeq, wordSeq, phIdxToWordIdx) = G2P(text);

        if (phSeq.Count < 2 || phSeq[0] != "SP" || phSeq[^1] != "SP")
        {
            throw new InvalidOperationException("The first and last phonemes should be `SP`.");
        }
        for (var i = 0; i < phSeq.Count - 1; i++)
        {
            if (phSeq[i] == "SP" && phSeq[i + 1] == "SP")
            {
                throw new InvalidOperationException("There should not be more than two consecutive `SP`s.");
            }
        }

        if (Language is not null)
        {
            for (var i = 0; i < phSeq.Count; i++)
            {
                if (phSeq[i] != "SP")
                {
                    phSeq[i] = $"{Language}/{phSeq[i]}";
                }
            }
        }
        return (phSeq, wordSeq, phIdxToWordIdx);
    }
}

sealed class PhonemeG2P : BaseG2P
{
    public PhonemeG2P(string? language) : base(language) { }

    protected override (List<string> PhSeq, List<string> WordSeq, List<int> PhIdxToWordIdx) G2P(string inputText)
    {
        var words = inputText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w != "SP").ToList();
        var phSeq = new List<string> { "SP" };
        var map = new List<int> { -1 };

        for (var i = 0; i < words.Count; i++)
        {
            phSeq.Add(words[i]);
            map.Add(i);
            phSeq.Add("SP");
            map.Add(-1);
        }

        return (phSeq, words, map);
    }
}

sealed class DictionaryG2P : BaseG2P
{
    private readonly Dictionary<string, List<string>> _dict;

    public DictionaryG2P(string? language, string dictPath) : base(language)
    {
        if (!File.Exists(dictPath))
        {
            throw new FileNotFoundException($"{Path.GetFullPath(dictPath)} does not exist.");
        }

        _dict = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(dictPath, Encoding.UTF8))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            var parts = trimmed.Split('\t', 2);
            if (parts.Length != 2) continue;
            var word = parts[0].Trim();
            var phs = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            _dict[word] = phs;
        }
    }

    protected override (List<string> PhSeq, List<string> WordSeq, List<int> PhIdxToWordIdx) G2P(string inputText)
    {
        var wordSeqRaw = inputText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordSeq = new List<string>();
        var phSeq = new List<string> { "SP" };
        var map = new List<int> { -1 };
        var wordSeqIdx = 0;

        foreach (var word in wordSeqRaw)
        {
            if (!_dict.TryGetValue(word, out var phones))
            {
                continue;
            }

            wordSeq.Add(word);
            for (var i = 0; i < phones.Count; i++)
            {
                var ph = phones[i];
                if ((i == 0 || i == phones.Count - 1) && ph == "SP")
                {
                    continue;
                }
                phSeq.Add(ph);
                map.Add(wordSeqIdx);
            }
            if (phSeq[^1] != "SP")
            {
                phSeq.Add("SP");
                map.Add(-1);
            }
            wordSeqIdx++;
        }

        return (phSeq, wordSeq, map);
    }
}

sealed class Phoneme
{
    public double Start { get; set; }
    public double End { get; set; }
    public string Text { get; set; }

    public Phoneme(double start, double end, string text)
    {
        Start = Math.Max(0.0, start);
        End = end;
        Text = text;
        if (!(Start < End))
        {
            throw new ArgumentException($"Phoneme Invalid: text={Text} start={Start}, end={End}");
        }
    }
}

sealed class Word
{
    public double Start { get; set; }
    public double End { get; set; }
    public string Text { get; set; }
    public List<Phoneme> Phonemes { get; } = new();

    public Word(double start, double end, string text, bool initPhoneme = false)
    {
        Start = Math.Max(0.0, start);
        End = end;
        Text = text;
        if (!(Start < End))
        {
            throw new ArgumentException($"Word Invalid: text={Text} start={Start}, end={End}");
        }
        if (initPhoneme)
        {
            Phonemes.Add(new Phoneme(Start, End, Text));
        }
    }

    public double Dur => End - Start;

    public void AddPhoneme(Phoneme phoneme, List<string>? logList = null)
    {
        if (phoneme.Start == phoneme.End)
        {
            var msg = $"{phoneme.Text} phoneme长度为0，非法";
            if (logList is not null) logList.Add("WARNING: " + msg);
            return;
        }
        if (phoneme.Start >= Start && phoneme.End <= End)
        {
            Phonemes.Add(phoneme);
        }
        else
        {
            var msg = $"{phoneme.Text}: phoneme边界超出word，添加失败";
            if (logList is not null) logList.Add("WARNING: " + msg);
        }
    }

    public void AppendPhoneme(Phoneme phoneme, List<string>? logList = null)
    {
        if (phoneme.Start == phoneme.End)
        {
            var msg = $"{phoneme.Text} phoneme长度为0，非法";
            if (logList is not null) logList.Add("WARNING: " + msg);
            return;
        }

        if (Phonemes.Count == 0)
        {
            if (Math.Abs(phoneme.Start - Start) < 1e-9)
            {
                Phonemes.Add(phoneme);
                End = phoneme.End;
            }
            else
            {
                var msg = $"{phoneme.Text}: phoneme左边界超出word，添加失败";
                if (logList is not null) logList.Add("WARNING: " + msg);
            }
            return;
        }

        if (Math.Abs(phoneme.Start - Phonemes[^1].End) < 1e-9)
        {
            Phonemes.Add(phoneme);
            End = phoneme.End;
        }
        else
        {
            var msg = $"{phoneme.Text}: phoneme添加失败";
            if (logList is not null) logList.Add("WARNING: " + msg);
        }
    }

    public void MoveStart(double newStart, List<string>? logList = null)
    {
        if (0 <= newStart && newStart < Phonemes[0].End)
        {
            Start = newStart;
            Phonemes[0].Start = newStart;
        }
        else
        {
            var msg = $"{Text}: start >= first_phone_end，无法调整word边界";
            if (logList is not null) logList.Add("WARNING: " + msg);
        }
    }

    public void MoveEnd(double newEnd, List<string>? logList = null)
    {
        if (newEnd > Phonemes[^1].Start && Phonemes[^1].Start >= 0)
        {
            End = newEnd;
            Phonemes[^1].End = newEnd;
        }
        else
        {
            var msg = $"{Text}: new_end <= first_phone_start，无法调整word边界";
            if (logList is not null) logList.Add("WARNING: " + msg);
        }
    }
}

sealed class WordList : List<Word>
{
    private readonly List<string> _log = new();

    public IReadOnlyList<string> Phonemes => this.SelectMany(w => w.Phonemes.Select(p => p.Text)).ToList();

    public List<Phoneme> PhonemesList => this.SelectMany(w => w.Phonemes).ToList();

    public string Log => string.Join(Environment.NewLine, _log);

    public void ClearLog() => _log.Clear();

    private void AddLog(string message) => _log.Add(message);

    private List<Word> OverlappingWords(Word newWord)
    {
        var overlapping = new List<Word>();
        foreach (var word in this)
        {
            if (!(newWord.End <= word.Start || newWord.Start >= word.End))
            {
                overlapping.Add(word);
            }
        }
        return overlapping;
    }

    public void Append(Word word)
    {
        if (word.Phonemes.Count == 0)
        {
            AddLog("WARNING: phones为空，非法word");
            return;
        }

        if (Count == 0)
        {
            base.Add(word);
            return;
        }

        if (!OverlappingWords(word).Any())
        {
            base.Add(word);
        }
        else
        {
            AddLog("WARNING: 区间重叠，无法添加word");
        }
    }

    public void AddAP(Word newWord, double minDur = 0.1)
    {
        try
        {
            if (newWord.Phonemes.Count == 0)
            {
                AddLog($"WARNING: {newWord.Text} phonemes为空，非法word");
                return;
            }

            if (Count == 0)
            {
                Append(newWord);
                return;
            }

            var overlapping = OverlappingWords(newWord);
            if (!overlapping.Any())
            {
                Append(newWord);
                Sort((a, b) => a.Start.CompareTo(b.Start));
                return;
            }

            var apIntervals = new List<(double Start, double End)> { (newWord.Start, newWord.End) };
            foreach (var word in this)
            {
                var temp = new List<(double Start, double End)>();
                foreach (var ap in apIntervals)
                {
                    temp.AddRange(RemoveOverlappingIntervals(ap, (word.Start, word.End)));
                }
                apIntervals = temp;
            }

            apIntervals = apIntervals.Where(i => i.End - i.Start >= minDur).ToList();
            foreach (var ap in apIntervals)
            {
                try
                {
                    Append(new Word(ap.Start, ap.End, newWord.Text, initPhoneme: true));
                }
                catch (Exception e)
                {
                    AddLog("ERROR: " + e.Message);
                }
            }
            Sort((a, b) => a.Start.CompareTo(b.Start));
        }
        catch (Exception e)
        {
            AddLog("ERROR in add_AP: " + e.Message);
        }
    }

    private static List<(double Start, double End)> RemoveOverlappingIntervals((double Start, double End) raw, (double Start, double End) remove)
    {
        var (rStart, rEnd) = raw;
        var (mStart, mEnd) = remove;
        if (!(rStart < rEnd)) throw new ArgumentException("raw_interval.start must be smaller than raw_interval.end");
        if (!(mStart < mEnd)) throw new ArgumentException("remove_interval.start must be smaller than remove_interval.end");

        var overlapStart = Math.Max(rStart, mStart);
        var overlapEnd = Math.Min(rEnd, mEnd);
        if (overlapStart >= overlapEnd)
        {
            return new List<(double, double)> { raw };
        }

        var result = new List<(double, double)>();
        if (rStart < overlapStart) result.Add((rStart, overlapStart));
        if (overlapEnd < rEnd) result.Add((overlapEnd, rEnd));
        return result;
    }

    public void FillSmallGaps(double wavLength, double gapLength = 0.1)
    {
        try
        {
            if (Count == 0) return;

            if (this[0].Start < 0) this[0].Start = 0;
            if (this[0].Start > 0)
            {
                if (Math.Abs(this[0].Start) < gapLength && gapLength < this[0].Dur)
                {
                    this[0].MoveStart(0, _log);
                }
            }

            if (this[^1].End >= wavLength - gapLength)
            {
                this[^1].MoveEnd(wavLength, _log);
            }

            for (var i = 1; i < Count; i++)
            {
                if (0 < this[i].Start - this[i - 1].End && this[i].Start - this[i - 1].End <= gapLength)
                {
                    this[i - 1].MoveEnd(this[i].Start, _log);
                }
            }
        }
        catch (Exception e)
        {
            AddLog("ERROR in fill_small_gaps: " + e.Message);
        }
    }

    public void AddSP(double wavLength, string addPhone = "SP")
    {
        try
        {
            if (Count == 0) return;
            var wordsRes = new WordList();
            wordsRes._log.AddRange(_log);

            if (this[0].Start > 0)
            {
                try
                {
                    wordsRes.Append(new Word(0, this[0].Start, addPhone, initPhoneme: true));
                }
                catch (Exception e)
                {
                    AddLog("ERROR: " + e.Message);
                }
            }

            wordsRes.Append(this[0]);
            for (var i = 1; i < Count; i++)
            {
                var word = this[i];
                if (word.Start > wordsRes[^1].End)
                {
                    try
                    {
                        wordsRes.Append(new Word(wordsRes[^1].End, word.Start, addPhone, initPhoneme: true));
                    }
                    catch (Exception e)
                    {
                        AddLog("ERROR: " + e.Message);
                    }
                }
                wordsRes.Append(word);
            }

            if (this[^1].End < wavLength)
            {
                try
                {
                    wordsRes.Append(new Word(this[^1].End, wavLength, addPhone, initPhoneme: true));
                }
                catch (Exception e)
                {
                    AddLog("ERROR: " + e.Message);
                }
            }

            Clear();
            AddRange(wordsRes);
        }
        catch (Exception e)
        {
            AddLog("ERROR in add_SP: " + e.Message);
        }
    }

    public void ClearLanguagePrefix()
    {
        foreach (var word in this)
        {
            foreach (var phoneme in word.Phonemes)
            {
                var parts = phoneme.Text.Split('/');
                phoneme.Text = parts.Length > 0 ? parts[^1] : phoneme.Text;
            }
        }
    }
}

sealed class AlignmentDecoder
{
    private readonly VocabConfig _vocab;
    private readonly int _sampleRate;
    private readonly int _hopSize;
    private readonly double _frameLength;

    public AlignmentDecoder(VocabConfig vocab, int sampleRate, int hopSize)
    {
        _vocab = vocab;
        _sampleRate = sampleRate;
        _hopSize = hopSize;
        _frameLength = hopSize / (double)sampleRate;
    }

    public WordList Decode(
        float[,] phFrameLogits,
        float[] phEdgeLogits,
        double wavLength,
        List<string> phSeq,
        List<string>? wordSeq,
        List<int>? phIdxToWordIdx,
        bool ignoreSp)
    {
        var phSeqId = phSeq.Select(ph => _vocab.Vocab[ph]).ToArray();

        var phMask = new float[_vocab.VocabSize];
        for (var i = 0; i < phMask.Length; i++) phMask[i] = 1_000_000_000f;
        foreach (var id in phSeqId) phMask[id] = 0f;
        phMask[0] = 0f;

        wordSeq ??= phSeq;
        phIdxToWordIdx ??= Enumerable.Range(0, phSeq.Count).ToList();

        var numFrames = (int)((wavLength * _sampleRate + 0.5) / _hopSize);
        var tMax = Math.Min(numFrames, phFrameLogits.GetLength(1));

        var vocabSize = phFrameLogits.GetLength(0);
        var adjusted = new float[vocabSize, tMax];
        for (var v = 0; v < vocabSize; v++)
        {
            for (var t = 0; t < tMax; t++)
            {
                adjusted[v, t] = phFrameLogits[v, t] - phMask[v];
            }
        }

        var phProbLog = InferenceMath.LogSoftmaxOverFirstAxis(adjusted);
        var phEdgePred = phEdgeLogits.Take(tMax).Select(x => (float)Math.Clamp(InferenceMath.Sigmoid(x), 0.0, 1.0)).ToArray();

        var edgeDiff = new float[tMax];
        for (var t = 0; t < tMax - 1; t++)
        {
            edgeDiff[t] = phEdgePred[t + 1] - phEdgePred[t];
        }
        edgeDiff[^1] = 0;

        var edgeProb = new float[tMax];
        for (var t = 0; t < tMax; t++)
        {
            var prev = t == 0 ? 0f : phEdgePred[t - 1];
            edgeProb[t] = Math.Clamp(phEdgePred[t] + prev, 0f, 1f);
        }

        var (phIdxSeq, phTimeIntPred) = DecodePath(phSeqId, phProbLog, edgeProb);

        var phTimeFractional = new double[phTimeIntPred.Count];
        for (var i = 0; i < phTimeIntPred.Count; i++)
        {
            var v = edgeDiff[phTimeIntPred[i]] / 2f;
            phTimeFractional[i] = Math.Clamp(v, -0.5, 0.5);
        }

        var boundaries = new double[phTimeIntPred.Count + 1];
        for (var i = 0; i < phTimeIntPred.Count; i++)
        {
            boundaries[i] = _frameLength * (phTimeIntPred[i] + phTimeFractional[i]);
        }
        boundaries[^1] = _frameLength * tMax;
        for (var i = 0; i < boundaries.Length; i++)
        {
            if (boundaries[i] < 0) boundaries[i] = 0;
        }

        var word = default(Word);
        var words = new WordList();
        var wordIdxLast = -1;

        for (var i = 0; i < phIdxSeq.Count; i++)
        {
            var phIdx = phIdxSeq[i];
            var phText = phSeq[phIdx];
            if (phText == "SP" && ignoreSp) continue;

            var start = boundaries[i];
            var end = boundaries[i + 1];
            var phoneme = new Phoneme(start, end, phText);

            var wordIdx = phIdxToWordIdx[phIdx];
            if (wordIdx == wordIdxLast && word is not null)
            {
                word.AppendPhoneme(phoneme);
            }
            else
            {
                word = new Word(start, end, wordSeq[wordIdx]);
                word.AddPhoneme(phoneme);
                words.Append(word);
                wordIdxLast = wordIdx;
            }
        }

        return words;
    }

    private static (List<int> PhIdxSeq, List<int> PhTimeIntPred) DecodePath(
        int[] phSeqId,
        float[,] phProbLog,
        float[] edgeProb)
    {
        var sLen = phSeqId.Length;
        var tLen = phProbLog.GetLength(1);

        var probLog = new float[sLen, tLen];
        for (var s = 0; s < sLen; s++)
        {
            var id = phSeqId[s];
            for (var t = 0; t < tLen; t++)
            {
                probLog[s, t] = phProbLog[id, t];
            }
        }

        var dp = new float[sLen, tLen];
        var backtrack = new int[sLen, tLen];
        var currMax = new float[sLen];

        for (var s = 0; s < sLen; s++)
        {
            currMax[s] = float.NegativeInfinity;
            for (var t = 0; t < tLen; t++)
            {
                dp[s, t] = float.NegativeInfinity;
                backtrack[s, t] = -1;
            }
        }

        dp[0, 0] = probLog[0, 0];
        currMax[0] = probLog[0, 0];
        if (phSeqId[0] == 0 && sLen > 1)
        {
            dp[1, 0] = probLog[1, 0];
            currMax[1] = probLog[1, 0];
        }

        var edgeProbLog = edgeProb.Select(x => (float)Math.Log(x + 1e-6)).ToArray();
        var notEdgeProbLog = edgeProb.Select(x => (float)Math.Log(1 - x + 1e-6)).ToArray();
        var maskReset = phSeqId.Select(x => x == 0).ToArray();

        var prob1 = new float[sLen];
        var prob2 = new float[sLen];
        var prob3 = new float[sLen];
        Array.Fill(prob2, float.NegativeInfinity);
        Array.Fill(prob3, float.NegativeInfinity);

        var prob3PadLen = sLen >= 2 ? 2 : 1;
        var iValsProb3 = Enumerable.Range(prob3PadLen, Math.Max(0, sLen - prob3PadLen)).ToArray();
        var idxArr = iValsProb3.Select(i => Math.Clamp(i - prob3PadLen + 1, 0, sLen - 1)).ToArray();
        var maskCondProb3 = idxArr.Select(idx => idx >= sLen - 1 || phSeqId[idx] == 0).ToArray();

        for (var t = 1; t < tLen; t++)
        {
            var edgeLogT = edgeProbLog[t];
            var notEdgeLogT = notEdgeProbLog[t];

            for (var s = 0; s < sLen; s++)
            {
                prob1[s] = dp[s, t - 1] + probLog[s, t] + notEdgeLogT;
            }

            for (var s = 1; s < sLen; s++)
            {
                prob2[s] = dp[s - 1, t - 1] + probLog[s - 1, t] + edgeLogT + currMax[s - 1] * (tLen / (float)sLen);
            }
            prob2[0] = float.NegativeInfinity;

            for (var idx = 0; idx < iValsProb3.Length; idx++)
            {
                var s = iValsProb3[idx];
                if (!maskCondProb3[idx])
                {
                    prob3[s] = float.NegativeInfinity;
                    continue;
                }
                var srcS = s - prob3PadLen;
                prob3[s] = dp[srcS, t - 1] + probLog[srcS, t] + edgeLogT + currMax[srcS] * (tLen / (float)sLen);
            }

            for (var s = 0; s < sLen; s++)
            {
                var bestType = 0;
                var bestVal = prob1[s];
                if (prob2[s] > bestVal)
                {
                    bestVal = prob2[s];
                    bestType = 1;
                }
                if (prob3[s] > bestVal)
                {
                    bestVal = prob3[s];
                    bestType = 2;
                }

                dp[s, t] = bestVal;
                backtrack[s, t] = bestType;

                if (bestType == 0)
                {
                    currMax[s] = Math.Max(currMax[s], probLog[s, t]);
                }
                else
                {
                    currMax[s] = probLog[s, t];
                }
                if (maskReset[s]) currMax[s] = 0f;
            }

            for (var s = 1; s < sLen; s++) prob2[s] = float.NegativeInfinity;
            for (var idx = 0; idx < iValsProb3.Length; idx++) prob3[iValsProb3[idx]] = float.NegativeInfinity;
        }

        var phIdxSeq = new List<int>();
        var phTimeInt = new List<int>();

        int sEnd;
        if (sLen == 1)
        {
            sEnd = 0;
        }
        else
        {
            sEnd = (dp[sLen - 2, tLen - 1] > dp[sLen - 1, tLen - 1] && phSeqId[^1] == 0) ? sLen - 2 : sLen - 1;
        }

        var sCur = sEnd;
        for (var t = tLen - 1; t >= 0; t--)
        {
            if (backtrack[sCur, t] != 0)
            {
                phIdxSeq.Add(sCur);
                phTimeInt.Add(t);
                if (backtrack[sCur, t] == 1) sCur -= 1;
                else if (backtrack[sCur, t] == 2) sCur -= 2;
            }
        }

        phIdxSeq.Reverse();
        phTimeInt.Reverse();
        return (phIdxSeq, phTimeInt);
    }
}

sealed class NonLexicalDecoder
{
    private readonly VocabConfig _vocab;
    private readonly List<string> _nonLexicalPhs;
    private readonly int _sampleRate;
    private readonly int _hopSize;
    private readonly double _frameLength;

    public NonLexicalDecoder(VocabConfig vocab, List<string> classNames, int sampleRate, int hopSize)
    {
        _vocab = vocab;
        _nonLexicalPhs = classNames;
        _sampleRate = sampleRate;
        _hopSize = hopSize;
        _frameLength = hopSize / (double)sampleRate;
    }

    public List<List<Word>> Decode(float[,] cvntLogits, double? wavLength, List<string> nonLexicalPhonemes)
    {
        var tLen = cvntLogits.GetLength(1);
        if (wavLength is not null)
        {
            var numFrames = (int)((wavLength.Value * _sampleRate + 0.5) / _hopSize);
            tLen = Math.Min(tLen, numFrames);
        }

        var logitsTrim = new float[cvntLogits.GetLength(0), tLen];
        for (var c = 0; c < cvntLogits.GetLength(0); c++)
        {
            for (var t = 0; t < tLen; t++)
            {
                logitsTrim[c, t] = cvntLogits[c, t];
            }
        }

        var probs = InferenceMath.SoftmaxOverFirstAxis(logitsTrim);
        var res = new List<List<Word>>();
        foreach (var ph in nonLexicalPhonemes)
        {
            var i = _nonLexicalPhs.IndexOf(ph);
            if (i < 0) continue;
            var p = new float[tLen];
            for (var t = 0; t < tLen; t++) p[t] = probs[i, t];
            res.Add(NonLexicalWords(p, tag: ph));
        }
        return res;
    }

    private List<Word> NonLexicalWords(float[] prob, float threshold = 0.5f, int maxGap = 5, int mixFrames = 10, string tag = "")
    {
        var words = new List<Word>();
        int? start = null;
        var gapCount = 0;

        for (var i = 0; i < prob.Length; i++)
        {
            if (prob[i] >= threshold)
            {
                start ??= i;
                gapCount = 0;
            }
            else if (start is not null)
            {
                if (gapCount < maxGap)
                {
                    gapCount += 1;
                }
                else
                {
                    var end = i - gapCount - 1;
                    if (end > start.Value && (end - start.Value) >= mixFrames)
                    {
                        var w = new Word(start.Value * _frameLength, end * _frameLength, tag);
                        w.AddPhoneme(new Phoneme(start.Value * _frameLength, end * _frameLength, tag));
                        words.Add(w);
                    }
                    start = null;
                    gapCount = 0;
                }
            }
        }

        if (start is not null && (prob.Length - start.Value) >= mixFrames)
        {
            var w = new Word(start.Value * _frameLength, (prob.Length - 1) * _frameLength, tag);
            w.AddPhoneme(new Phoneme(start.Value * _frameLength, (prob.Length - 1) * _frameLength, tag));
            words.Add(w);
        }
        return words;
    }
}

static class InferenceMath
{
    public static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));

    public static float[,] SoftmaxOverFirstAxis(float[,] x)
    {
        var rows = x.GetLength(0);
        var cols = x.GetLength(1);
        var res = new float[rows, cols];

        for (var c = 0; c < cols; c++)
        {
            var max = float.NegativeInfinity;
            for (var r = 0; r < rows; r++)
            {
                if (x[r, c] > max) max = x[r, c];
            }
            var sum = 0.0;
            for (var r = 0; r < rows; r++)
            {
                var ex = Math.Exp(x[r, c] - max);
                res[r, c] = (float)ex;
                sum += ex;
            }
            var inv = (float)(1.0 / sum);
            for (var r = 0; r < rows; r++)
            {
                res[r, c] *= inv;
            }
        }
        return res;
    }

    public static float[,] LogSoftmaxOverFirstAxis(float[,] x)
    {
        var rows = x.GetLength(0);
        var cols = x.GetLength(1);
        var res = new float[rows, cols];

        for (var c = 0; c < cols; c++)
        {
            var max = float.NegativeInfinity;
            for (var r = 0; r < rows; r++)
            {
                if (x[r, c] > max) max = x[r, c];
            }
            var sum = 0.0;
            for (var r = 0; r < rows; r++)
            {
                sum += Math.Exp(x[r, c] - max);
            }
            var logSum = Math.Log(sum);
            for (var r = 0; r < rows; r++)
            {
                res[r, c] = (float)(x[r, c] - max - logSum);
            }
        }
        return res;
    }

    public static List<int> FindAllDuplicatePhonemes(List<IReadOnlyList<string>> phList)
    {
        if (phList.Count == 1) return new List<int> { 0 };

        var indexDict = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var idx = 0; idx < phList.Count; idx++)
        {
            var key = string.Join('\u0001', phList[idx]);
            if (!indexDict.TryGetValue(key, out var list))
            {
                list = new List<int>();
                indexDict[key] = list;
            }
            list.Add(idx);
        }

        var duplicates = indexDict.Where(kvp => kvp.Value.Count > 1).ToList();
        if (duplicates.Count == 0)
        {
            throw new InvalidOperationException("No duplicate groups");
        }

        duplicates.Sort((a, b) =>
        {
            var cmp = b.Value.Count.CompareTo(a.Value.Count);
            if (cmp != 0) return cmp;
            return b.Key.Length.CompareTo(a.Key.Length);
        });

        return duplicates[0].Value;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return double.NaN;
        if (sorted.Length % 2 == 1) return sorted[sorted.Length / 2];
        return 0.5 * (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]);
    }

    private static double MedianAbsDeviation(List<double> values)
    {
        if (values.Count == 0) return double.NaN;
        var med = Median(values);
        var absDev = values.Select(v => Math.Abs(v - med)).ToList();
        return Median(absDev);
    }

    public static List<double> RemoveOutliersPerPosition(List<List<double>> dataSeries, double threshold = 1.5)
    {
        var processed = new List<double>(dataSeries.Count);
        foreach (var positionValues in dataSeries)
        {
            if (positionValues.Count == 0)
            {
                processed.Add(0.0);
                continue;
            }

            var med = Median(positionValues);
            var mad = MedianAbsDeviation(positionValues);
            if (mad == 0)
            {
                processed.Add(med);
                continue;
            }

            var retained = new List<double>();
            for (var i = 0; i < positionValues.Count; i++)
            {
                var z = Math.Abs((positionValues[i] - med) / (mad * 1.4826));
                if (z <= threshold)
                {
                    retained.Add(positionValues[i]);
                }
            }

            processed.Add(retained.Count > 0 ? retained.Average() : med);
        }
        return processed;
    }
}

static class TensorUtils
{
    public static float[,] ExtractPhFrameLogits(Tensor<float> tensor, int paddedFrames)
    {
        if (tensor.Dimensions.Length != 3) throw new ArgumentException("ph_frame_logits must be 3D");
        var b = tensor.Dimensions[0];
        var c = tensor.Dimensions[1];
        var t = tensor.Dimensions[2];
        if (b != 1) throw new ArgumentException("Only batch=1 is supported");

        var tOut = Math.Max(0, t - paddedFrames);
        var res = new float[c, tOut];
        var span = tensor.ToArray();

        for (var ci = 0; ci < c; ci++)
        {
            for (var ti = paddedFrames; ti < t; ti++)
            {
                var srcIdx = (0 * c * t) + (ci * t) + ti;
                res[ci, ti - paddedFrames] = span[srcIdx];
            }
        }
        return res;
    }

    public static float[] ExtractPhEdgeLogits(Tensor<float> tensor, int paddedFrames)
    {
        if (tensor.Dimensions.Length != 2) throw new ArgumentException("ph_edge_logits must be 2D");
        var b = tensor.Dimensions[0];
        var t = tensor.Dimensions[1];
        if (b != 1) throw new ArgumentException("Only batch=1 is supported");

        var tOut = Math.Max(0, t - paddedFrames);
        var res = new float[tOut];
        var span = tensor.ToArray();
        for (var ti = paddedFrames; ti < t; ti++)
        {
            var srcIdx = (0 * t) + ti;
            res[ti - paddedFrames] = span[srcIdx];
        }
        return res;
    }

    public static float[,] ExtractCvntLogits(Tensor<float> tensor, int paddedFrames)
    {
        if (tensor.Dimensions.Length != 3) throw new ArgumentException("cvnt_logits must be 3D");
        var b = tensor.Dimensions[0];
        var n = tensor.Dimensions[1];
        var t = tensor.Dimensions[2];
        if (b != 1) throw new ArgumentException("Only batch=1 is supported");

        var tOut = Math.Max(0, t - paddedFrames);
        var res = new float[n, tOut];
        var span = tensor.ToArray();

        for (var ni = 0; ni < n; ni++)
        {
            for (var ti = paddedFrames; ti < t; ti++)
            {
                var srcIdx = (0 * n * t) + (ni * t) + ti;
                res[ni, ti - paddedFrames] = span[srcIdx];
            }
        }
        return res;
    }
}

static class TextGridWriter
{
    public static void Write(string path, double wavLength, WordList words)
    {
        var sb = new StringBuilder();
        var c = CultureInfo.InvariantCulture;

        sb.AppendLine("File type = \"ooTextFile\"");
        sb.AppendLine("Object class = \"TextGrid\"");
        sb.AppendLine();
        sb.AppendLine($"xmin = {0.ToString(c)}");
        sb.AppendLine($"xmax = {wavLength.ToString("0.########", c)}");
        sb.AppendLine("tiers? <exists>");
        sb.AppendLine("size = 2");
        sb.AppendLine("item []:");

        var wordIntervals = words.Select(w => (w.Start, w.End, w.Text, w.Phonemes)).ToList();
        var phoneIntervals = words.SelectMany(w => w.Phonemes.Select(p => (p.Start, p.End, p.Text))).ToList();

        AppendIntervalTier(sb, 1, "words", wavLength, wordIntervals.Select(w => (w.Start, w.End, w.Text)).ToList());
        AppendIntervalTier(sb, 2, "phones", wavLength, phoneIntervals);

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void AppendIntervalTier(StringBuilder sb, int index, string name, double wavLength, List<(double Start, double End, string Text)> intervals)
    {
        var c = CultureInfo.InvariantCulture;
        sb.AppendLine($"    item [{index}]:");
        sb.AppendLine("        class = \"IntervalTier\"");
        sb.AppendLine($"        name = \"{Escape(name)}\"");
        sb.AppendLine($"        xmin = {0.ToString(c)}");
        sb.AppendLine($"        xmax = {wavLength.ToString("0.########", c)}");
        sb.AppendLine($"        intervals: size = {intervals.Count}");

        for (var i = 0; i < intervals.Count; i++)
        {
            var (start, end, text) = intervals[i];
            sb.AppendLine($"        intervals [{i + 1}]:");
            sb.AppendLine($"            xmin = {start.ToString("0.########", c)}");
            sb.AppendLine($"            xmax = {end.ToString("0.########", c)}");
            sb.AppendLine($"            text = \"{Escape(text)}\"");
        }
    }

    private static string Escape(string text) => text.Replace("\"", "\"\"");
}
