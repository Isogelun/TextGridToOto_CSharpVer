using System;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TextGridToOto_CSharpVer.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
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
    }
}
