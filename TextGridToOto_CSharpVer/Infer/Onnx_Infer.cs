using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TextGridToOto_CSharpVer.Infer
{
    #region 配置模型

    public class ModelConfig
    {
        [JsonPropertyName("hubert_config")]
        public HubertConfig? HubertConfig { get; set; }

        [JsonPropertyName("mel_spec_config")]
        public MelSpecConfig? MelSpecConfig { get; set; }
    }

    public class HubertConfig
    {
        [JsonPropertyName("encoder")] public string? Encoder { get; set; }
        [JsonPropertyName("model_path")] public string? ModelPath { get; set; }
        [JsonPropertyName("sample_rate")] public int SampleRate { get; set; }
        [JsonPropertyName("hop_size")] public int HopSize { get; set; }
        [JsonPropertyName("channel")] public int Channel { get; set; }
    }

    public class MelSpecConfig
    {
        [JsonPropertyName("n_mels")] public int NMels { get; set; }
        [JsonPropertyName("sample_rate")] public int SampleRate { get; set; }
        [JsonPropertyName("window_size")] public int WindowSize { get; set; }
        [JsonPropertyName("hop_size")] public int HopSize { get; set; }
        [JsonPropertyName("n_fft")] public int NFft { get; set; }
        [JsonPropertyName("f_min")] public int FMin { get; set; }
        [JsonPropertyName("f_max")] public int FMax { get; set; }
        [JsonPropertyName("clamp")] public double Clamp { get; set; }
    }

    public class VocabConfig
    {
        [JsonPropertyName("non_lexical_phonemes")] public List<string>? NonLexicalPhonemes { get; set; }
        [JsonPropertyName("non_lexical_phonemes_dict")] public Dictionary<string, int>? NonLexicalPhonemesDict { get; set; }
        [JsonPropertyName("dictionaries")] public Dictionary<string, string>? Dictionaries { get; set; }
        [JsonPropertyName("language_prefix")] public bool LanguagePrefix { get; set; }
        [JsonPropertyName("vocab")] public Dictionary<string, int>? Vocab { get; set; }
        [JsonPropertyName("vocab_size")] public int VocabSize { get; set; }
    }

    #endregion

    #region 数据模型

    public class Phoneme
    {
        public double Start { get; set; }
        public double End { get; set; }
        public string Text { get; set; }

        public Phoneme(double start, double end, string text)
        {
            Start = Math.Max(0.0, start);
            End = end;
            Text = text;
            if (Start >= End) throw new ArgumentException($"Phoneme Invalid: {text} start={start}, end={end}");
        }
    }

    public class Word
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
            if (Start >= End) throw new ArgumentException($"Word Invalid: {text} start={start}, end={end}");
            if (initPhoneme) Phonemes.Add(new Phoneme(Start, End, Text));
        }

        public void AddPhoneme(Phoneme ph) { if (ph.Start >= Start && ph.End <= End) Phonemes.Add(ph); }
        public void AppendPhoneme(Phoneme ph) { Phonemes.Add(ph); End = ph.End; }
        public void MoveStart(double s) { Start = s; if (Phonemes.Count > 0) Phonemes[0].Start = s; }
        public void MoveEnd(double e) { End = e; if (Phonemes.Count > 0) Phonemes[^1].End = e; }
    }

    public class WordList : List<Word>
    {
        public new void Add(Word word) { if (word.Phonemes.Count > 0) base.Add(word); }

        public void AddAP(Word newWord, double minDur = 0.1)
        {
            if (newWord.Phonemes.Count == 0) return;
            if (Count == 0) { Add(newWord); return; }

            var intervals = new List<(double s, double e)> { (newWord.Start, newWord.End) };
            foreach (var w in this)
            {
                var temp = new List<(double, double)>();
                foreach (var (s, e) in intervals)
                {
                    double os = Math.Max(s, w.Start), oe = Math.Min(e, w.End);
                    if (os >= oe) { temp.Add((s, e)); }
                    else { if (s < os) temp.Add((s, os)); if (oe < e) temp.Add((oe, e)); }
                }
                intervals = temp;
            }
            foreach (var (s, e) in intervals.Where(i => i.e - i.s >= minDur))
            {
                try { Add(new Word(s, e, newWord.Text, true)); } catch { }
            }
            Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        public void FillSmallGaps(double wavLen, double gap = 0.1)
        {
            if (Count == 0) return;
            if (this[0].Start > 0 && this[0].Start < gap) this[0].MoveStart(0);
            if (this[^1].End >= wavLen - gap) this[^1].MoveEnd(wavLen);
            for (int i = 1; i < Count; i++)
                if (this[i].Start - this[i - 1].End > 0 && this[i].Start - this[i - 1].End <= gap)
                    this[i - 1].MoveEnd(this[i].Start);
        }

        public void AddSP(double wavLen, string sp = "SP")
        {
            var result = new WordList();
            if (this[0].Start > 0) try { result.Add(new Word(0, this[0].Start, sp, true)); } catch { }
            result.Add(this[0]);
            for (int i = 1; i < Count; i++)
            {
                if (this[i].Start > result[^1].End)
                    try { result.Add(new Word(result[^1].End, this[i].Start, sp, true)); } catch { }
                result.Add(this[i]);
            }
            if (this[^1].End < wavLen) try { result.Add(new Word(this[^1].End, wavLen, sp, true)); } catch { }
            Clear(); AddRange(result);
        }

        public void ClearLanguagePrefix()
        {
            foreach (var w in this) foreach (var ph in w.Phonemes) ph.Text = ph.Text.Split('/')[^1];
        }
    }

    #endregion

    #region G2P (Grapheme-to-Phoneme)

    public interface IG2P
    {
        (List<string> phSeq, List<string> wordSeq, List<int> phIdxToWordIdx) Convert(string text);
    }

    public class DictionaryG2P : IG2P
    {
        private readonly string? _lang;
        private readonly Dictionary<string, List<string>> _dict;

        public DictionaryG2P(string? lang, string path)
        {
            _lang = lang;
            _dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && !_dict.ContainsKey(parts[0].Trim()))
                    _dict[parts[0].Trim()] = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }

        public (List<string>, List<string>, List<int>) Convert(string text)
        {
            var phSeq = new List<string>(); var wordSeq = new List<string>(); var idx = new List<int>();
            int wIdx = 0;
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!_dict.TryGetValue(word, out var phs)) throw new Exception($"Word '{word}' not in dictionary");
                wordSeq.Add(word);
                foreach (var ph in phs) { phSeq.Add(_lang != null ? $"{_lang}/{ph}" : ph); idx.Add(wIdx); }
                wIdx++;
            }
            return (phSeq, wordSeq, idx);
        }
    }

    public class PhonemeG2P : IG2P
    {
        private readonly string? _lang;
        public PhonemeG2P(string? lang) => _lang = lang;

        public (List<string>, List<string>, List<int>) Convert(string text)
        {
            var phs = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (phs.Select(p => _lang != null ? $"{_lang}/{p}" : p).ToList(), phs.ToList(),
                    Enumerable.Range(0, phs.Length).ToList());
        }
    }

    #endregion

    #region 解码器

    /// <summary>
    /// 对齐解码器 - 核心推理逻辑
    /// </summary>
    public class AlignmentDecoder
    {
        private readonly VocabConfig _vocab;
        private readonly double _frameLen;
        private readonly int _sampleRate;
        private readonly int _hopSize;

        public AlignmentDecoder(VocabConfig vocab, int sr, int hop)
        {
            _vocab = vocab;
            _sampleRate = sr;
            _hopSize = hop;
            _frameLen = (double)hop / sr;
        }

        public (WordList words, double conf) Decode(float[,] phLogits, float[] edgeLogits, double wavLen,
            List<string> phSeq, List<string> wordSeq, List<int> phToWord, bool ignoreSp = true)
        {
            int V = phLogits.GetLength(0), T = phLogits.GetLength(1);
            var phSeqId = phSeq.Select(p => _vocab.Vocab!.GetValueOrDefault(p, 0)).ToArray();
            int S = phSeqId.Length;

            // 创建掩码
            var mask = Enumerable.Repeat(1e9f, V).ToArray();
            foreach (var id in phSeqId) mask[id] = 0;
            mask[0] = 0;

            int numFrames = Math.Min((int)((wavLen * _sampleRate + 0.5) / _hopSize), T);

            // Softmax 计算概率
            var probLog = new float[S, numFrames];
            for (int t = 0; t < numFrames; t++)
            {
                float max = float.MinValue;
                for (int v = 0; v < V; v++) max = Math.Max(max, phLogits[v, t] - mask[v]);
                float sum = 0;
                for (int v = 0; v < V; v++) sum += MathF.Exp(phLogits[v, t] - mask[v] - max);
                for (int s = 0; s < S; s++)
                    probLog[s, t] = phLogits[phSeqId[s], t] - mask[phSeqId[s]] - max - MathF.Log(sum);
            }

            // 边界概率计算
            var edgeProb = new float[numFrames];
            for (int t = 0; t < numFrames; t++)
                edgeProb[t] = Math.Clamp(1f / (1f + MathF.Exp(-edgeLogits[t])) +
                    (t > 0 ? 1f / (1f + MathF.Exp(-edgeLogits[t - 1])) : 0), 0, 1);

            // 动态规划对齐
            var dp = new float[S, numFrames];
            var bt = new int[S, numFrames];
            for (int s = 0; s < S; s++)
                for (int t = 0; t < numFrames; t++)
                {
                    dp[s, t] = float.NegativeInfinity;
                    bt[s, t] = -1;
                }

            dp[0, 0] = probLog[0, 0];
            if (phSeqId[0] == 0 && S > 1) dp[1, 0] = probLog[1, 0];

            for (int t = 1; t < numFrames; t++)
            {
                float eLog = MathF.Log(edgeProb[t] + 1e-6f);
                float neLog = MathF.Log(1 - edgeProb[t] + 1e-6f);
                for (int s = 0; s < S; s++)
                {
                    float p1 = dp[s, t - 1] + probLog[s, t] + neLog;
                    float p2 = s > 0 ? dp[s - 1, t - 1] + probLog[s, t] + eLog : float.NegativeInfinity;
                    if (p1 >= p2) { dp[s, t] = p1; bt[s, t] = 0; }
                    else { dp[s, t] = p2; bt[s, t] = 1; }
                }
            }

            // 回溯路径
            var phIdx = new List<int>();
            var phTime = new List<int>();
            int sEnd = S == 1 ? 0 : (dp[S - 2, numFrames - 1] > dp[S - 1, numFrames - 1] && phSeqId[S - 1] == 0) ? S - 2 : S - 1;
            int sc = sEnd;
            for (int t = numFrames - 1; t >= 0; t--)
            {
                if (bt[sc, t] != 0)
                {
                    phIdx.Add(sc);
                    phTime.Add(t);
                    if (bt[sc, t] == 1) sc--;
                }
            }
            phIdx.Reverse();
            phTime.Reverse();

            // 构建结果
            var words = new WordList();
            Word? cur = null;
            int lastWIdx = -1;
            var times = phTime.Select(t => _frameLen * t).Concat(new[] { _frameLen * numFrames }).ToArray();

            for (int i = 0; i < phIdx.Count; i++)
            {
                int pi = phIdx[i];
                if (phSeq[pi] == "SP" && ignoreSp) continue;
                var ph = new Phoneme(times[i], times[i + 1], phSeq[pi]);
                int wi = phToWord[pi];
                if (wi == lastWIdx && cur != null)
                    cur.AppendPhoneme(ph);
                else
                {
                    cur = new Word(times[i], times[i + 1], wordSeq[wi]);
                    cur.AddPhoneme(ph);
                    words.Add(cur);
                    lastWIdx = wi;
                }
            }
            return (words, 1.0);
        }
    }

    #endregion

    #region ONNX 推理主类

    /// <summary>
    /// ONNX 推理核心类
    /// </summary>
    internal class Onnx_Infer : IDisposable
    {
        private readonly InferenceSession? _session;
        private readonly VocabConfig? _vocab;
        private readonly AlignmentDecoder? _decoder;
        private readonly int _sampleRate;
        private readonly int _hopSize;

        public Onnx_Infer(string modelPath, VocabConfig vocabConfig, int sampleRate = 44100, int hopSize = 512)
        {
            _sampleRate = sampleRate;
            _hopSize = hopSize;
            _vocab = vocabConfig;

            // 初始化 ONNX 运行时
            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            _session = new InferenceSession(modelPath, options);

            // 初始化解码器
            _decoder = new AlignmentDecoder(_vocab, _sampleRate, _hopSize);
        }

        /// <summary>
        /// 执行推理
        /// </summary>
        public (WordList words, double confidence) Infer(float[] audioData, double wavLen,
            List<string> phSeq, List<string> wordSeq, List<int> phToWord)
        {
            if (_session == null || _decoder == null)
                throw new InvalidOperationException("推理器未正确初始化");

            // 准备输入张量
            var inputTensor = new DenseTensor<float>(audioData, new[] { 1, audioData.Length });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("audio", inputTensor)
            };

            // 执行推理
            using var results = _session.Run(inputs);

            // 提取输出
            var phLogitsOutput = results.First(r => r.Name == "ph_logits").AsTensor<float>();
            var edgeLogitsOutput = results.First(r => r.Name == "edge_logits").AsTensor<float>();

            // 转换为数组格式
            var phLogits = ConvertTo2DArray(phLogitsOutput);
            var edgeLogits = ConvertTo1DArray(edgeLogitsOutput);

            // 解码
            return _decoder.Decode(phLogits, edgeLogits, wavLen, phSeq, wordSeq, phToWord);
        }

        private float[,] ConvertTo2DArray(Tensor<float> tensor)
        {
            var dims = tensor.Dimensions.ToArray();
            var result = new float[dims[0], dims[1]];
            for (int i = 0; i < dims[0]; i++)
                for (int j = 0; j < dims[1]; j++)
                    result[i, j] = tensor[i, j];
            return result;
        }

        private float[] ConvertTo1DArray(Tensor<float> tensor)
        {
            return tensor.ToArray();
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }

    #endregion
}
