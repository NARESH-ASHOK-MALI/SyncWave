// ============================================================================
// ProcessLoopbackCaptureService — Production Class Smoke Test
// ============================================================================
// Instantiates SyncWave.Core.ProcessLoopbackCaptureService directly.
// Subscribes to DataAvailable & CaptureFormatAvailable.
// Measures peak level over 10 seconds while system audio plays.
// Stops and Disposes cleanly.
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using NAudio.Wave;
using SyncWave.Core;

class Program
{
    private static float _currentWindowMaxPeak = 0f;
    private static readonly object _peakLock = new();

    static void Main()
    {
        Console.Title = "ProcessLoopbackCaptureService — Production Class Smoke Test";
        Console.WriteLine("===============================================================================");
        Console.WriteLine("  SyncWave.Core.ProcessLoopbackCaptureService — Direct Smoke Test");
        Console.WriteLine("===============================================================================");
        Console.WriteLine();

        Console.WriteLine("  Testing production class directly (outside MainViewModel)...");
        Console.WriteLine();

        using var captureService = new ProcessLoopbackCaptureService();

        captureService.CaptureFormatAvailable += format =>
        {
            Console.WriteLine($"  [EVENT] CaptureFormatAvailable: {format.SampleRate}Hz, {format.BitsPerSample}-bit, {format.Channels}ch ({format.Encoding})");
        };

        captureService.DataAvailable += (buffer, byteCount) =>
        {
            // Process 32-bit IEEE Float samples
            int floatCount = byteCount / 4;
            float max = 0f;
            for (int i = 0; i < floatCount; i++)
            {
                float sample = BitConverter.ToSingle(buffer, i * 4);
                float abs = Math.Abs(sample);
                if (abs > max) max = abs;
            }

            lock (_peakLock)
            {
                if (max > _currentWindowMaxPeak)
                    _currentWindowMaxPeak = max;
            }
        };

        Console.Write("  Calling captureService.Start()... ");
        captureService.Start();
        Console.WriteLine("OK!");
        Console.WriteLine($"  IsCapturing: {captureService.IsCapturing}");
        Console.WriteLine($"  CaptureFormat: {captureService.CaptureFormat}");
        Console.WriteLine();

        Console.WriteLine("  +-------+---------------+---------------+----------------------------------------+");
        Console.WriteLine("  | TIME  | CAPTURED PEAK | CAPTURED dB   | LEVEL VISUALIZER                       |");
        Console.WriteLine("  +-------+---------------+---------------+----------------------------------------+");

        var sw = Stopwatch.StartNew();
        int totalIterations = 50; // 50 * 200ms = 10.0 seconds

        for (int i = 0; i < totalIterations; i++)
        {
            Thread.Sleep(200);

            float peak;
            lock (_peakLock)
            {
                peak = _currentWindowMaxPeak;
                _currentWindowMaxPeak = 0f;
            }

            float db = peak > 0 ? 20f * MathF.Log10(peak) : -96.0f;
            string bar = DrawMeterBar(peak, 40);

            Console.WriteLine($"  | {sw.Elapsed.TotalSeconds,5:F1}s | {peak,13:F4} | {db,10:F1} dB | {bar} |");
        }

        Console.WriteLine("  +-------+---------------+---------------+----------------------------------------+");
        Console.WriteLine();

        Console.Write("  Calling captureService.Stop()... ");
        captureService.Stop();
        Console.WriteLine("OK! (IsCapturing: " + captureService.IsCapturing + ")");

        Console.Write("  Calling captureService.Dispose()... ");
        captureService.Dispose();
        Console.WriteLine("OK!");

        Console.WriteLine();
        Console.WriteLine("  [SUCCESS] Production ProcessLoopbackCaptureService started, streamed, stopped, and disposed cleanly with 0 exceptions!");
    }

    static string DrawMeterBar(float peak, int width)
    {
        int filled = (int)(Math.Min(peak, 1.0f) * width);
        return new string('█', filled).PadRight(width, '░');
    }
}
