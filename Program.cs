using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TcpDebugAssistant
{
    /// <summary>
    /// 程序入口。
    /// 采用标准的 WinForms 启动方式：开启视觉样式后运行主窗体 MainForm。
    /// 额外增加全局异常捕获，把崩溃原因写入程序目录下的 crash.log，便于排查“打不开”等问题。
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // 全局异常捕获：把崩溃原因落盘，避免“一闪而过/打不开”时看不到原因
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                WriteCrash("UnhandledException", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            Application.ThreadException += (s, e) =>
                WriteCrash("ThreadException", e.Exception);

            try
            {
                // .NET 中 GB2312/GBK 等非内置编码需先注册 CodePages 提供程序，否则 GetEncoding 会报错
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                // 开启系统主题（否则控件显示为经典灰白样式）
                Application.EnableVisualStyles();
                // 关闭兼容文本渲染，使用更清晰的 GDI+ 文本
                Application.SetCompatibleTextRenderingDefault(false);
                // 启动主窗体
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                WriteCrash("MainTryCatch", ex);
                MessageBox.Show("程序启动失败：\n" + ex.Message + "\n\n详细日志见程序目录 crash.log",
                    "TCP 调试助手 - 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 将异常信息追加写入程序所在目录的 crash.log。
        /// </summary>
        static void WriteCrash(string tag, Exception ex)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}:\r\n{ex}\r\n\r\n");
            }
            catch { /* 忽略写日志失败 */ }
        }
    }
}
