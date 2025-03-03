using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace JPEG;

public class FFT
{
    private static readonly double[] AlphaValues;

    static FFT()
    {
        const int size = 8;
        AlphaValues = new double[size];

        for (var u = 0; u < size; u++)
        {
            AlphaValues[u] = u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;
        }
    }

    public static double[,] FFT2D(double[,] input)
    {
        var height = input.GetLength(0);
        var width = input.GetLength(1);

        var output = new double[width, height];

        var blockCoordinates = new System.Collections.Concurrent.ConcurrentBag<(int blockX, int blockY)>();

        for (var blockY = 0; blockY < height; blockY += 8)
        {
            for (var blockX = 0; blockX < width; blockX += 8)
            {
                blockCoordinates.Add((blockX, blockY));
            }
        }

        Parallel.ForEach(blockCoordinates, coords =>
        {
            var blockX = coords.blockX;
            var blockY = coords.blockY;

            var block = new Complex[8, 8];

            var maxY = Math.Min(blockY + 8, height);
            var maxX = Math.Min(blockX + 8, width);

            for (var y = blockY; y < maxY; y++)
            {
                for (var x = blockX; x < maxX; x++)
                {
                    block[y - blockY, x - blockX] = new Complex(input[x, y], 0);
                }
            }

            // Применяем FFT к блоку
            var blockFFT = FastFourierTransform2D(block);

            for (var v = 0; v < 8; v++)
            {
                var alphaV = AlphaValues[v];

                for (var u = 0; u < 8; u++)
                {
                    var alphaU = AlphaValues[u];
                    var scaleFactor = alphaU * alphaV / 4.0;

                    if (blockX + u < width && blockY + v < height)
                    {
                        output[blockX + u, blockY + v] = blockFFT[v, u].Real * scaleFactor;
                    }
                }
            }
        });

        return output;
    }

    public static void IFFT2D(double[,] coeffs, double[,] output)
    {
        var height = coeffs.GetLength(0);
        var width = coeffs.GetLength(1);

        var blockCoordinates = new System.Collections.Concurrent.ConcurrentBag<(int blockX, int blockY)>();

        for (var blockY = 0; blockY < height; blockY += 8)
        {
            for (var blockX = 0; blockX < width; blockX += 8)
            {
                blockCoordinates.Add((blockX, blockY));
            }
        }

        Parallel.ForEach(blockCoordinates, coords =>
        {
            var blockX = coords.blockX;
            var blockY = coords.blockY;

            var block = new Complex[8, 8];

            var maxY = Math.Min(blockY + 8, height);
            var maxX = Math.Min(blockX + 8, width);

            for (int v = 0; v < 8; v++)
            {
                var alphaV = AlphaValues[v];

                for (var u = 0; u < 8; u++)
                {
                    var alphaU = AlphaValues[u];

                    if (blockY + v >= height || blockX + u >= width)
                    {
                        continue;
                    }

                    var coeff = coeffs[blockX + u, blockY + v] / (alphaU * alphaV / 4.0);
                    block[v, u] = new Complex(coeff, 0);
                }
            }

            var blockIFFT = FastFourierTransform2D(block, true);

            lock (output)
            {
                for (var y = blockY; y < maxY; y++)
                {
                    for (var x = blockX; x < maxX; x++)
                    {
                        output[x, y] = blockIFFT[y - blockY, x - blockX].Real;
                    }
                }
            }
        });
    }

    private static Complex[,] FastFourierTransform2D(Complex[,] input, bool inverse = false)
    {
        var height = input.GetLength(0);
        var width = input.GetLength(1);

        var result = new Complex[height, width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                result[y, x] = input[y, x];
            }
        }

        // 1. FFT для каждой строки (параллельно)
        Parallel.For(0, height, y =>
        {
            var row = new Complex[width];
            for (var x = 0; x < width; x++)
            {
                row[x] = result[y, x];
            }

            FFT1D(row, inverse);

            for (var x = 0; x < width; x++)
            {
                result[y, x] = row[x];
            }
        });

        // 2. FFT для каждого столбца (параллельно)
        Parallel.For(0, width, x =>
        {
            var column = new Complex[height];
            for (var y = 0; y < height; y++)
            {
                column[y] = result[y, x];
            }

            FFT1D(column, inverse);

            for (var y = 0; y < height; y++)
            {
                result[y, x] = column[y];
            }
        });

        return result;
    }

    // Итеративная реализация одномерного FFT для случая, когда размер равен степени 2
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FFT1D(Complex[] data, bool inverse)
    {
        var n = data.Length;
        var j = 0;

        for (var i = 0; i < n - 1; i++)
        {
            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }

            var k = n >> 1;
            while (k <= j)
            {
                j -= k;
                k >>= 1;
            }
            j += k;
        }

        // Вычисление FFT
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = (inverse ? 2 : -2) * Math.PI / len;
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var i = 0; i < n; i += len)
            {
                var w = new Complex(1, 0);

                for (j = 0; j < len / 2; j++)
                {
                    var u = data[i + j];
                    var v = data[i + j + len / 2] * w;

                    data[i + j] = u + v;
                    data[i + j + len / 2] = u - v;

                    w *= wLen;
                }
            }
        }

        if (!inverse) return;

        for (var i = 0; i < n; i++)
        {
            data[i] /= n;
        }
    }
}