using System.Drawing;

namespace JPEG.Images;

public class Matrix
{
	public readonly Pixel[,] Pixels;
	public readonly int Height;
	public readonly int Width;

	public Matrix(int height, int width)
	{
		Height = height;
		Width = width;

		Pixels = new Pixel[height, width];
	}

	public static explicit operator Matrix(Bitmap bmp)
	{
		return MatrixBitmapConverter.BitmapToMatrix(bmp);
	}

	public static explicit operator Bitmap(Matrix matrix)
	{
		return MatrixBitmapConverter.MatrixToBitmap(matrix);
	}
}