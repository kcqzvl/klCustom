using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace KlCustom.Downloader
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new DownloadWindow());
        }
    }

    internal sealed class DownloadWindow : Form
    {
        private readonly CheckBox includeRuntime = new CheckBox();
        private readonly TextBox destination = new TextBox();
        private readonly Button browse = new Button();
        private readonly Button prepare = new Button();
        private readonly Button openFolder = new Button();
        private readonly Label status = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private string preparedFolder;
        private bool busy;

        internal DownloadWindow()
        {
            var version = typeof(DownloadWindow).Assembly.GetName().Version.ToString(3);
            Text = "kl Custom 下载器";
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(556, 286);

            var title = new Label
            {
                Text = "kl Custom " + version, Font = new Font(Font.FontFamily, 16, FontStyle.Bold)
            };
            Add(title, 24, 18, 508, 32);
            Add(new Label { Text = "保存到" }, 24, 66, 508, 20);
            destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Add(destination, 24, 88, 416, 25);
            browse.Text = "浏览…";
            browse.Click += Browse;
            Add(browse, 450, 85, 82, 30);

            bool installed = Package.HasDesktopRuntime();
            includeRuntime.Text = "同时下载 .NET 10 桌面运行库（x64）";
            includeRuntime.Checked = !installed;
            Add(includeRuntime, 24, 130, 508, 24);
            status.Text = installed ? "已检测到 .NET 10 桌面运行库。" : "可勾选下载运行库，工具箱会直接使用随附的 .NET 10。";
            Add(status, 24, 164, 508, 42);
            progress.Visible = false;
            Add(progress, 24, 209, 508, 9);

            openFolder.Text = "打开文件夹";
            openFolder.Enabled = false;
            openFolder.Click += OpenFolder;
            Add(openFolder, 316, 237, 102, 34);
            prepare.Text = "开始";
            prepare.Click += Prepare;
            Add(prepare, 428, 237, 104, 34);
            AcceptButton = prepare;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true;
            base.OnFormClosing(e);
        }

        private void Add(Control control, int left, int top, int width, int height)
        {
            control.SetBounds(left, top, width, height);
            Controls.Add(control);
        }

        private void Browse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择工具箱保存位置";
                dialog.SelectedPath = destination.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    destination.Text = dialog.SelectedPath;
            }
        }

        private async void Prepare(object sender, EventArgs e)
        {
            bool downloadRuntime = includeRuntime.Checked;
            string selectedPath = destination.Text.Trim();
            busy = true;
            prepare.Enabled = browse.Enabled = destination.Enabled = includeRuntime.Enabled = openFolder.Enabled = false;
            progress.Visible = true;
            progress.Style = ProgressBarStyle.Marquee;
            status.Text = downloadRuntime ? "正在解压工具箱并从微软下载运行库…" : "正在解压工具箱…";
            try
            {
                preparedFolder = await Package.PrepareAsync(selectedPath, downloadRuntime);
                status.Text = downloadRuntime
                    ? "已保存。运行库在工具箱的 .net 文件夹中，可以直接运行 klCustom.exe。"
                    : "已保存。请打开文件夹并运行 klCustom.exe。";
            }
            catch (Exception error)
            {
                status.Text = "未能完成：" + error.Message;
            }
            finally
            {
                busy = false;
                progress.Visible = false;
                prepare.Enabled = browse.Enabled = destination.Enabled = includeRuntime.Enabled = true;
                openFolder.Enabled = preparedFolder != null;
            }
        }

        private void OpenFolder(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(preparedFolder) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                status.Text = error.Message;
            }
        }
    }
}
