# TextGridToOto_CSharpVer

一个基于 Avalonia UI 的跨平台桌面应用程序，用于语音合成数据标注。通过深度学习驱动的强制对齐(Forced Alignment)技术，自动生成高质量的 TextGrid 标注文件，主要用于 VOCALOID、UTAU、DeepVocal 等歌声合成软件的音源制作。

## ✨ 功能特性

### 核心功能
- 🎯 **LAB 文件生成**：从 WAV 文件名自动生成对应的 LAB 标注文件
- 🤖 **AI 强制对齐**：使用 ONNX 深度学习模型进行语音与文本的自动对齐
- 📊 **TextGrid 输出**：生成标准的 Praat TextGrid 格式文件，包含音素和单词级别的时间标注
- 🎨 **现代化 GUI**：简洁直观的图形界面，支持实时进度显示和详细日志
- 🌏 **多语言支持**：内置中文、日语、英语字典，支持扩展更多语言

### 技术亮点
- ⚡ 基于 ONNX Runtime 的高效推理引擎
- 🔧 智能音素边界优化算法
- 📈 批量处理支持，适合大规模数据标注
- 🎵 自动处理立体声转单声道、采样率重采样

## 🛠️ 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET | 8.0 | 应用程序框架 |
| Avalonia UI | 11.3.9 | 跨平台桌面 UI 框架 |
| ONNX Runtime | 1.20.0 | 深度学习模型推理 |
| NAudio | 2.2.1 | 音频文件处理与重采样 |
| CommunityToolkit.Mvvm | 8.2.1 | MVVM 架构支持 |

## 📁 项目结构

```
TextGridToOto_CSharpVer/
├── ViewModels/              # MVVM 视图模型
│   ├── MainWindowViewModel.cs   # 主窗口逻辑
│   └── ViewModelBase.cs         # 视图模型基类
├── Views/                   # UI 视图
│   └── MainWindow.axaml.cs      # 主窗口视图
├── Infer/                   # 推理核心模块
│   ├── Onnx_Infer.cs           # ONNX 强制对齐引擎
│   └── Generate_lab.cs         # LAB 文件生成器
├── Config/                  # 配置文件
│   ├── Config.yaml
│   └── Run_Config.yaml
├── dictionary/              # 语言字典
│   ├── zh.txt              # 中文拼音字典
│   ├── ja.txt              # 日语假名字典
│   ├── ja-romaji.txt       # 日语罗马音字典
│   ├── en.txt              # 英语音素字典
│   └── hira2roma_list.txt  # 平假名罗马音映射
└── FA_Models/               # 强制对齐模型文件（需自行准备）
    ├── model.onnx          # ONNX 模型
    ├── vocab.json          # 词汇表
    ├── config.json         # 模型配置
    ├── VERSION             # 模型版本（需为 5）
    └── *.txt               # 语言字典
```

## 🚀 快速开始

### 环境要求

- **操作系统**：Windows 10/11、macOS 10.15+、Linux（主流发行版）
- **.NET SDK**：8.0 或更高版本
- **内存**：建议 8GB 以上
- **存储空间**：模型文件约 100MB - 500MB

### 安装步骤

#### 1. 克隆或下载项目

```bash
git clone https://github.com/yourusername/TextGridToOto_CSharpVer.git
cd TextGridToOto_CSharpVer
```

#### 2. 准备强制对齐模型

在项目根目录创建 `FA_Model` 或 `FA_Models` 文件夹，放入以下文件：

```
FA_Models/
├── model.onnx          # 必需：ONNX 格式的强制对齐模型
├── vocab.json          # 必需：词汇表配置文件
├── config.json         # 必需：模型超参数配置
├── VERSION             # 必需：版本标识文件（内容为：5）
└── zh.txt              # 推荐：中文拼音字典（或其他语言字典）
```

**模型版本要求**：
- `VERSION` 文件内容必须为 `5`
- 模型需支持输出 `ph_frame_logits`、`ph_edge_logits`、`cvnt_logits` 三个张量

#### 3. 构建并运行

```bash
dotnet restore
dotnet build
dotnet run
```

或在 Visual Studio / JetBrains Rider 中直接打开 `.csproj` 文件并运行。

## 📖 使用指南

### 工作流程

```
WAV 音频文件 → 生成 LAB 标注 → 强制对齐推理 → 输出 TextGrid 文件
```

#### 第 1 步：准备 WAV 文件

将需要标注的 WAV 音频文件放入同一文件夹，文件命名规则：

```
文件名示例：
你好_世界.wav        → LAB 内容：你好 世界
こんにちは.wav       → LAB 内容：こんにちは
hello_world.wav      → LAB 内容：hello world
```

**重要提示**：
- 文件名中的 `_`（下划线）会被自动替换为空格
- 确保文件名包含正确的歌词或音素序列
- 音频要求：单声道或立体声，采样率任意（会自动重采样）

#### 第 2 步：生成 LAB 文件

1. 启动应用程序
2. 点击 **「选择 Wav Folder」** 按钮，选择 WAV 文件所在文件夹
3. 点击 **「Run LAB」** 按钮
4. 程序会在 WAV 文件夹下创建 `lab/` 子文件夹，并生成同名的 `.lab` 文件

**示例输出**：
```
wav_folder/
├── 你好_世界.wav
├── こんにちは.wav
└── lab/
    ├── 你好_世界.lab      # 内容：你好 世界
    └── こんにちは.lab     # 内容：こんにちは
```

#### 第 3 步：执行强制对齐

1. 确认 LAB 文件已生成
2. 点击 **「Run FA」** 按钮
3. 程序会：
   - 加载 ONNX 模型
   - 读取 WAV 音频和对应的 LAB 文件
   - 使用深度学习模型进行音素级对齐
   - 在 WAV 文件夹下创建 `TextGrid/` 子文件夹并保存结果

**输出结果**：
```
wav_folder/
├── 你好_世界.wav
└── TextGrid/
    └── 你好_世界.TextGrid
```

#### 第 4 步：查看和使用 TextGrid

使用 [Praat](https://www.fon.hum.uva.nl/praat/) 软件打开 TextGrid 文件，可以看到：
- **Word 层**：单词级别的时间标注
- **Phone 层**：音素级别的时间标注
- **非词汇音素**：如 AP（吸气音）、EP（呼气音）、SP（静音）

### 界面说明

| 控件 | 功能说明 |
|------|----------|
| **Wav Folder** | 选择包含 WAV 音频文件的文件夹 |
| **Presamp File** | （预留）预设参数文件路径 |
| **Config File** | （预留）配置文件路径 |
| **类型选择** | （预留）CV、CVVC、VCV 音源类型 |
| **多音阶** | （预留）是否为多音阶音源 |
| **后缀** | （预留）生成文件的后缀名 |
| **Run LAB** | 执行 LAB 文件生成 |
| **Run FA** | 执行强制对齐推理 |
| **进度条** | 显示当前处理进度 |
| **日志区域** | 显示详细的操作日志和错误信息 |

## 🔧 高级配置

### 模型配置文件说明

#### vocab.json

```json
{
  "vocab": {          // 音素到 ID 的映射
    "SP": 0,
    "AP": 1,
    "...": 2
  },
  "vocab_size": 100,
  "non_lexical_phonemes": ["AP", "EP"],  // 非词汇音素列表
  "dictionaries": {    // 语言字典映射
    "zh": "zh.txt",
    "ja": "ja.txt",
    "en": "en.txt"
  },
  "language_prefix": true  // 是否为音素添加语言前缀（如 zh/a）
}
```

#### config.json

```json
{
  "mel_spec_config": {
    "sample_rate": 44100,  // 采样率
    "hop_size": 441        // 帧移
  }
}
```

### 字典文件格式

每行一个词条，格式为：`单词<TAB>音素序列`

```
你好	n i3 h ao3
世界	sh i4 j ie4
こんにちは	k o N n i ch i w a
hello	hh ax l ow
```

### 自定义推理参数

修改 `MainWindowViewModel.cs` 中的 `RunFA()` 方法参数：

```csharp
FaAutoAnnotator.RunFolder(
    wavFolder: WavFolderPath,
    labFolder: labFolder,
    modelFolder: faModelsPath,
    language: "zh",              // 修改为目标语言：zh/ja/en
    g2p: "dictionary",           // 或 "phoneme"（直接使用音素）
    dictionaryPath: null,        // 自定义字典路径
    nonLexicalPhonemes: "AP,EP", // 非词汇音素（逗号分隔）
    padTimes: 1,                 // 填充次数（用于边界优化）
    padLength: 5.0               // 填充长度（秒）
);
```

## ❓ 常见问题

### 1. 找不到 FA 模型文件夹

**错误信息**：
```
FA 模型文件夹不存在: E:\...\FA_Model
```

**解决方法**：
- 在项目根目录或可执行文件目录下创建 `FA_Model` 或 `FA_Models` 文件夹
- 确保文件夹内包含 `model.onnx`、`vocab.json`、`config.json`、`VERSION` 文件

### 2. 模型版本不匹配

**错误信息**：
```
onnx model version must be 5.
```

**解决方法**：
- 创建 `VERSION` 文件，内容为单独一行 `5`
- 或使用兼容版本 5 的模型文件

### 3. LAB 文件为空或生成失败

**可能原因**：
- WAV 文件名为空或只包含下划线
- 文件夹路径包含特殊字符

**解决方法**：
- 确保 WAV 文件名包含有效内容
- 使用英文或常规字符作为文件夹路径

### 4. 强制对齐推理失败

**可能原因**：
- WAV 文件损坏或格式不支持
- LAB 文件内容与字典不匹配
- 内存不足

**解决方法**：
- 使用专业音频软件检查 WAV 文件完整性
- 确认 LAB 内容中的词汇都存在于字典文件中
- 尝试分批处理文件

### 5. 输出的 TextGrid 边界不准确

**调优方法**：
- 增加 `padTimes` 参数（如改为 3 或 5）以进行多次推理取平均
- 调整 `padLength` 参数（默认 5.0 秒）
- 确保训练模型时使用了与当前数据相似的音频质量

## 🤝 贡献指南

欢迎提交 Issue 和 Pull Request！

### 开发环境搭建

```bash
git clone <your-fork>
cd TextGridToOto_CSharpVer
dotnet restore
dotnet build
```

### 代码规范

- 遵循 C# 官方命名约定
- 使用 MVVM 架构模式
- 添加 XML 文档注释
- 提交前运行测试

## 📄 许可证

MIT License - 详见 [LICENSE](LICENSE) 文件

## 🙏 致谢

- [Avalonia UI](https://avaloniaui.net/) - 跨平台 UI 框架
- [ONNX Runtime](https://onnxruntime.ai/) - 高性能推理引擎
- [NAudio](https://github.com/naudio/NAudio) - 音频处理库

## 📮 联系方式

- **Issues**：[GitHub Issues](https://github.com/yourusername/TextGridToOto_CSharpVer/issues)
- **Discussions**：[GitHub Discussions](https://github.com/yourusername/TextGridToOto_CSharpVer/discussions)

---

**Star** ⭐ 本项目如果对你有帮助！

