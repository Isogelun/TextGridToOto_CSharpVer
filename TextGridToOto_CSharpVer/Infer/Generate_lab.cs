using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TextGridToOto_CSharpVer.Infer
{
    /// <summary>
    /// Lab 文件生成器 - 从 wav 文件名生成对应的 lab 标注文件
    /// </summary>
    internal class Generate_lab
    {
        /// <summary>
        /// 处理 wav 文件，生成对应的 lab 文件
        /// </summary>
        /// <param name="folderPath">包含 wav 文件的文件夹路径</param>
        /// <param name="outputFolder">保存 lab 文件的输出文件夹路径</param>
        /// <returns>成功处理的文件数量</returns>
        public static int ProcessWavFiles(string folderPath, string outputFolder)
        {
            // 创建输出目录（如果不存在）
            var outputDir = new DirectoryInfo(outputFolder);
            if (!outputDir.Exists)
            {
                outputDir.Create();
            }

            var folder = new DirectoryInfo(folderPath);
            if (!folder.Exists)
            {
                throw new DirectoryNotFoundException($"文件夹不存在: {folderPath}");
            }

            int processedCount = 0;

            // 遍历文件夹中的所有文件
            foreach (var file in folder.GetFiles())
            {
                // 只处理 wav 文件
                if (file.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    // 分离文件名（不含扩展名）
                    string namePart = Path.GetFileNameWithoutExtension(file.Name);

                    // 处理内容：下划线转空格 + 去除首尾空格
                    string content = namePart.Replace('_', ' ').Trim();

                    // 生成 lab 文件路径
                    string labFileName = Path.GetFileNameWithoutExtension(file.Name) + ".lab";
                    string labFilePath = Path.Combine(outputFolder, labFileName);

                    // 写入 lab 文件
                    File.WriteAllText(labFilePath, content, Encoding.UTF8);

                    Console.WriteLine($"Created: {labFileName} (内容: \"{content}\")");
                    processedCount++;
                }
            }

            return processedCount;
        }

        /// <summary>
        /// 异步处理 wav 文件，生成对应的 lab 文件
        /// </summary>
        public static async Task<int> ProcessWavFilesAsync(string folderPath, string outputFolder)
        {
            return await Task.Run(() => ProcessWavFiles(folderPath, outputFolder));
        }

        /// <summary>
        /// 批量生成 lab 文件（带进度回调）
        /// </summary>
        /// <param name="folderPath">wav 文件夹路径</param>
        /// <param name="outputFolder">输出文件夹路径</param>
        /// <param name="progressCallback">进度回调 (当前处理数, 总文件数, 文件名)</param>
        public static int ProcessWavFilesWithProgress(string folderPath, string outputFolder,
            Action<int, int, string>? progressCallback = null)
        {
            // 创建输出目录
            var outputDir = new DirectoryInfo(outputFolder);
            if (!outputDir.Exists)
            {
                outputDir.Create();
            }

            var folder = new DirectoryInfo(folderPath);
            if (!folder.Exists)
            {
                throw new DirectoryNotFoundException($"文件夹不存在: {folderPath}");
            }

            // 获取所有 wav 文件
            var wavFiles = folder.GetFiles("*.wav", SearchOption.TopDirectoryOnly);
            int totalCount = wavFiles.Length;
            int processedCount = 0;

            foreach (var file in wavFiles)
            {
                string namePart = Path.GetFileNameWithoutExtension(file.Name);
                string content = namePart.Replace('_', ' ').Trim();
                string labFileName = Path.GetFileNameWithoutExtension(file.Name) + ".lab";
                string labFilePath = Path.Combine(outputFolder, labFileName);

                File.WriteAllText(labFilePath, content, Encoding.UTF8);

                processedCount++;
                progressCallback?.Invoke(processedCount, totalCount, labFileName);
            }

            return processedCount;
        }

        /// <summary>
        /// 从单个 wav 文件名生成 lab 文件内容
        /// </summary>
        /// <param name="wavFileName">wav 文件名（不含路径）</param>
        /// <returns>lab 文件内容</returns>
        public static string GenerateLabContent(string wavFileName)
        {
            string namePart = Path.GetFileNameWithoutExtension(wavFileName);
            return namePart.Replace('_', ' ').Trim();
        }

        /// <summary>
        /// 验证路径是否有效
        /// </summary>
        public static bool ValidatePath(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }
    }
}
