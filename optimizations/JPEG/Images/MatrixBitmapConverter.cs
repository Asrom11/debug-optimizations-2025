using System.Drawing;
using System.Drawing.Imaging;

namespace JPEG.Images;

public static class MatrixBitmapConverter
{
    private const int BytesPerPixel = 3;
    public static unsafe Matrix BitmapToMatrix(Bitmap bmp)
    {
        var height = bmp.Height - bmp.Height % 8;
        var width = bmp.Width - bmp.Width % 8;
        var matrix = new Matrix(height, width);

        var bmpData = bmp.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        var stride = bmpData.Stride;


        var scan0 = (byte*)bmpData.Scan0.ToPointer();

        for (var y = 0; y < height; y++)
        {
            var  row = scan0 + y * stride;

            for (var x = 0; x < width; x++)
            {

                var p = row + x * BytesPerPixel;

                var b = p[0];
                var g = p[1];
                var r = p[2];

                matrix.Pixels[y, x] = new Pixel(r, g, b, PixelFormat.RGB);
            }
        }


        bmp.UnlockBits(bmpData);

        return matrix;
    }


    public static unsafe Bitmap MatrixToBitmap(Matrix matrix)
    {
        var width = matrix.Width;
        var height = matrix.Height;


        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);


        var bmpData = bmp.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        var stride = bmpData.Stride;

        var scan0 = (byte*)bmpData.Scan0.ToPointer();

        for (var y = 0; y < height; y++)
        {
            var row = scan0 + (y * stride);

            for (var x = 0; x < width; x++)
            {
                var pixel = matrix.Pixels[y, x];

                var p = row + x * BytesPerPixel;

                p[0] = ToByte(pixel.B);
                p[1] = ToByte(pixel.G);
                p[2] = ToByte(pixel.R);
            }
        }

        bmp.UnlockBits(bmpData);

        return bmp;
    }

    private static byte ToByte(double value)
    {
        return value switch
        {
            < 0 => 0,
            > 255 => 255,
            _ => (byte)value
        };
    }
}