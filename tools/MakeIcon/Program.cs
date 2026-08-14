using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var pngPath = args[0];
var icoPath = args[1];
int[] sizes = [16, 32, 48, 64, 128, 256];

using var src = new Bitmap(pngPath);
var images = new List<Bitmap>();
var payloads = new List<byte[]>();

foreach (var size in sizes)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, size, size);
    }

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    payloads.Add(ms.ToArray());
    images.Add(bmp);
}

await using var fs = File.Create(icoPath);
await using var bw = new BinaryWriter(fs);
bw.Write((short)0);
bw.Write((short)1);
bw.Write((short)images.Count);

var offset = 6 + 16 * images.Count;
for (var i = 0; i < images.Count; i++)
{
    var bmp = images[i];
    var bytes = payloads[i];
    bw.Write((byte)(bmp.Width >= 256 ? 0 : bmp.Width));
    bw.Write((byte)(bmp.Height >= 256 ? 0 : bmp.Height));
    bw.Write((byte)0);
    bw.Write((byte)0);
    bw.Write((short)1);
    bw.Write((short)32);
    bw.Write(bytes.Length);
    bw.Write(offset);
    offset += bytes.Length;
}

foreach (var bytes in payloads)
    bw.Write(bytes);

foreach (var bmp in images)
    bmp.Dispose();

Console.WriteLine($"Wrote {icoPath}");
