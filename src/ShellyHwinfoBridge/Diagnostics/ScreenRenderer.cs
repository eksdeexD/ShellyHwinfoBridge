using System.Drawing.Imaging;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>
/// Renders windows to PNG without showing them (<c>--render-screens &lt;dir&gt;</c>), for visual comparison with
/// design/final. The process runs DPI-unaware in this mode, so everything renders at 100 %.
/// </summary>
/// <remarks>
/// Add a window by appending to <see cref="Screens"/>: a file name (without extension) and a factory that builds the
/// form with fake data. Each form is created, laid out and drawn with <see cref="Control.DrawToBitmap"/>, then disposed.
/// </remarks>
internal static class ScreenRenderer
{
    /// <summary>The screens to render, in order.</summary>
    public static readonly List<(string Name, Func<Form> Create)> Screens =
    [
        ("gallery-1", () => new ControlGalleryForm(1)),
        ("gallery-2", () => new ControlGalleryForm(2)),
    ];

    /// <summary>Renders every screen into <paramref name="directory"/>; returns the process exit code.</summary>
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var (name, create) in Screens)
        {
            using var form = create();
            var path = Path.Combine(directory, name + ".png");
            using var bitmap = Render(form);
            bitmap.Save(path, ImageFormat.Png);
            Console.WriteLine(path);
        }

        return 0;
    }

    /// <summary>
    /// Renders a form's client area. The form is shown off-screen and fully transparent for the capture, because
    /// WM_PRINT skips the children of a window that was never shown.
    /// </summary>
    public static Bitmap Render(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.StartPosition = FormStartPosition.Manual;
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.CreateControl();
        form.Location = new Point(-30000, -30000);
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        var client = form.ClientSize;
        using var whole = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(whole, new Rectangle(Point.Empty, form.Size));
        var origin = form.PointToScreen(Point.Empty);
        var offset = new Point(origin.X - form.Left, origin.Y - form.Top);
        var result = new Bitmap(client.Width, client.Height);
        using (var g = Graphics.FromImage(result))
        {
            g.DrawImage(whole, new Rectangle(Point.Empty, client), new Rectangle(offset, client), GraphicsUnit.Pixel);
        }

        form.Hide();
        return result;
    }
}
