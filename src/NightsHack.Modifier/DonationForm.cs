using System.Diagnostics;

namespace NightsHack.Modifier;

internal sealed class DonationForm : Form
{
    const string RepositoryUrl = "https://github.com/Kainy030/Nivalis-Nights-modifier";
    readonly List<Image> images = new();

    public DonationForm()
    {
        Text = "无偿捐赠作者";
        Font = new Font("Microsoft YaHei UI", 10);
        ClientSize = new Size(880, 640);
        MinimumSize = new Size(680, 520);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var repository = new Button { Text = "本项目为开源公益项目（点击按钮转入github仓库）", Dock = DockStyle.Fill };
        repository.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, "无法打开浏览器：" + error.Message + Environment.NewLine + RepositoryUrl, "GitHub 仓库", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        layout.Controls.Add(repository, 0, 0);
        layout.Controls.Add(new Label { Text = "本项目为开源公益项目，仅接受无偿捐赠，如果你在任意渠道付费下载本修改器，恭喜你，你被圈钱了。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 8, 4, 8) }, 0, 1);
        var pictures = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        pictures.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pictures.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pictures.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(pictures, 0, 2);
        Controls.Add(layout);
        // Only load packaged assets; the installed Trainer never accesses the developer's D:\Pay directory.
        for (int index = 0; index < 2; index++)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "donation", $"donation-{index + 1}.image");
            try
            {
                using var source = Image.FromFile(path);
                var image = new Bitmap(source);
                images.Add(image);
                pictures.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image, BackColor = Color.White, Margin = new Padding(8), AccessibleName = $"捐赠图片 {index + 1}" }, index, 0);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
            {
                pictures.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = $"捐赠图片 {index + 1} 未打包或无法显示。\n请使用完整发布包。" }, index, 0);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var image in images) image.Dispose();
            images.Clear();
        }
    }
}
