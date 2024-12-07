using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using NAudio.Wave;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using QPlayer.Models;

namespace QPlayer.ViewModels;

public class WaveFormRenderer : ObservableObject
{
    [Reactive]
    public PeakFile? PeakFile
    {
        set
        {
            peakFile = value;
            startTime = 0;
            endTime = 1;
            Update();
        }
        get => peakFile;
    }
    [Reactive] public SoundCueViewModel SoundCueViewModel { get; init; }
    [Reactive] public Drawing WaveFormDrawing => drawingGroup;
    [Reactive]
    public double Width
    {
        set { width = (int)value; Update(); }
    }
    [Reactive]
    public double Height
    {
        set { height = (int)value; Update(); }
    }
    [Reactive]
    public TimeSpan ViewStart
    {
        get => TimeSpan.FromSeconds(startTime * (peakFile?.length ?? 0) / (double)(peakFile?.fs ?? 1));
        set { startTime = Math.Clamp(((float)value.TotalSeconds) / ((peakFile?.length ?? 0) / (float)(peakFile?.fs ?? 1)), 0, 1); Update(); }
    }
    [Reactive]
    public TimeSpan ViewEnd
    {
        get => TimeSpan.FromSeconds(endTime * (peakFile?.length ?? 0) / (double)(peakFile?.fs ?? 1));
        set { endTime = Math.Clamp(((float)value.TotalSeconds) / ((peakFile?.length ?? 0) / (float)(peakFile?.fs ?? 1)), 0, 1); Update(); }
    }
    [Reactive]
    public TimeSpan ViewSpan => TimeSpan.FromSeconds((endTime - startTime) * (peakFile?.length ?? 0) / (double)(peakFile?.fs ?? 1));
    [Reactive]
    public TimeSpan Duration => TimeSpan.FromSeconds((peakFile?.length ?? 0) / (double)(peakFile?.fs ?? 1));
    [Reactive, ReactiveDependency(nameof(PeakFile))] public string FileName => peakFile?.sourceName ?? string.Empty;
    [Reactive, ReactiveDependency(nameof(PeakFile))] public string WindowTitle => $"QPlayer - Waveform - {peakFile?.sourceName ?? string.Empty}";

    // Accursed over-abstraction...
    private readonly Brush peakBrush = new SolidColorBrush(Color.FromArgb(200, 10, 90, 255));
    private readonly Pen peakPen;
    private readonly Brush rmsBrush = new SolidColorBrush(Color.FromArgb(200, 10, 30, 220));
    private readonly Pen rmsPen;
    private readonly DrawingGroup drawingGroup;
    private readonly GeometryDrawing geometryDrawingPeak;
    private readonly PathGeometry geometryPeak;
    private readonly PathFigure figurePeak;
    private readonly PolyLineSegment peakPoly;
    private readonly GeometryDrawing geometryDrawingRMS;
    private readonly PathGeometry geometryRMS;
    private readonly PathFigure figureRMS;
    private readonly PolyLineSegment rmsPoly;
    private readonly List<Point> peakPoints = [];
    private readonly List<Point> rmsPoints = [];
    private readonly RectangleGeometry clipGeometry;

    // WPF can be slow, especially with complex shapes, so we limit the maximum number of points displayed here;
    // This only really has an effect on the waveform popup (due to it's large width).
    private const int MAX_DISPLAYED_POINTS = 500;
    // Lowering this lowers the amount of detail in the waveforms, which is good for performance.
    private const float WAVEFORM_DETAIL_FACTOR = 0.75f;

    private PeakFile? peakFile = null;
    private int width = 2;
    private int height = 2;
    private float startTime = 0;
    private float endTime = 1;

    public WaveFormRenderer(SoundCueViewModel soundCue)
    {
        // Accursed over-abstraction... Blame Microsoft...
        peakPen = new(peakBrush, 0);
        figurePeak = new()
        {
            IsClosed = true,
            IsFilled = true
        };
        peakPoly = new PolyLineSegment();
        figurePeak.Segments.Add(peakPoly);

        geometryPeak = new();
        geometryPeak.Figures.Add(figurePeak);
        geometryDrawingPeak = new GeometryDrawing(peakBrush, peakPen, geometryPeak);

        rmsPen = new(rmsBrush, 0);
        figureRMS = new()
        {
            IsClosed = true,
            IsFilled = true
        };
        rmsPoly = new PolyLineSegment();
        figureRMS.Segments.Add(rmsPoly);
        geometryRMS = new();
        geometryRMS.Figures.Add(figureRMS);

        clipGeometry = new(new Rect(0, 0, width, height));

        geometryDrawingRMS = new GeometryDrawing(rmsBrush, rmsPen, geometryRMS);
        drawingGroup = new();
        drawingGroup.Children.Add(geometryDrawingPeak);
        drawingGroup.Children.Add(geometryDrawingRMS);
        drawingGroup.ClipGeometry = clipGeometry;

        SoundCueViewModel = soundCue;

        //WaveFormDrawing = new DrawingImage(drawingGroup);
    }

    public void Update()
    {
        OnPropertyChanged(nameof(ViewSpan));
        OnPropertyChanged(nameof(Duration));
        Render();
    }

    private void Render()
    {
        if (peakFile == null)
            return;

        if (width != clipGeometry.Rect.Width || height != clipGeometry.Rect.Height)
        {
            clipGeometry.Rect = new Rect(0, 0, width, height);
        }

        peakPoints.Clear();
        rmsPoints.Clear();
        figurePeak.StartPoint = new Point(0, height);
        figureRMS.StartPoint = new Point(0, height);
        //peakPoints.Add(new Point(0, height));
        //rmsPoints.Add(new Point(0, height));

        float viewSpan = endTime - startTime;
        // Find a pyramid with slightly fewer points than the width in pixels of the final waveform.
        float targetLength = Math.Min(width * WAVEFORM_DETAIL_FACTOR, MAX_DISPLAYED_POINTS) / viewSpan;
        var buff = peakFile.Value.peakDataPyramid.LastOrDefault(
            x => x.samples.Length <= targetLength,
            peakFile.Value.peakDataPyramid[0]);
        // A value between 0-1 where 1 indicates that we are close to using the full resolution of the pyramid, and 0 means we are close to the lower resolution
        float lodLerp = (buff.samples.Length * 2 - targetLength) / targetLength;
        int sampleStart = Math.Clamp((int)(buff.samples.Length * startTime), 0, buff.samples.Length - 1);
        int sampleEnd = Math.Clamp((int)(buff.samples.Length * endTime), 0, buff.samples.Length - 1);
        int samples = sampleEnd - sampleStart;

        int j = 0;
        float prevPeak = 0;
        float prevRms = 0;
        for (int i = sampleStart; i < sampleEnd + 1; i++)
        {
            float x = (j * width) / (float)samples;
            float peakVal = buff.samples[i].peak / (float)ushort.MaxValue;
            float rmsVal = buff.samples[i].rms / (float)ushort.MaxValue;

            float peakLerped = Lerp(peakVal, MathF.Max(peakVal, prevPeak), lodLerp);
            float rmsLerped = Lerp(rmsVal, (rmsVal + prevRms) * .5f, lodLerp);
            prevPeak = peakVal;
            prevRms = rmsVal;

            float peak = height - MathF.Sqrt(peakLerped) * height;
            float rms = height - MathF.Sqrt(rmsLerped) * height;

            peakPoints.Add(new Point(x, peak));
            rmsPoints.Add(new Point(x, rms));
            j++;
        }

        peakPoints.Add(new Point(width, height));
        rmsPoints.Add(new Point(width, height));

        peakPoly.Points.Clear();
        rmsPoly.Points.Clear();
        peakPoly.Points.Add(peakPoints);
        rmsPoly.Points.Add(rmsPoints);
    }

    private static float Lerp(float a, float b, float t)
    {
        return b * t + a * (1 - t);
    }
}
