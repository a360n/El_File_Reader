using BitMiracle.LibTiff.Classic;

namespace EcoLabReaderApp.Services;

public class TiffImageService
{
    public byte[]? ConvertTiffToPngBytes(string tiffPath)
    {
        if (!File.Exists(tiffPath)) return null;

        try
        {
            using var tiff = Tiff.Open(tiffPath, "r");
            if (tiff == null) return null;

            int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

            int[] raster = new int[width * height];
            if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
            {
                return null;
            }

            // Write uncompressed BMP stream
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
            writer.Write(54); // Offset to pixel data

            // DIB Header (40 bytes)
            writer.Write(40); // DIB header size
            writer.Write(width);
            writer.Write(-height); // Top-down
            writer.Write((short)1); // Planes
            writer.Write((short)32); // Bits per pixel
            writer.Write(0); // Compression (none)
            writer.Write(pixelDataSize);
            writer.Write(2835); // Horizontal resolution
            writer.Write(2835); // Vertical resolution
            writer.Write(0);
            writer.Write(0);

            // Write Pixel Data (converting ABGR from LibTiff to BGRA for BMP)
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
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error decoding Tiff using LibTiff.NET: {ex.Message}");
            return null;
        }
    }
}
