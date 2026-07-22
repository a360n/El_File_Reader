using BitMiracle.LibTiff.Classic;

namespace EcoLabReaderApp.Services;

public class TiffImageService
{
    public (byte[]? bytes, string contentType) ConvertTiffToImageBytes(string tiffPath)
    {
        if (!File.Exists(tiffPath)) return (null, "image/png");

        try
        {
            byte[] header = new byte[4];
            using (var fs = new FileStream(tiffPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Read(header, 0, 4) < 2) return (null, "image/png");
            }

            // 1. Check PNG magic number: 0x89, 0x50 ('P'), 0x4E ('N'), 0x47 ('G')
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                return (File.ReadAllBytes(tiffPath), "image/png");
            }

            // 2. Check BMP magic number: 'B', 'M' (0x42, 0x4D)
            if (header[0] == 0x42 && header[1] == 0x4D)
            {
                return (File.ReadAllBytes(tiffPath), "image/bmp");
            }

            // 3. Check JPEG magic number: 0xFF, 0xD8
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                return (File.ReadAllBytes(tiffPath), "image/jpeg");
            }

            // 4. Real TIFF magic: 'I','I' (0x49, 0x49) or 'M','M' (0x4D, 0x4D)
            if ((header[0] == 0x49 && header[1] == 0x49) || (header[0] == 0x4D && header[1] == 0x4D))
            {
                using var tiff = Tiff.Open(tiffPath, "r");
                if (tiff != null)
                {
                    int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                    int height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

                    int[] raster = new int[width * height];
                    if (tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                    {
                        using var ms = new MemoryStream();
                        using var writer = new BinaryWriter(ms);

                        int pixelDataSize = width * height * 4;
                        int fileSize = 54 + pixelDataSize;

                        // BMP Header (14 bytes)
                        writer.Write((byte)'B');
                        writer.Write((byte)'M');
                        writer.Write(fileSize);
                        writer.Write((short)0);
                        writer.Write((short)0);
                        writer.Write(54);

                        // DIB Header (40 bytes)
                        writer.Write(40);
                        writer.Write(width);
                        writer.Write(-height);
                        writer.Write((short)1);
                        writer.Write((short)32);
                        writer.Write(0);
                        writer.Write(pixelDataSize);
                        writer.Write(2835);
                        writer.Write(2835);
                        writer.Write(0);
                        writer.Write(0);

                        for (int i = 0; i < raster.Length; i++)
                        {
                            int rgba = raster[i];
                            byte r = (byte)(rgba & 0xff);
                            byte g = (byte)((rgba >> 8) & 0xff);
                            byte b = (byte)((rgba >> 16) & 0xff);
                            byte a = (byte)((rgba >> 24) & 0xff);

                            writer.Write(b);
                            writer.Write(g);
                            writer.Write(r);
                            writer.Write(a);
                        }

                        writer.Flush();
                        return (ms.ToArray(), "image/bmp");
                    }
                }
            }

            // Fallback
            return (File.ReadAllBytes(tiffPath), "image/png");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing image {tiffPath}: {ex.Message}");
            try
            {
                return (File.ReadAllBytes(tiffPath), "image/png");
            }
            catch
            {
                return (null, "image/png");
            }
        }
    }
}
