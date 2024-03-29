using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using QPlayer.ViewModels;

namespace QPlayer.Models
{
    /// <summary>
    /// Stores a compact representation of the audio peaks in an audio file for faster waveform rendering.
    /// </summary>
    [Serializable]
    public struct PeakFile
    {
        public const uint FILE_MAGIC = ((byte)'Q') + ((byte)'P' << 8) + ((byte)'e' << 16) + ((byte)'k' << 24);
        public const int FILE_VERSION = 1;
        public const string FILE_EXTENSION = ".qpek";

        /// <summary>
        /// The reduction factor of the highest resolution pyramid.
        /// </summary>
        public const int MIN_REDUCTION = 32;
        /// <summary>
        /// The number of bits to shift the reduction factor by between each pyramid.
        /// </summary>
        public const int REDUCTION_STEP = 1;
        /// <summary>
        /// The minimum number of samples in a pyramid.
        /// </summary>
        public const int MIN_SAMPLES = 64;

        // Metadata
        public uint fileMagic;
        public int fileVersion;
        public long sourceFileLength;
        public DateTime sourceDate;
        public int sourceNameLength;
        public string sourceName;

        // Peak data
        public int fs;  // Sample rate
        public long length;  // The total number of uncompressed mono samples in the source file
        public PeakData[] peakDataPyramid;

        [StructLayout(LayoutKind.Explicit)]
        public struct Sample
        {
            [FieldOffset(0)] public ushort peak;
            [FieldOffset(2)] public ushort rms;

            [FieldOffset(0)] public uint data;
        }

        public struct PeakData
        {
            // How many samples each of the actual samples each sample at this level summarises
            public int reductionFactor;
            public Sample[] samples;
        }
    }

    internal static class PeakFileReader
    {
        /// <summary>
        /// Reads and validates the metadata portion of a <see cref="PeakFile"/>.
        /// 
        /// This method does not close the stream once complete!
        /// </summary>
        /// <param name="stream">The binary stream to parse</param>
        /// <returns>A new <see cref="PeakFile"/> instance parsed from the stream.</returns>
        /// <exception cref="FormatException"></exception>
        public static PeakFile ReadMetadata(Stream stream)
        {
            var peak = new PeakFile();
            try
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

                peak.fileMagic = reader.ReadUInt32();
                if (peak.fileMagic != PeakFile.FILE_MAGIC)
                    throw new FormatException($"Peak file is corrupt! " +
                        $"Peak file magic = 0x{peak.fileMagic:X8}, expected file magic = 0x{PeakFile.FILE_MAGIC:X8}");

                peak.fileVersion = reader.ReadInt32();
                if (peak.fileVersion != PeakFile.FILE_VERSION)
                    throw new FormatException($"Peak file version does not match application version! " +
                        $"Peak file version = {peak.fileVersion}, expected version = {PeakFile.FILE_VERSION}!");

                peak.sourceFileLength = reader.ReadInt64();
                peak.sourceDate = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                peak.sourceNameLength = reader.ReadInt32();
                Span<char> sourceName = stackalloc char[peak.sourceNameLength];
                for (int i = 0; i < peak.sourceNameLength; i++)
                    sourceName[i] = reader.ReadChar();
                peak.sourceName = sourceName.ToString();
            }
            catch (Exception ex)
            {
                throw new FormatException("Error while decoding peak file metadata!", ex);
            }

            return peak;
        }

        /// <summary>
        /// Reads and validates <see cref="PeakFile"/> from a stream.
        /// </summary>
        /// <param name="stream">The binary stream to parse</param>
        /// <returns>A new <see cref="PeakFile"/> instance parsed from the stream.</returns>
        public static PeakFile ReadPeakFile(Stream stream)
        {
            var peak = ReadMetadata(stream);

            using BinaryReader reader = new(stream);
            peak.fs = reader.ReadInt32();
            peak.length = reader.ReadInt64();

            using BrotliStream br = new(stream, CompressionMode.Decompress);
            using BinaryReader compressedReader = new(br);

            peak.peakDataPyramid = new PeakFile.PeakData[compressedReader.ReadInt32()];
            for (int i = 0; i < peak.peakDataPyramid.Length; i++)
            {
                var pyramid = new PeakFile.PeakData();
                pyramid.reductionFactor = compressedReader.ReadInt32();
                pyramid.samples = new PeakFile.Sample[compressedReader.ReadInt64()];
                for (int j = 0; j < pyramid.samples.LongLength; j++)
                {
                    pyramid.samples[j] = new() { data = compressedReader.ReadUInt32() };
                }
                peak.peakDataPyramid[i] = pyramid;
            }

            return peak;
        }

        /// <summary>
        /// Reads and validates <see cref="PeakFile"/> from a stream.
        /// </summary>
        /// <param name="stream">The binary stream to parse</param>
        /// <returns>A new <see cref="PeakFile"/> instance parsed from the stream.</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<PeakFile> ReadPeakFile(string path)
        {
            return await Task.Run(() =>
            {
                using var f = File.OpenRead(path);
                return ReadPeakFile(f);
            });
        }
    }

    internal static class PeakFileWriter
    {
        /// <summary>
        /// Loads a <see cref="PeakFile"/> for the given audio file if it's valid, otherwise generates a 
        /// new <see cref="PeakFile"/> for an audio file if needed. 
        /// </summary>
        /// <param name="path">The path of the audio to process</param>
        /// <returns>The loaded or generated <see cref="PeakFile"/></returns>
        public static async Task<PeakFile> LoadOrGeneratePeakFile(string path)
        {
            string peakFilePath = Path.ChangeExtension(path, PeakFile.FILE_EXTENSION);
            // Check if we actually need to write a new peak file
            if (await CheckForExistingPeakFile(path))
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        using var f = File.OpenRead(peakFilePath);
                        return PeakFileReader.ReadPeakFile(f);
                    }
                    catch (Exception ex)
                    {
                        MainViewModel.Log($"Exception encountered while reading peak file '{path}', it will be regenerated:\n" + ex,
                            MainViewModel.LogLevel.Warning);
                    }
                    return default;
                });
            }

            PeakFile peakFile = default;
            try
            {
                Stopwatch sw = new();
                sw.Start();
                peakFile = await Task.Run(() => GeneratePeakFile(path));
                MainViewModel.Log($"Generated peak file for '{Path.GetFileName(path)}' in {sw.Elapsed:mm\\:ss\\.fff}", MainViewModel.LogLevel.Debug);
                sw.Restart();
                using var f = File.Open(peakFilePath, FileMode.Create, FileAccess.Write);
                await Task.Run(() => WritePeakFile(peakFile, f));
                MainViewModel.Log($"   wrote peak file for '{Path.GetFileName(path)}' in {sw.Elapsed:mm\\:ss\\.fff}", MainViewModel.LogLevel.Debug);
            }
            catch (Exception ex)
            {
                MainViewModel.Log($"Exception encountered while generating peak file for '{Path.GetFileName(path)}':\n" + ex,
                    MainViewModel.LogLevel.Error);
            }

            return peakFile;
        }

        private static async Task<bool> CheckForExistingPeakFile(string path)
        {
            if (!File.Exists(path))
                return false;
            string peakPath = Path.ChangeExtension(path, PeakFile.FILE_EXTENSION);
            if (!File.Exists(peakPath))
                return false;

            try
            {
                using var f = File.OpenRead(peakPath);
                var peakMeta = await Task.Run(() => PeakFileReader.ReadMetadata(f));
                var fileInfo = new FileInfo(path);

                if (peakMeta.sourceName != fileInfo.Name)
                    throw new Exception("Peak file name doesn't match audio file name!");
                if (peakMeta.sourceDate != (fileInfo.CreationTimeUtc > fileInfo.LastWriteTimeUtc ? fileInfo.CreationTimeUtc : fileInfo.LastWriteTimeUtc))
                    throw new Exception("Peak file date doesn't match audio file date!");
                if (peakMeta.sourceFileLength != fileInfo.Length)
                    throw new Exception("Peak file doesn't match");
            }
            catch (FormatException ex)
            {
                MainViewModel.Log($"Exception encountered while reading peak file '{peakPath}', it will be regenerated:\n" + ex,
                    MainViewModel.LogLevel.Warning);
                return false;
            }
            catch (Exception ex)
            {
                MainViewModel.Log($"Peak file for '{path}' was invalid, it will be regenerated:\n" + ex,
                    MainViewModel.LogLevel.Debug);
                return false;
            }

            return true;
        }

        private static PeakFile GeneratePeakFile(string path)
        {
            var peakFile = new PeakFile();

            // Load source file
            var sourceInfo = new FileInfo(path);
            using var sourceAudio = new AudioFileReader(path);

            // Generate metadata...
            peakFile.fileMagic = PeakFile.FILE_MAGIC;
            peakFile.fileVersion = PeakFile.FILE_VERSION;

            peakFile.sourceFileLength = sourceInfo.Length;
            peakFile.sourceDate = (sourceInfo.CreationTimeUtc > sourceInfo.LastWriteTimeUtc ? sourceInfo.CreationTimeUtc : sourceInfo.LastWriteTimeUtc);
            peakFile.sourceNameLength = sourceInfo.Name.Length;
            peakFile.sourceName = sourceInfo.Name;

            peakFile.fs = sourceAudio.WaveFormat.SampleRate;

            // Generate peak data
            List<PeakFile.PeakData> pyramids = new();

            // Generate the peaks for the first pyramid from the audio file
            float[] sourceBuffer = new float[sourceAudio.WaveFormat.Channels * 4 * PeakFile.MIN_REDUCTION];
            int samplesPerSample = sourceBuffer.Length;
            PeakFile.PeakData pyramid = new();
            pyramid.reductionFactor = PeakFile.MIN_REDUCTION;
            List<PeakFile.Sample> samples = new();
            do
            {
                // Read a number of samples to average together
                int read = 0;
                do
                {
                    int lastRead = sourceAudio.Read(sourceBuffer, read, samplesPerSample - read);
                    read += lastRead;
                    if (lastRead == 0)
                        break;
                } while (read < samplesPerSample);
                peakFile.length += read;
                if (read < samplesPerSample)
                    break;

                // Compute the peak and rms of the samples
                float max = 0;
                float sqrSum = 0;
                for (int i = 0; i < sourceBuffer.Length; i++)
                {
                    float s = Math.Abs(sourceBuffer[i]);
                    max = Math.Max(s, max);
                    sqrSum += s * s;
                }
                ushort peak = (ushort)(max * ushort.MaxValue);
                ushort rms = (ushort)(Math.Sqrt(sqrSum / sourceBuffer.Length) * ushort.MaxValue);

                samples.Add(new PeakFile.Sample() { peak = peak, rms = rms });
            } while (true);

            pyramid.samples = samples.ToArray();
            //if (samples.Count >= PeakFile.MIN_SAMPLES)
            pyramids.Add(pyramid);
            peakFile.length /= sourceAudio.WaveFormat.Channels;

            // Now compute all subsequant pyramids from the previous pyramid
            int currReduction = PeakFile.MIN_REDUCTION << PeakFile.REDUCTION_STEP;
            do
            {
                pyramid = new();
                samples.Clear();
                pyramid.reductionFactor = currReduction;
                samplesPerSample = 1 << PeakFile.REDUCTION_STEP;
                var lastPyramid = pyramids[^1].samples;

                int i = 0;
                do
                {
                    float max = 0;
                    float sqrSum = 0;
                    int y = 0;
                    for (y = 0; y < samplesPerSample && i < lastPyramid.Length; y++, i++)
                    {
                        float p = lastPyramid[i].peak * (1 / (float)ushort.MaxValue);
                        float r = lastPyramid[i].rms * (1 / (float)ushort.MaxValue);
                        max = Math.Max(p, max);
                        sqrSum += r * r;
                    }
                    ushort peak = (ushort)(max * ushort.MaxValue);
                    ushort rms = (ushort)(Math.Sqrt(sqrSum / y) * ushort.MaxValue);
                    samples.Add(new PeakFile.Sample() { peak = peak, rms = rms });
                } while (i < lastPyramid.Length);

                currReduction <<= PeakFile.REDUCTION_STEP;
                pyramid.samples = samples.ToArray();
                if (pyramid.samples.Length >= PeakFile.MIN_SAMPLES)
                    pyramids.Add(pyramid);
                else
                    break;
            } while (true);

            peakFile.peakDataPyramid = pyramids.Reverse<PeakFile.PeakData>().ToArray();

            return peakFile;
        }

        private static void WritePeakFile(PeakFile peakFile, Stream stream)
        {
            using BinaryWriter writer = new(stream, Encoding.UTF8);

            writer.Write(peakFile.fileMagic);
            writer.Write(peakFile.fileVersion);

            writer.Write(peakFile.sourceFileLength);
            writer.Write(peakFile.sourceDate.Ticks);
            writer.Write(peakFile.sourceName.Length);
            writer.Write(new ReadOnlySpan<char>(peakFile.sourceName.ToCharArray()));

            writer.Write(peakFile.fs);
            writer.Write(peakFile.length);

            using (BrotliStream br = new(stream, CompressionLevel.Optimal, true))
            using (BinaryWriter compressedWriter = new(br, Encoding.UTF8, true))
            {
                compressedWriter.Write(peakFile.peakDataPyramid.Length);
                for (int i = 0; i < peakFile.peakDataPyramid.Length; i++)
                {
                    var pyramid = peakFile.peakDataPyramid[i];
                    compressedWriter.Write(pyramid.reductionFactor);
                    compressedWriter.Write(pyramid.samples.LongLength);
                    for (int j = 0; j < pyramid.samples.LongLength; j++)
                    {
                        var sample = pyramid.samples[j];
                        compressedWriter.Write(sample.data);
                    }
                }
            }

            writer.Write("\n\nThis is a QPlayer peak file used to accelerate waveform rendering.\n" +
                "https://github.com/space928/QPlayer\n\n<3");
        }
    }
}
