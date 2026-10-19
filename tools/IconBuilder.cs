using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

internal static class IconBuilder
{
    private static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath(); p.AddArc(x, y, r * 2, r * 2, 180, 90); p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90); p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90); p.CloseFigure(); return p;
    }
    private static Bitmap Draw(int size)
    {
        using (var large = new Bitmap(size * 4, size * 4, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(large))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(size * 4f / 256f, size * 4f / 256f); g.Clear(Color.Transparent);
            using (var path = Rounded(4, 4, 248, 248, 53))
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 256, 256), Color.FromArgb(45, 38, 89), Color.FromArgb(14, 24, 42), 65f)) g.FillPath(brush, path);
            using (var path = Rounded(37, 39, 151, 105, 12))
            using (var brush = new SolidBrush(Color.FromArgb(36, 36, 74)))
            using (var pen = new Pen(Color.FromArgb(156, 132, 255), 11)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
            using (var path = Rounded(77, 84, 144, 105, 12))
            using (var brush = new SolidBrush(Color.FromArgb(19, 38, 58)))
            using (var pen = new Pen(Color.FromArgb(86, 227, 219), 11)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
            using (var pen = new Pen(Color.FromArgb(86, 227, 219), 10))
            { pen.StartCap = pen.EndCap = LineCap.Round; g.DrawLine(pen, 149, 193, 149, 213); g.DrawLine(pen, 126, 215, 172, 215); }
            using (var pen = new Pen(Color.White, 9))
            {
                pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new Point[] { new Point(112, 121), new Point(184, 121), new Point(174, 111) });
                g.DrawLine(pen, 184, 121, 174, 131);
                g.DrawLines(pen, new Point[] { new Point(186, 153), new Point(114, 153), new Point(124, 143) });
                g.DrawLine(pen, 114, 153, 124, 163);
            }
            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics target = Graphics.FromImage(result)) { target.InterpolationMode = InterpolationMode.HighQualityBicubic; target.DrawImage(large, 0, 0, size, size); }
            return result;
        }
    }
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(args[0]); int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 }; var images = new List<byte[]>();
        foreach (int size in sizes) using (Bitmap bmp = Draw(size)) using (var stream = new MemoryStream()) { bmp.Save(stream, ImageFormat.Png); images.Add(stream.ToArray()); if (size == 256) bmp.Save(Path.Combine(args[0], "monitor-switch.png"), ImageFormat.Png); }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(args[0], "monitor-switch.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); uint offset = (uint)(6 + sizes.Length * 16);
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((uint)images[i].Length); writer.Write(offset); offset += (uint)images[i].Length;
            }
            foreach (byte[] bytes in images) writer.Write(bytes);
        }
        return 0;
    }
}
