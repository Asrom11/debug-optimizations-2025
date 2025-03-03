using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using JPEG.Images;
using PixelFormat = JPEG.Images.PixelFormat;

namespace JPEG.Processor;

public class JpegProcessor : IJpegProcessor
{
    public static readonly JpegProcessor Init = new();
    public const int CompressionQuality = 70;
    private const int DCTSize = 8;

    public void Compress(string imagePath, string compressedImagePath)
    {
        using var fileStream = File.OpenRead(imagePath);
        using var bmp = (Bitmap)Image.FromStream(fileStream, false, false);
        var imageMatrix = (Matrix)bmp;

        var compressionResult = Compress(imageMatrix, CompressionQuality);
        compressionResult.Save(compressedImagePath);
    }

    public void Uncompress(string compressedImagePath, string uncompressedImagePath)
    {
        var compressedImage = CompressedImage.Load(compressedImagePath);
        var uncompressedImage = Uncompress(compressedImage);
        using var resultBmp = (Bitmap)uncompressedImage;
        resultBmp.Save(uncompressedImagePath, ImageFormat.Bmp);
    }

    private static CompressedImage Compress(Matrix matrix, int quality = 50)
    {
        var blocksRow = matrix.Width / DCTSize;
        var totalBlockCount = (matrix.Width / DCTSize) * (matrix.Height / DCTSize);
        var allQuantizedBytes = new byte[totalBlockCount * 3 * 64];
        var quantMatrix = GetQuantizationMatrix(quality);

        var channelSelectors = new Func<Pixel, double>[]
        {
            p => p.Y,
            p => p.Cb,
            p => p.Cr
        };

        Parallel.For(0, matrix.Height / DCTSize, blockY =>
        {
            var currentY = blockY * DCTSize;
            var subMatrix = new double[DCTSize, DCTSize];

            for (var blockX = 0; blockX < matrix.Width / DCTSize; blockX++)
            {
                var currentX = blockX * DCTSize;
                var blockIndex = blockY * blocksRow + blockX;
                for (var channelIndex = 0; channelIndex < 3; channelIndex++)
                {
                    GetSubMatrix(matrix, currentY, DCTSize, currentX, DCTSize, channelSelectors[channelIndex], subMatrix);
                    ShiftMatrixValues(subMatrix, -128);
                    var channelFreqs = FFT.FFT2D(subMatrix);
                    var quantizedFreqs = Quantize(channelFreqs, quantMatrix);
                    var zigzag = ZigZagScan(quantizedFreqs);
                    var destinationIndex = blockIndex * 3 * 64 + channelIndex * 64;
                    Array.Copy(zigzag, 0, allQuantizedBytes, destinationIndex, 64);
                }
            }
        });

        long bitsCount;
        Dictionary<BitsWithLength, byte> decodeTable;
        var compressedBytes = HuffmanCodec.Encode(allQuantizedBytes, out decodeTable, out bitsCount);

        return new CompressedImage
        {
            Quality = quality, CompressedBytes = compressedBytes, BitsCount = bitsCount, DecodeTable = decodeTable,
            Height = matrix.Height, Width = matrix.Width
        };
    }

    private static Matrix Uncompress(CompressedImage compressedSnapshot)
    {
            var restoredMatrix = new Matrix(compressedSnapshot.Height, compressedSnapshot.Width);
            var decodedBuffer =
                HuffmanCodec.Decode(compressedSnapshot.CompressedBytes, compressedSnapshot.DecodeTable, compressedSnapshot.BitsCount);

            using var restoreStream = new MemoryStream(decodedBuffer);

            var quantMatrix = GetQuantizationMatrix(compressedSnapshot.Quality);

            var blocksInWidth = compressedSnapshot.Width / DCTSize;
            var rowByteCount = blocksInWidth * 3 * 64;

            var verticalBlocks = new List<(int verticalStart, byte[] rowData)>(compressedSnapshot.Height / DCTSize);
            for (var verticalIndex = 0; verticalIndex < compressedSnapshot.Height / DCTSize; verticalIndex++)
            {
                var verticalOffset = verticalIndex * DCTSize;
                var rowBuffer = new byte[rowByteCount];

                restoreStream.ReadExactly(rowBuffer, 0, rowBuffer.Length);

                verticalBlocks.Add((verticalOffset, rowBuffer));
            }

            Parallel.ForEach(verticalBlocks, blockSample =>
            {
                var verticalPosition = blockSample.verticalStart;
                var localQuantData = blockSample.rowData;
                var horizontalBlockCount = compressedSnapshot.Width / DCTSize;

                for (var horizontalIndex = 0; horizontalIndex < horizontalBlockCount; horizontalIndex++)
                {
                    var horizontalPosition = horizontalIndex * DCTSize;
                    var channelBlockOffset = horizontalIndex * 3 * 64;


                    var channelData = new[]
                    {
                        new double[DCTSize, DCTSize],
                        new double[DCTSize, DCTSize],
                        new double[DCTSize, DCTSize]
                    };


                    for (var chanIdx = 0; chanIdx < 3; chanIdx++)
                    {
                        var chanDataStart = channelBlockOffset + chanIdx * 64;
                        var chanSpan = localQuantData.AsSpan(chanDataStart, 64);

                        var freqMatrix = ZigZagUnScan(chanSpan);
                        DeQuantize(freqMatrix, quantMatrix);
                        FFT.IFFT2D(freqMatrix, channelData[chanIdx]);
                        ShiftMatrixValues(channelData[chanIdx], 128);
                    }


                    SetPixels(restoredMatrix, channelData[0], channelData[1], channelData[2],
                              verticalPosition, horizontalPosition, PixelFormat.YCbCr);
                }
            });

            return restoredMatrix;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ShiftMatrixValues(double[,] subMatrix, int shiftValue)
    {
        var height = subMatrix.GetLength(0);
        var width = subMatrix.GetLength(1);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            subMatrix[y, x] = subMatrix[y, x] + shiftValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetPixels(Matrix matrix, double[,] a, double[,] b, double[,] c, int yOffset, int xOffset, PixelFormat format)
    {
        for (var y = 0; y < DCTSize; y++)
        {
            for (var x = 0; x < DCTSize; x++)
            {
                matrix.Pixels[yOffset + y, xOffset + x] = new Pixel(a[y, x], b[y, x], c[y, x], format);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetSubMatrix(Matrix matrix, int yOffset, int yLength, int xOffset, int xLength,
        Func<Pixel, double> componentSelector, double[,] result)
    {
        for (var j = 0; j < yLength; j++)
        {
            for (var i = 0; i < xLength; i++)
            {
                result[j, i] = componentSelector(matrix.Pixels[yOffset + j, xOffset + i]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] ZigZagScan(byte[,] channelFreqs)
    {
        return
        [
            channelFreqs[0,0], channelFreqs[0,1], channelFreqs[1,0], channelFreqs[2,0],
            channelFreqs[1,1], channelFreqs[0,2], channelFreqs[0,3], channelFreqs[1,2],
            channelFreqs[2,1], channelFreqs[3,0], channelFreqs[4,0], channelFreqs[3,1],
            channelFreqs[2,2], channelFreqs[1,3], channelFreqs[0,4], channelFreqs[0,5],
            channelFreqs[1,4], channelFreqs[2,3], channelFreqs[3,2], channelFreqs[4,1],
            channelFreqs[5,0], channelFreqs[6,0], channelFreqs[5,1], channelFreqs[4,2],
            channelFreqs[3,3], channelFreqs[2,4], channelFreqs[1,5], channelFreqs[0,6],
            channelFreqs[0,7], channelFreqs[1,6], channelFreqs[2,5], channelFreqs[3,4],
            channelFreqs[4,3], channelFreqs[5,2], channelFreqs[6,1], channelFreqs[7,0],
            channelFreqs[7,1], channelFreqs[6,2], channelFreqs[5,3], channelFreqs[4,4],
            channelFreqs[3,5], channelFreqs[2,6], channelFreqs[1,7], channelFreqs[2,7],
            channelFreqs[3,6], channelFreqs[4,5], channelFreqs[5,4], channelFreqs[6,3],
            channelFreqs[7,2], channelFreqs[7,3], channelFreqs[6,4], channelFreqs[5,5],
            channelFreqs[4,6], channelFreqs[3,7], channelFreqs[4,7], channelFreqs[5,6],
            channelFreqs[6,5], channelFreqs[7,4], channelFreqs[7,5], channelFreqs[6,6],
            channelFreqs[5,7], channelFreqs[6,7], channelFreqs[7,6], channelFreqs[7,7]
        ];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double[,] ZigZagUnScan(Span<byte> quantizedBytes)
    {
        return new double[,]
        {
            {
                quantizedBytes[0], quantizedBytes[1], quantizedBytes[5], quantizedBytes[6],
                quantizedBytes[14], quantizedBytes[15], quantizedBytes[27], quantizedBytes[28]
            },
            {
                quantizedBytes[2], quantizedBytes[4], quantizedBytes[7], quantizedBytes[13],
                quantizedBytes[16], quantizedBytes[26], quantizedBytes[29], quantizedBytes[42]
            },
            {
                quantizedBytes[3], quantizedBytes[8], quantizedBytes[12], quantizedBytes[17],
                quantizedBytes[25], quantizedBytes[30], quantizedBytes[41], quantizedBytes[43]
            },
            {
                quantizedBytes[9], quantizedBytes[11], quantizedBytes[18], quantizedBytes[24],
                quantizedBytes[31], quantizedBytes[40], quantizedBytes[44], quantizedBytes[53]
            },
            {
                quantizedBytes[10], quantizedBytes[19], quantizedBytes[23], quantizedBytes[32],
                quantizedBytes[39], quantizedBytes[45], quantizedBytes[52], quantizedBytes[54]
            },
            {
                quantizedBytes[20], quantizedBytes[22], quantizedBytes[33], quantizedBytes[38],
                quantizedBytes[46], quantizedBytes[51], quantizedBytes[55], quantizedBytes[60]
            },
            {
                quantizedBytes[21], quantizedBytes[34], quantizedBytes[37], quantizedBytes[47],
                quantizedBytes[50], quantizedBytes[56], quantizedBytes[59], quantizedBytes[61]
            },
            {
                quantizedBytes[35], quantizedBytes[36], quantizedBytes[48], quantizedBytes[49],
                quantizedBytes[57], quantizedBytes[58], quantizedBytes[62], quantizedBytes[63]
            }
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[,] Quantize(double[,] channelFreqs, int[,] quantizationMatrix)
    {
        var height = channelFreqs.GetLength(0);
        var width = channelFreqs.GetLength(1);
        var result = new byte[height, width];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            result[y, x] = (byte)(channelFreqs[y, x] / quantizationMatrix[y, x]);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DeQuantize(double[,] freqArray, int[,] quantizationMatrix)
    {
        var height = freqArray.GetLength(0);
        var width = freqArray.GetLength(1);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            freqArray[y, x] = (sbyte)freqArray[y, x] * quantizationMatrix[y, x];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int[,] GetQuantizationMatrix(int quality)
    {
        if (quality is < 1 or > 99)
            throw new ArgumentException("вне интервала");

        var multiplier = quality < 50 ? 5000 / quality : 200 - 2 * quality;

        var result = new[,]
        {
            {16, 11, 10, 16, 24, 40, 51, 61},
            {12, 12, 14, 19, 26, 58, 60, 55},
            {14, 13, 16, 24, 40, 57, 69, 56},
            {14, 17, 22, 29, 51, 87, 80, 62},
            {18, 22, 37, 56, 68, 109, 103, 77},
            {24, 35, 55, 64, 81, 104, 113, 92},
            {49, 64, 78, 87, 103, 121, 120, 101},
            {72, 92, 95, 98, 112, 100, 103, 99}
        };

        for (var y = 0; y < result.GetLength(0); y++)
        for (var x = 0; x < result.GetLength(1); x++)
            result[y, x] = (multiplier * result[y, x] + 50) / 100;

        return result;
    }
}