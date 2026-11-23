using System.Globalization;
using OpenCvSharp;

namespace Clipper98;

/// <summary>Melacak wajah utama pada sampel 1 fps dan membuat ekspresi crop yang bergerak halus.</summary>
public sealed record SmartCropSegment(double Start, double End);
public sealed record SmartCropAnalysis(
    bool IsAvailable,
    bool HasDetectedFace,
    string? CropExpression,
    IReadOnlyList<SmartCropSegment> FullFrameSegments);

public sealed class SmartCropAnalyzer
{
    private const int MinimumNoFaceSamples = 2;

    public async Task<SmartCropAnalysis> AnalyzeAsync(string videoFile, string workDir, CancellationToken ct)
    {
        try
        {
            var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
            var model = Path.Combine(AppContext.BaseDirectory, "tools", "models", "haarcascade_frontalface_default.xml");
            if (!File.Exists(model)) return Unavailable();

            var sampleDir = Path.Combine(workDir, "face-samples");
            Directory.CreateDirectory(sampleDir);
            var template = Path.Combine(sampleDir, "frame-%04d.jpg");
            var extract = await ProcessRunner.RunAsync(
                ffmpeg,
                ["-hide_banner", "-loglevel", "error", "-y", "-i", videoFile, "-vf", "fps=1,scale=640:-2", "-q:v", "4", template],
                workDir, null, ct);
            if (extract.ExitCode != 0) return Unavailable();

            var files = Directory.EnumerateFiles(sampleDir, "frame-*.jpg").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) return Unavailable();

            using var detector = new CascadeClassifier(model);
            if (detector.Empty()) return Unavailable();
            var samples = new List<(double Time, double Center)>();
            var detectedCenters = new double?[files.Count];
            double? previousCenter = null;
            double sourceWidth = 0;
            double sourceHeight = 0;

            for (var index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                using var image = Cv2.ImRead(files[index], ImreadModes.Grayscale);
                if (image.Empty()) continue;
                sourceWidth = image.Width;
                sourceHeight = image.Height;
                var faces = detector.DetectMultiScale(image, 1.12, 4, HaarDetectionTypes.ScaleImage, new OpenCvSharp.Size(36, 36));
                if (faces.Length == 0) continue;

                Rect selected;
                if (previousCenter is null)
                {
                    selected = faces.OrderByDescending(face => face.Width * face.Height).First();
                }
                else
                {
                    selected = faces.OrderByDescending(face =>
                    {
                        var center = (face.X + face.Width / 2.0) / image.Width;
                        var area = face.Width * face.Height;
                        return area / (1.0 + Math.Abs(center - previousCenter.Value) * 5.0);
                    }).First();
                }

                var faceCenter = Math.Clamp((selected.X + selected.Width / 2.0) / image.Width, 0.0, 1.0);
                previousCenter = faceCenter;
                detectedCenters[index] = faceCenter;
                samples.Add((index, faceCenter));
            }

            var fullFrameSegments = BuildNoFaceSegments(detectedCenters);
            if (samples.Count == 0)
                return new SmartCropAnalysis(true, false, null, fullFrameSegments);
            if (sourceHeight <= 0) return Unavailable();
            var scaledWidth = Math.Max(1080.0, sourceWidth / sourceHeight * 1920.0);
            var maxX = Math.Max(0.0, scaledWidth - 1080.0);
            if (maxX <= 1)
                return new SmartCropAnalysis(true, true, null, fullFrameSegments);

            // Interpolate missing detections and smooth small movement while quickly catching large shifts.
            var points = InterpolateMissing(samples, files.Count);
            for (var i = 1; i < points.Count; i++)
            {
                var delta = Math.Abs(points[i] - points[i - 1]);
                var responsiveness = delta >= 0.18 ? 0.84 : delta >= 0.07 ? 0.58 : 0.32;
                points[i] = points[i - 1] * (1 - responsiveness) + points[i] * responsiveness;
            }

            return new SmartCropAnalysis(true, true, BuildCropExpression(points), fullFrameSegments);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // If analysis itself is unavailable, keep the legacy centered-crop fallback.
            return Unavailable();
        }
    }

    private static SmartCropAnalysis Unavailable() => new(false, false, null, []);

    private static IReadOnlyList<SmartCropSegment> BuildNoFaceSegments(IReadOnlyList<double?> detectedCenters)
    {
        if (detectedCenters.Count > 0 && detectedCenters.All(center => center is null))
            return [new SmartCropSegment(0, detectedCenters.Count)];

        var segments = new List<SmartCropSegment>();
        for (var index = 0; index < detectedCenters.Count;)
        {
            if (detectedCenters[index] is not null) { index++; continue; }
            var start = index;
            while (index < detectedCenters.Count && detectedCenters[index] is null) index++;
            if (index - start >= MinimumNoFaceSamples)
                segments.Add(new SmartCropSegment(start, index));
        }

        // Bridge a one-second detection blip between longer gaps to avoid briefly snapping back to a tight crop.
        for (var index = 1; index < segments.Count;)
        {
            if (segments[index].Start - segments[index - 1].End <= 1)
            {
                segments[index - 1] = segments[index - 1] with { End = segments[index].End };
                segments.RemoveAt(index);
            }
            else index++;
        }
        return segments;
    }

    private static List<double> InterpolateMissing(IReadOnlyList<(double Time, double Center)> samples, int count)
    {
        var points = new List<double>(count);
        for (var frame = 0; frame < count; frame++)
        {
            var beforeIndex = -1;
            var afterIndex = -1;
            for (var i = 0; i < samples.Count; i++)
            {
                if (samples[i].Time <= frame) beforeIndex = i;
                if (samples[i].Time >= frame) { afterIndex = i; break; }
            }
            if (beforeIndex < 0) points.Add(afterIndex < 0 ? 0.5 : samples[afterIndex].Center);
            else if (afterIndex < 0 || samples[afterIndex].Time == samples[beforeIndex].Time) points.Add(samples[beforeIndex].Center);
            else
            {
                var before = samples[beforeIndex];
                var after = samples[afterIndex];
                var ratio = (frame - before.Time) / (after.Time - before.Time);
                points.Add(before.Center + (after.Center - before.Center) * ratio);
            }
        }
        return points;
    }

    private static string BuildCropExpression(IReadOnlyList<double> centers)
    {
        static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        static string X(double center) => $"max(0,min(in_w-out_w,({F(center)}*in_w-out_w/2)))";

        if (centers.Count == 1) return X(centers[0]);

        string Segment(int index)
        {
            var interpolate = $"({F(centers[index])}+({F(centers[index + 1] - centers[index])})*(t-{F(index)}))";
            return $"max(0,min(in_w-out_w,({interpolate}*in_w-out_w/2)))";
        }

        // A linear chain exceeds FFmpeg's expression nesting limit on longer clips.
        // A balanced decision tree keeps nesting logarithmic while preserving 1 fps interpolation.
        string BuildBalanced(int firstSegment, int lastSegment)
        {
            if (firstSegment == lastSegment) return Segment(firstSegment);

            var split = (firstSegment + lastSegment + 1) / 2;
            return $"if(lt(t,{F(split)}),{BuildBalanced(firstSegment, split - 1)},{BuildBalanced(split, lastSegment)})";
        }

        var lastSample = centers.Count - 1;
        return $"if(gte(t,{F(lastSample)}),{X(centers[^1])},{BuildBalanced(0, lastSample - 1)})";
    }
}
