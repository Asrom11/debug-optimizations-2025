using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace JPEG;

public class DCT
{
    private static readonly double[,] CosineTableX;
    private static readonly double[,] CosineTableY;
    private static readonly double[] AlphaValues;

    static DCT()
    {
        const int size = 8;
        CosineTableX = new double[size, size];
        CosineTableY = new double[size, size];
        AlphaValues = new double[size];

        for (var u = 0; u < size; u++)
        {
            AlphaValues[u] = u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;

            for (var x = 0; x < size; x++)
            {
                CosineTableX[u, x] = Math.Cos(((2.0 * x + 1.0) * u * Math.PI) / (2.0 * size));
                CosineTableY[u, x] = Math.Cos(((2.0 * x + 1.0) * u * Math.PI) / (2.0 * size));
            }
        }
    }

    public static double[,] DCT2D(double[,] input)
    {
        var height = input.GetLength(0);
        var width = input.GetLength(1);
        var coeffs = new double[width, height];
        var beta = 1.0 / width + 1.0 / height;

        var temp = new double[width, height];

        Parallel.For(0, height, y =>
        {
            for (var u = 0; u < width; u++)
            {
                var sum = 0.0;

                for (var x = 0; x < width; x++)
                {
                    var cosVal = (u < 8 && x < 8) ? CosineTableX[u, x] :
                        Math.Cos(((2.0 * x + 1.0) * u * Math.PI) / (2.0 * width));

                    sum += input[x, y] * cosVal;
                }

                temp[u, y] = sum;
            }
        });

        Parallel.For(0, width, u =>
        {
            var alphaU = (u < AlphaValues.Length) ? AlphaValues[u] :
                (u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0);

            for (var v = 0; v < height; v++)
            {
                var alphaV = (v < AlphaValues.Length) ? AlphaValues[v] :
                    (v == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0);

                var sum = 0.0;

                for (var y = 0; y < height; y++)
                {
                    var cosVal = (v < 8 && y < 8) ? CosineTableY[v, y] :
                        Math.Cos(((2.0 * y + 1.0) * v * Math.PI) / (2.0 * height));

                    sum += temp[u, y] * cosVal;
                }

                coeffs[u, v] = sum * beta * alphaU * alphaV;
            }
        });

        return coeffs;
    }

    public static void IDCT2D(double[,] coeffs, double[,] output)
    {
        var height = coeffs.GetLength(0);
        var width = coeffs.GetLength(1);
        var beta = 1.0 / width + 1.0 / height;

        var temp = new double[width, height];

        Parallel.For(0, width, x =>
        {
            for (var v = 0; v < height; v++)
            {
                var sum = 0.0;

                for (var u = 0; u < width; u++)
                {
                    var alphaU = (u < AlphaValues.Length) ? AlphaValues[u] :
                        (u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0);

                    var cosVal = (u < 8 && x < 8) ? CosineTableX[u, x] :
                        Math.Cos(((2.0 * x + 1.0) * u * Math.PI) / (2.0 * width));

                    sum += coeffs[u, v] * alphaU * cosVal;
                }

                temp[x, v] = sum;
            }
        });

        Parallel.For(0, width, x =>
        {
            for (var y = 0; y < height; y++)
            {
                var sum = 0.0;

                for (var v = 0; v < height; v++)
                {
                    var alphaV = (v < AlphaValues.Length) ? AlphaValues[v] :
                        (v == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0);

                    var cosVal = (v < 8 && y < 8) ? CosineTableY[v, y] :
                        Math.Cos(((2.0 * y + 1.0) * v * Math.PI) / (2.0 * height));

                    sum += temp[x, v] * alphaV * cosVal;
                }

                output[x, y] = sum * beta;
            }
        });
    }
}