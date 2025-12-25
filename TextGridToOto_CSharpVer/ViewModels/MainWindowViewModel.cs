using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextGridToOto_CSharpVer.Infer;

namespace TextGridToOto_CSharpVer.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
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

                SetIndeterminateProgress("推理中...");
                await System.Threading.Tasks.Task.Run(() =>
                {
                    FaAutoAnnotator.RunFolder(
                        wavFolder: WavFolderPath,
                        labFolder: labFolder,
                        modelFolder: faModelsPath,
                        language: "zh",
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
    }
}
