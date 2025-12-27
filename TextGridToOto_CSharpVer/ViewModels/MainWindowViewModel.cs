using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextGrid2Oto;
using TextGridToOto_CSharpVer.Infer;

namespace TextGridToOto_CSharpVer.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            LoadAvailableLanguages();
        }

        [ObservableProperty]
        private string _wavFolderPath = string.Empty;
        
        [ObservableProperty]
        private string _presampFilePath = string.Empty;
        
        [ObservableProperty]
        private string _configFilePath = string.Empty;

        [ObservableProperty]
        private int _selectedTypeIndex = 0;  // CV=0, CVVC=1, VCV=2

        [ObservableProperty]
        private int _selectedMultipleScalesIndex = 0;  // Close=0, Open=1

        [ObservableProperty]
        private int _selectedLanguageIndex = 0;  // 选中的语言索引

        [ObservableProperty]
        private List<string> _availableLanguages = new();  // 可用的语言列表

        [ObservableProperty]
        private string _suffix = string.Empty;

        [ObservableProperty]
        private double _progressValue = 0;

        [ObservableProperty]
        private string _progressText = "就绪";

        [ObservableProperty]
        private bool _isProgressIndeterminate = false;

        [RelayCommand]
        private void WavFolderSelected(string? folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath))
            {
                WavFolderPath = folderPath;
                Log($"选择的 Wav 文件夹: {folderPath}");
            }
        }
        
        [RelayCommand]
        private void PresampFileSelected(string? filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                PresampFilePath = filePath;
                Log($"选择的 Presamp 文件: {filePath}");
            }
        }
        
        [RelayCommand]
        private void ConfigFileSelected(string? filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                ConfigFilePath = filePath;
                Log($"选择的 Config 文件: {filePath}");
            }
        }

        /// <summary>
        /// 运行 LAB 生成
        /// </summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task RunLab()
        {
            try
            {
                // 验证路径
                if (string.IsNullOrEmpty(WavFolderPath))
                {
                    LogError("请先选择 Wav 文件夹");
                    return;
                }

                if (!Directory.Exists(WavFolderPath))
                {
                    LogError($"Wav 文件夹不存在: {WavFolderPath}");
                    return;
                }

                SetIndeterminateProgress("初始化...");
                Log("开始生成 LAB 文件...");

                // 在 Wav 文件夹下创建 lab 子文件夹
                string outputFolder = Path.Combine(WavFolderPath, "lab");

                // 使用带进度的方法处理
                int count = await System.Threading.Tasks.Task.Run(() =>
                {
                    return Generate_lab.ProcessWavFilesWithProgress(
                        WavFolderPath,
                        outputFolder,
                        (current, total, fileName) =>
                        {
                            PostToUi(() =>
                            {
                                UpdateProgress(current, total, "LAB");
                                Log($"[{current}/{total}] 生成: {fileName}");
                            });
                        });
                });

                ResetProgress();
                LogSuccess($"LAB 文件生成完成！共处理 {count} 个文件");
                Log($"输出路径: {outputFolder}");
            }
            catch (Exception ex)
            {
                ResetProgress();
                LogError($"生成 LAB 文件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 运行 FA (Forced Alignment) 强制对齐
        /// </summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task RunFA()
        {
            try
            {
                // 验证路径
                if (string.IsNullOrEmpty(WavFolderPath))
                {
                    LogError("请先选择 Wav 文件夹");
                    return;
                }

                if (!Directory.Exists(WavFolderPath))
                {
                    LogError($"Wav 文件夹不存在: {WavFolderPath}");
                    return;
                }

                // 检查 lab 文件夹是否存在
                string labFolder = Path.Combine(WavFolderPath, "lab");
                if (!Directory.Exists(labFolder))
                {
                    LogError("lab 文件夹不存在，请先运行 Run LAB 生成 lab 文件");
                    return;
                }

                Log("开始执行 FA (Forced Alignment)...");
                SetIndeterminateProgress("检查配置...");
                Log("正在检查 FA 模型文件夹...");
                
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string faModelsPath = Path.Combine(appDir, "FA_Model");
                if (!Directory.Exists(faModelsPath))
                {
                    faModelsPath = Path.Combine(appDir, "FA_Models");
                }
                
                if (!Directory.Exists(faModelsPath))
                {
                    ResetProgress();
                    LogError($"FA 模型文件夹不存在: {faModelsPath}");
                    LogWarning("请创建 FA_Model 或 FA_Models 文件夹并放入以下文件:");
                    LogWarning("  - ONNX 模型文件 (如: fa_model.onnx)");
                    LogWarning("  - vocab.json (词汇表配置)");
                    LogWarning("  - 字典文件 (如: zh.txt, ja.txt 等)");
                    return;
                }

                var labFiles = Directory.GetFiles(labFolder, "*.lab", SearchOption.TopDirectoryOnly);
                Log($"找到 {labFiles.Length} 个 lab 文件");
                if (labFiles.Length == 0)
                {
                    ResetProgress();
                    LogWarning("lab 文件夹中没有找到任何 .lab 文件");
                    return;
                }

                // 获取选中的语言
                string selectedLanguage = "zh"; // 默认值
                if (AvailableLanguages.Count > 0 && SelectedLanguageIndex >= 0 && SelectedLanguageIndex < AvailableLanguages.Count)
                {
                    selectedLanguage = AvailableLanguages[SelectedLanguageIndex];
                }
                Log($"使用语言: {selectedLanguage}");

                SetIndeterminateProgress("推理中...");
                await System.Threading.Tasks.Task.Run(() =>
                {
                    FaAutoAnnotator.RunFolder(
                        wavFolder: WavFolderPath,
                        labFolder: labFolder,
                        modelFolder: faModelsPath,
                        language: selectedLanguage,
                        g2p: "dictionary",
                        dictionaryPath: null,
                        nonLexicalPhonemes: "AP,EP",
                        padTimes: 1,
                        padLength: 5.0,
                        progressCallback: (current, total, fileName) =>
                        {
                            PostToUi(() =>
                            {
                                UpdateProgress(current, total, "FA");
                                Log($"[{current}/{total}] 推理: {fileName}");
                            });
                        });
                });

                ResetProgress();
                LogSuccess("FA 推理完成！");
                Log($"输出路径: {Path.Combine(WavFolderPath, "TextGrid")}");
            }
            catch (Exception ex)
            {
                ResetProgress();
                LogError($"执行 FA 时出错: {ex.Message}");
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task RunConversion()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                if (string.IsNullOrEmpty(WavFolderPath))
                {
                    LogError("请先选择 Wav 文件夹");
                    return;
                }

                if (!Directory.Exists(WavFolderPath))
                {
                    LogError($"Wav 文件夹不存在: {WavFolderPath}");
                    return;
                }

                if (string.IsNullOrEmpty(PresampFilePath))
                {
                    LogError("请先选择 Presamp 文件");
                    return;
                }

                if (!File.Exists(PresampFilePath))
                {
                    LogError($"Presamp 文件不存在: {PresampFilePath}");
                    return;
                }

                if (string.IsNullOrEmpty(ConfigFilePath))
                {
                    LogError("请先选择 Config 文件");
                    return;
                }

                if (!File.Exists(ConfigFilePath))
                {
                    LogError($"Config 文件不存在: {ConfigFilePath}");
                    return;
                }

                var mode = SelectedTypeIndex switch
                {
                    0 => OtoMode.Cvv,
                    1 => OtoMode.Cvvc,
                    2 => OtoMode.Vcv,
                    _ => OtoMode.Cvvc
                };

                var conversionConfig = LoadConversionConfig(ConfigFilePath, PresampFilePath, mode);

                var isMultiScale = SelectedMultipleScalesIndex == 1;

                if (!isMultiScale)
                {
                    var pitch = string.IsNullOrWhiteSpace(Suffix) ? conversionConfig.Pitch : Suffix.Trim();

                    SetIndeterminateProgress("转换中...");
                    Log("开始执行 TextGrid -> oto 转换...");
                    Log($"模式: {GetTypeName(SelectedTypeIndex)}");
                    Log($"多音阶: 关闭");
                    Log($"Suffix: {pitch}");

                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        ConvertOneFolder(WavFolderPath, conversionConfig, pitch);
                    });

                    ResetProgress();
                    LogSuccess("转换完成！");
                    Log($"输出路径: {Path.Combine(WavFolderPath, "oto.ini")}");
                    return;
                }

                var allSubDirs = Directory.GetDirectories(WavFolderPath, "*", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var subDirs = allSubDirs
                    .Where(p =>
                    {
                        var name = Path.GetFileName(p);
                        if (string.Equals(name, "lab", StringComparison.OrdinalIgnoreCase)) return false;
                        if (string.Equals(name, "TextGrid", StringComparison.OrdinalIgnoreCase)) return false;
                        return true;
                    })
                    .ToList();

                if (subDirs.Count == 0)
                {
                    LogError("多音阶模式已开启，但未找到任何子文件夹");
                    return;
                }

                var marks = ParseSuffixMarks(Suffix);

                Log("开始执行 TextGrid -> oto 转换...");
                Log($"模式: {GetTypeName(SelectedTypeIndex)}");
                Log($"多音阶: 开启（{subDirs.Count} 个文件夹）");
                Log($"标识: {(marks.Count == 0 ? "(未设置)" : string.Join(",", marks))}");

                ProgressValue = 0;
                IsProgressIndeterminate = false;
                ProgressText = $"Conversion (0/{subDirs.Count})";

                await System.Threading.Tasks.Task.Run(() =>
                {
                    for (var i = 0; i < subDirs.Count; i++)
                    {
                        var dir = subDirs[i];
                        var folderName = Path.GetFileName(dir);
                        var pitch = marks.Count switch
                        {
                            0 => folderName,
                            1 => marks[0],
                            _ => i < marks.Count ? marks[i] : folderName
                        };

                        try
                        {
                            ConvertOneFolder(dir, conversionConfig, pitch);
                            var current = i + 1;
                            PostToUi(() =>
                            {
                                UpdateProgress(current, subDirs.Count, "Conversion");
                                Log($"[{current}/{subDirs.Count}] 完成: {folderName} -> Suffix={pitch}");
                            });
                        }
                        catch (Exception ex)
                        {
                            PostToUi(() =>
                            {
                                LogError($"转换失败: {folderName} - {ex.Message}");
                            });
                        }
                    }
                });

                ResetProgress();
                LogSuccess("多音阶转换完成！");
            }
            catch (Exception ex)
            {
                ResetProgress();
                LogError($"执行 Conversion 时出错: {ex.Message}");
            }
        }

        private sealed record ConversionConfig(
            string DsDictPath,
            string PresampPath,
            string IgnoreCsv,
            OtoMode Mode,
            double[] CvSum,
            double[] VcSum,
            double[] VvSum,
            double[] CvOffset,
            double[] VcOffset,
            int CvRepeat,
            int VcRepeat,
            string Pitch,
            string Cover);

        private ConversionConfig LoadConversionConfig(string configPath, string presampPath, OtoMode mode)
        {
            var raw = File.ReadAllText(configPath, Encoding.UTF8);
            var hasEquals = raw.Contains('=', StringComparison.Ordinal);

            string? dsDictPath = null;
            string? ignore = null;
            double[]? cvSum = null;
            double[]? vcSum = null;
            double[]? vvSum = null;
            double[]? cvOffset = null;
            double[]? vcOffset = null;
            int? cvRepeat = null;
            int? vcRepeat = null;
            string? pitch = null;
            string? cover = null;

            if (hasEquals)
            {
                var cfg = IniLikeConfig.Load(configPath);
                dsDictPath = cfg.Get("ds_dict");
                ignore = cfg.Get("ignore");
                cvSum = cfg.GetCsvDoubles("cv_sum");
                vcSum = cfg.GetCsvDoubles("vc_sum");
                vvSum = cfg.GetCsvDoubles("vv_sum");
                cvOffset = cfg.GetCsvDoubles("cv_offset");
                vcOffset = cfg.GetCsvDoubles("vc_offset");
                cvRepeat = cfg.GetInt("CV_repeat");
                vcRepeat = cfg.GetInt("VC_repeat");
                pitch = cfg.Get("pitch");
                cover = cfg.Get("cover");
            }
            else
            {
                var dict = ParseYamlLike(configPath);
                dsDictPath = GetFirst(dict, "ds_dict", "dsDict", "dsDictPath");
                var sofa2utau = GetFirst(dict, "sofa2utau");
                if (string.IsNullOrWhiteSpace(dsDictPath) && !string.IsNullOrWhiteSpace(sofa2utau))
                {
                    dsDictPath = Path.IsPathRooted(sofa2utau) ? sofa2utau : Path.Combine(Path.GetDirectoryName(configPath) ?? "", sofa2utau);
                }

                ignore = GetFirst(dict, "ignore", "Ignore_phonemes", "ignore_phonemes");
                cvSum = ParseCsvDoubles(GetFirst(dict, "cv_sum"));
                vcSum = ParseCsvDoubles(GetFirst(dict, "vc_sum"));
                vvSum = ParseCsvDoubles(GetFirst(dict, "vv_sum"));
                cvOffset = ParseCsvDoubles(GetFirst(dict, "cv_offset"));
                vcOffset = ParseCsvDoubles(GetFirst(dict, "vc_offset"));
                cvRepeat = ParseInt(GetFirst(dict, "CV_repeat", "cv_repeat"));
                vcRepeat = ParseInt(GetFirst(dict, "VC_repeat", "vc_repeat"));
                pitch = GetFirst(dict, "pitch");
                cover = GetFirst(dict, "cover");
            }

            if (string.IsNullOrWhiteSpace(dsDictPath))
            {
                if (Path.GetExtension(configPath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    dsDictPath = configPath;
                }
                else
                {
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var fallbackDict = Path.Combine(appDir, "dictionary", "zh.txt");
                    if (File.Exists(fallbackDict))
                    {
                        dsDictPath = fallbackDict;
                    }
                    else
                    {
                        throw new InvalidOperationException("Config 中未找到 ds_dict，且未找到默认字典 dictionary/zh.txt");
                    }
                }
            }
            else
            {
                if (!Path.IsPathRooted(dsDictPath))
                {
                    var baseDir = Path.GetDirectoryName(configPath) ?? "";
                    dsDictPath = Path.Combine(baseDir, dsDictPath);
                }
            }

            if (!File.Exists(dsDictPath))
            {
                throw new FileNotFoundException($"ds_dict 文件不存在: {dsDictPath}");
            }

            ignore ??= "AP,SP,EP,R,-,B";
            cvSum ??= [1, 3, 1.5, 1, 2];
            vcSum ??= [3, 0, 2, 1, 2];
            vvSum ??= [3, 3, 1.5, 1, 1.5];
            cvOffset ??= [0, 0, 0, 0, 0];
            vcOffset ??= [0, 0, 0, 0, 0];
            cvRepeat ??= 1;
            vcRepeat ??= 1;
            pitch ??= "";
            cover ??= "N";

            return new ConversionConfig(
                DsDictPath: dsDictPath,
                PresampPath: presampPath,
                IgnoreCsv: ignore,
                Mode: mode,
                CvSum: cvSum,
                VcSum: vcSum,
                VvSum: vvSum,
                CvOffset: cvOffset,
                VcOffset: vcOffset,
                CvRepeat: cvRepeat.Value,
                VcRepeat: vcRepeat.Value,
                Pitch: pitch,
                Cover: cover);
        }

        private void ConvertOneFolder(string wavFolder, ConversionConfig config, string pitch)
        {
            var textGridDir = Path.Combine(wavFolder, "TextGrid");
            if (!Directory.Exists(textGridDir))
            {
                throw new DirectoryNotFoundException($"TextGrid 文件夹不存在: {textGridDir}");
            }

            var converter = new TextGrid2OtoConverter(
                dsDictPath: config.DsDictPath,
                presampPath: config.PresampPath,
                ignoreCsv: config.IgnoreCsv,
                mode: config.Mode,
                cvSum: config.CvSum,
                vcSum: config.VcSum,
                vvSum: config.VvSum
            );

            var result = converter.ConvertTextGridDirectory(textGridDir);

            var cvRawPath = Path.Combine(wavFolder, "cv_oto.ini");
            var vcRawPath = Path.Combine(wavFolder, "vc_oto.ini");
            var finalPath = Path.Combine(wavFolder, "oto.ini");

            OtoWriter.WriteRawOtoLines(cvRawPath, result.CvRawLines, cover: "Y");
            OtoWriter.WriteRawOtoLines(vcRawPath, result.VcRawLines, cover: "Y");

            var cvEntries = OtoPostProcessor.ParseRawLines(result.CvRawLines);
            var vcEntries = OtoPostProcessor.ParseRawLines(result.VcRawLines);

            cvEntries = OtoPostProcessor.ApplyRepeat(cvEntries, config.CvRepeat);
            vcEntries = OtoPostProcessor.ApplyRepeat(vcEntries, config.VcRepeat);

            if (!OtoPostProcessor.IsZeroOffset(config.CvOffset))
            {
                cvEntries = OtoPostProcessor.ApplyOffset(cvEntries, config.CvOffset);
            }

            if (!OtoPostProcessor.IsZeroOffset(config.VcOffset))
            {
                vcEntries = OtoPostProcessor.ApplyOffset(vcEntries, config.VcOffset);
            }

            OtoWriter.WriteFinalOto(finalPath, cvEntries.Concat(vcEntries), pitch, config.Cover);
        }

        private static List<string> ParseSuffixMarks(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
        }

        private static Dictionary<string, string> ParseYamlLike(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('#')) continue;

                var idx = line.IndexOf(':');
                if (idx <= 0) continue;

                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();

                var commentIdx = value.IndexOf('#');
                if (commentIdx >= 0)
                {
                    value = value[..commentIdx].Trim();
                }

                value = value.Trim().Trim('"');
                if (key.Length == 0) continue;
                dict[key] = value;
            }
            return dict;
        }

        private static string? GetFirst(Dictionary<string, string> dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (dict.TryGetValue(k, out var v))
                {
                    return string.IsNullOrWhiteSpace(v) ? null : v;
                }
            }
            return null;
        }

        private static double[]? ParseCsvDoubles(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var parts = raw.Trim().Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return null;
            var result = new double[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                result[i] = double.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
            }
            return result;
        }

        private static int? ParseInt(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (int.TryParse(raw.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
            return null;
        }
        
        private readonly StringBuilder _logBuilder = new();

        [ObservableProperty]
        private string _logText = string.Empty;

        /// <summary>
        /// 添加日志信息
        /// </summary>
        public void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logBuilder.AppendLine($"[{timestamp}] {message}");
            LogText = _logBuilder.ToString();
        }

        /// <summary>
        /// 添加错误日志
        /// </summary>
        public void LogError(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logBuilder.AppendLine($"[{timestamp}] [ERROR] {message}");
            LogText = _logBuilder.ToString();
        }

        /// <summary>
        /// 添加警告日志
        /// </summary>
        public void LogWarning(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logBuilder.AppendLine($"[{timestamp}] [WARNING] {message}");
            LogText = _logBuilder.ToString();
        }

        /// <summary>
        /// 添加成功日志
        /// </summary>
        public void LogSuccess(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logBuilder.AppendLine($"[{timestamp}] [SUCCESS] {message}");
            LogText = _logBuilder.ToString();
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        public void ClearLog()
        {
            _logBuilder.Clear();
            LogText = string.Empty;
        }

        /// <summary>
        /// 更新进度条
        /// </summary>
        private void UpdateProgress(int current, int total, string status = "")
        {
            if (total > 0)
            {
                ProgressValue = (double)current / total * 100;
                ProgressText = string.IsNullOrEmpty(status) 
                    ? $"{current}/{total}" 
                    : $"{status} ({current}/{total})";
            }
            IsProgressIndeterminate = false;
        }

        /// <summary>
        /// 设置不确定进度
        /// </summary>
        private void SetIndeterminateProgress(string status)
        {
            IsProgressIndeterminate = true;
            ProgressText = status;
        }

        private void PostToUi(Action action)
        {
            Dispatcher.UIThread.Post(action);
        }

        /// <summary>
        /// 重置进度条
        /// </summary>
        private void ResetProgress()
        {
            ProgressValue = 0;
            ProgressText = "就绪";
            IsProgressIndeterminate = false;
        }

        /// <summary>
        /// 获取当前所有配置信息（用于调试或显示）
        /// </summary>
        public void LogCurrentConfig()
        {
            Log("========== 当前配置 ==========");
            Log($"Wav 文件夹: {(string.IsNullOrEmpty(WavFolderPath) ? "(未设置)" : WavFolderPath)}");
            Log($"Presamp 文件: {(string.IsNullOrEmpty(PresampFilePath) ? "(未设置)" : PresampFilePath)}");
            Log($"Config 文件: {(string.IsNullOrEmpty(ConfigFilePath) ? "(未设置)" : ConfigFilePath)}");
            Log($"类型: {GetTypeName(SelectedTypeIndex)}");
            Log($"多音阶: {(SelectedMultipleScalesIndex == 0 ? "关闭" : "开启")}");
            Log($"后缀: {(string.IsNullOrEmpty(Suffix) ? "(未设置)" : Suffix)}");
            Log("=============================");
        }

        private string GetTypeName(int index)
        {
            return index switch
            {
                0 => "CV",
                1 => "CVVC",
                2 => "VCV",
                _ => "未知"
            };
        }

        /// <summary>
        /// 加载可用的语言列表（从FA模型的vocab.json中读取）
        /// </summary>
        private void LoadAvailableLanguages()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string faModelsPath = Path.Combine(appDir, "FA_Model");
                if (!Directory.Exists(faModelsPath))
                {
                    faModelsPath = Path.Combine(appDir, "FA_Models");
                }

                if (!Directory.Exists(faModelsPath))
                {
                    // 如果FA模型文件夹不存在，使用默认语言列表
                    AvailableLanguages = new List<string> { "zh", "ja", "en" };
                    SelectedLanguageIndex = 0;
                    return;
                }

                string vocabPath = Path.Combine(faModelsPath, "vocab.json");
                if (!File.Exists(vocabPath))
                {
                    // 如果vocab.json不存在，使用默认语言列表
                    AvailableLanguages = new List<string> { "zh", "ja", "en" };
                    SelectedLanguageIndex = 0;
                    return;
                }

                // 读取vocab.json并解析语言列表
                using var doc = JsonDocument.Parse(File.ReadAllText(vocabPath, Encoding.UTF8));
                var root = doc.RootElement;
                
                if (root.TryGetProperty("dictionaries", out var dictionaries))
                {
                    var languages = new List<string>();
                    foreach (var prop in dictionaries.EnumerateObject())
                    {
                        languages.Add(prop.Name);
                    }
                    
                    if (languages.Count > 0)
                    {
                        AvailableLanguages = languages;
                        SelectedLanguageIndex = 0;
                        return;
                    }
                }

                // 如果没有找到dictionaries字段，使用默认语言列表
                AvailableLanguages = new List<string> { "zh", "ja", "en" };
                SelectedLanguageIndex = 0;
            }
            catch (Exception)
            {
                // 如果读取失败，使用默认语言列表
                AvailableLanguages = new List<string> { "zh", "ja", "en" };
                SelectedLanguageIndex = 0;
            }
        }
    }
}
