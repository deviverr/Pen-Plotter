using System;
using System.Collections.Generic;
using PlotterControl.Models;
using PlotterControl.Utils;
using SkiaSharp;

namespace PlotterControl.Services
{
    public class ImageProcessor
    {
        public enum DitherMode
        {
            Threshold,
            FloydSteinberg
        }

        public enum DrawMode
        {
            ScanLines,  // Horizontal zigzag lines (dense fills)
            Contours    // Trace outlines of shapes (more natural/artistic look)
        }

        // Maximum image dimension for contour mode to keep performance acceptable.
        private const int MaxContourDim = 500;

        /// <summary>
        /// Load an image, convert to grayscale, resize to fit bed, and generate drawing paths.
        /// </summary>
        public List<DrawingPath> ProcessImage(string filePath, double bedWidth, double bedHeight,
            double lineSpacing, int threshold, DitherMode ditherMode, double penDownZ, double penUpZ,
            DrawMode drawMode = DrawMode.ScanLines)
        {
            SKBitmap original;
            try
            {
                original = SKBitmap.Decode(filePath);
                if (original == null)
                {
                    Logger.Error($"Failed to decode image: {filePath}");
                    return new List<DrawingPath>();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading image: {ex.Message}", ex);
                return new List<DrawingPath>();
            }

            // Calculate size to fit bed while maintaining aspect ratio
            double scaleX = bedWidth / original.Width;
            double scaleY = bedHeight / original.Height;
            double scale = Math.Min(scaleX, scaleY);

            // For contour mode, cap resolution to avoid extreme processing times
            if (drawMode == DrawMode.Contours)
            {
                double contourScale = Math.Min((double)MaxContourDim / original.Width,
                                               (double)MaxContourDim / original.Height);
                scale = Math.Min(scale, contourScale);
            }

            int targetW = Math.Max(1, (int)(original.Width * scale));
            int targetH = Math.Max(1, (int)(original.Height * scale));

            // Resize
            var resized = original.Resize(new SKImageInfo(targetW, targetH), SKFilterQuality.High);
            original.Dispose();

            // Convert to grayscale
            byte[,] gray = new byte[targetH, targetW];
            for (int y = 0; y < targetH; y++)
                for (int x = 0; x < targetW; x++)
                {
                    var pixel = resized.GetPixel(x, y);
                    gray[y, x] = (byte)(0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue);
                }
            resized.Dispose();

            // Apply dithering or threshold to produce binary image (true = dark = draw)
            bool[,] binary = new bool[targetH, targetW];
            if (ditherMode == DitherMode.FloydSteinberg)
            {
                float[,] errors = new float[targetH, targetW];
                for (int y = 0; y < targetH; y++)
                    for (int x = 0; x < targetW; x++)
                        errors[y, x] = gray[y, x];

                for (int y = 0; y < targetH; y++)
                {
                    for (int x = 0; x < targetW; x++)
                    {
                        float oldVal = errors[y, x];
                        float newVal = oldVal < threshold ? 0 : 255;
                        binary[y, x] = newVal == 0;
                        float err = oldVal - newVal;

                        if (x + 1 < targetW) errors[y, x + 1] += err * 7f / 16f;
                        if (y + 1 < targetH)
                        {
                            if (x - 1 >= 0) errors[y + 1, x - 1] += err * 3f / 16f;
                            errors[y + 1, x] += err * 5f / 16f;
                            if (x + 1 < targetW) errors[y + 1, x + 1] += err * 1f / 16f;
                        }
                    }
                }
            }
            else
            {
                for (int y = 0; y < targetH; y++)
                    for (int x = 0; x < targetW; x++)
                        binary[y, x] = gray[y, x] < threshold;
            }

            // Both modes use the same coordinate mapping:
            // targetW pixels across the bed width dimension (bedWidth mm)
            // targetH pixels across the bed height dimension (bedHeight mm)
            if (drawMode == DrawMode.Contours)
            {
                return GenerateContourPaths(binary, targetW, targetH,
                    bedWidth, bedHeight, penDownZ, penUpZ);
            }
            else
            {
                return GenerateScanLinePaths(binary, targetW, targetH,
                    bedWidth, bedHeight, lineSpacing, penDownZ, penUpZ);
            }
        }

        private List<DrawingPath> GenerateScanLinePaths(bool[,] binary, int imgW, int imgH,
            double bedW, double bedH, double lineSpacing, double penDownZ, double penUpZ)
        {
            var paths = new List<DrawingPath>();

            double pixelsPerMM_X = imgW / bedW;
            double pixelsPerMM_Y = imgH / bedH;

            double lineSpacingPx = lineSpacing * pixelsPerMM_Y;
            bool leftToRight = true;

            for (double scanY = 0; scanY < imgH; scanY += lineSpacingPx)
            {
                int py = (int)scanY;
                if (py >= imgH) break;

                double mmY = scanY / pixelsPerMM_Y;
                bool drawing = false;
                double startX = 0;

                int startPx = leftToRight ? 0 : imgW - 1;
                int endPx = leftToRight ? imgW : -1;
                int step = leftToRight ? 1 : -1;

                for (int px = startPx; px != endPx; px += step)
                {
                    bool isDark = binary[py, px];
                    double mmX = (double)px / pixelsPerMM_X;

                    if (isDark && !drawing)
                    {
                        var travel = new DrawingPath { IsTravelMove = true };
                        travel.Points.Add(new PlotterPoint(mmX, mmY, penUpZ));
                        paths.Add(travel);
                        startX = mmX;
                        drawing = true;
                    }
                    else if (!isDark && drawing)
                    {
                        var drawPath = new DrawingPath();
                        drawPath.Points.Add(new PlotterPoint(startX, mmY, penDownZ));
                        drawPath.Points.Add(new PlotterPoint(mmX, mmY, penDownZ));
                        paths.Add(drawPath);
                        drawing = false;
                    }
                }

                if (drawing)
                {
                    double endMmX = (double)(leftToRight ? imgW - 1 : 0) / pixelsPerMM_X;
                    var drawPath = new DrawingPath();
                    drawPath.Points.Add(new PlotterPoint(startX, mmY, penDownZ));
                    drawPath.Points.Add(new PlotterPoint(endMmX, mmY, penDownZ));
                    paths.Add(drawPath);
                }

                leftToRight = !leftToRight;
            }

            return paths;
        }

        /// <summary>
        /// Generate contour strokes by detecting edges in the binary image and tracing
        /// connected chains of edge pixels. Produces an artistic outline-drawing style.
        /// </summary>
        private List<DrawingPath> GenerateContourPaths(bool[,] binary, int imgW, int imgH,
            double bedW, double bedH, double penDownZ, double penUpZ)
        {
            double pixPerMmX = imgW / bedW;
            double pixPerMmY = imgH / bedH;

            // Step 1: Find all edge pixels.
            // An edge pixel is a dark pixel that has at least one light 4-connected neighbor.
            var edgeSet = new HashSet<int>(); // encoded as y*imgW + x
            var edgeList = new List<(int x, int y)>();

            for (int y = 0; y < imgH; y++)
            {
                for (int x = 0; x < imgW; x++)
                {
                    if (!binary[y, x]) continue;
                    bool hasLightNeighbor =
                        (x > 0 && !binary[y, x - 1]) ||
                        (x < imgW - 1 && !binary[y, x + 1]) ||
                        (y > 0 && !binary[y - 1, x]) ||
                        (y < imgH - 1 && !binary[y + 1, x]);

                    if (hasLightNeighbor)
                    {
                        int key = y * imgW + x;
                        edgeSet.Add(key);
                        edgeList.Add((x, y));
                    }
                }
            }

            if (edgeList.Count == 0)
                return new List<DrawingPath>();

            // Step 2: Build adjacency for fast nearest-neighbor lookup.
            // Use a grid-indexed set for O(1) lookup of whether a pixel is an edge.
            // Walk greedily: start from any unvisited edge pixel, always move to the
            // nearest unvisited 8-connected edge neighbor. When no neighbor exists
            // within a jump threshold, start a new stroke (travel move).

            var visited = new bool[imgH, imgW];
            var paths = new List<DrawingPath>();

            // 8-connected neighbor offsets
            var neighborOffsets = new (int dx, int dy)[]
            {
                (0, -1), (1, -1), (1, 0), (1, 1),
                (0, 1), (-1, 1), (-1, 0), (-1, -1)
            };

            // Maximum gap in pixels before we lift the pen (start a travel move).
            // A gap of sqrt(2) means only direct 8-neighbors; 3 allows small gaps.
            const double MaxPenDownGap = 3.0;

            DrawingPath currentDraw = null;
            (int x, int y) currentPx = (-1, -1);

            // Process all edge pixels in scan-line order
            foreach (var startPx in edgeList)
            {
                if (visited[startPx.y, startPx.x]) continue;

                // Start a new chain from this pixel
                (int x, int y) px = startPx;

                while (true)
                {
                    if (visited[px.y, px.x]) break;
                    visited[px.y, px.x] = true;

                    double mmX = px.x / pixPerMmX;
                    double mmY = px.y / pixPerMmY;

                    if (currentDraw == null)
                    {
                        // First point of a new stroke: add travel move
                        var travel = new DrawingPath { IsTravelMove = true };
                        travel.Points.Add(new PlotterPoint(mmX, mmY, penUpZ));
                        paths.Add(travel);

                        currentDraw = new DrawingPath();
                        currentDraw.Points.Add(new PlotterPoint(mmX, mmY, penDownZ));
                    }
                    else
                    {
                        currentDraw.Points.Add(new PlotterPoint(mmX, mmY, penDownZ));
                    }

                    currentPx = px;

                    // Find nearest unvisited 8-connected edge neighbor
                    (int x, int y) best = (-1, -1);
                    double bestDist = double.MaxValue;

                    foreach (var (dx, dy) in neighborOffsets)
                    {
                        int nx = px.x + dx, ny = px.y + dy;
                        if (nx < 0 || nx >= imgW || ny < 0 || ny >= imgH) continue;
                        if (visited[ny, nx]) continue;
                        if (!edgeSet.Contains(ny * imgW + nx)) continue;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < bestDist) { bestDist = dist; best = (nx, ny); }
                    }

                    if (best.x < 0 || bestDist > MaxPenDownGap)
                    {
                        // No direct neighbor — end this stroke
                        if (currentDraw != null && currentDraw.Points.Count >= 2)
                            paths.Add(currentDraw);
                        currentDraw = null;
                        break;
                    }

                    px = best;
                }

                // Commit any pending stroke
                if (currentDraw != null && currentDraw.Points.Count >= 2)
                {
                    paths.Add(currentDraw);
                    currentDraw = null;
                }
            }

            return paths;
        }
    }
}
