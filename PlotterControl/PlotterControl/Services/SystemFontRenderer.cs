using System;
using System.Collections.Generic;
using System.Linq;
using PlotterControl.Models;
using PlotterControl.Utils;
using SkiaSharp;

namespace PlotterControl.Services
{
    public class SystemFontRenderer
    {
        /// <summary>
        /// Get list of available system font family names.
        /// </summary>
        public List<string> GetSystemFontNames()
        {
            var mgr = SKFontManager.Default;
            var names = new HashSet<string>();
            foreach (var family in mgr.FontFamilies)
            {
                names.Add(family);
            }
            return names.OrderBy(n => n).ToList();
        }

        /// <summary>
        /// Render text using a system font into DrawingPath list.
        /// Uses SKPaint.GetTextPath() to extract outlines, then flattens curves.
        /// </summary>
        public List<DrawingPath> RenderSystemFont(string text, string fontFamily, double fontSize,
            double penDownZ, double penUpZ)
        {
            var paths = new List<DrawingPath>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fontFamily)) return paths;

            using var typeface = SKTypeface.FromFamilyName(fontFamily);
            if (typeface == null)
            {
                Logger.Warning($"System font '{fontFamily}' not found.");
                return paths;
            }

            using var paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = (float)fontSize,
                IsAntialias = false,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.5f
            };

            // Split text by lines
            var lines = text.Split('\n');
            double yOffset = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    yOffset += fontSize * 1.5;
                    continue;
                }

                using var textPath = paint.GetTextPath(line, 0, (float)(yOffset + fontSize));

                if (textPath == null || textPath.IsEmpty)
                {
                    yOffset += fontSize * 1.5;
                    continue;
                }

                // Iterate path verbs and flatten cubic beziers
                using var iter = textPath.CreateIterator(false);
                var points = new SKPoint[4];
                DrawingPath currentPath = null;

                SKPathVerb verb;
                while ((verb = iter.Next(points)) != SKPathVerb.Done)
                {
                    switch (verb)
                    {
                        case SKPathVerb.Move:
                            // Start new sub-path - travel to this point
                            if (currentPath != null && currentPath.Points.Count > 0)
                                paths.Add(currentPath);

                            var travel = new DrawingPath { IsTravelMove = true };
                            travel.Points.Add(new PlotterPoint(points[0].X, points[0].Y, penUpZ));
                            paths.Add(travel);

                            currentPath = new DrawingPath();
                            currentPath.Points.Add(new PlotterPoint(points[0].X, points[0].Y, penDownZ));
                            break;

                        case SKPathVerb.Line:
                            currentPath?.Points.Add(new PlotterPoint(points[1].X, points[1].Y, penDownZ));
                            break;

                        case SKPathVerb.Quad:
                            // Flatten quadratic bezier
                            if (currentPath != null)
                            {
                                var lastPt = currentPath.Points.Last();
                                FlattenQuadBezier(currentPath, lastPt.X, lastPt.Y,
                                    points[1].X, points[1].Y,
                                    points[2].X, points[2].Y, penDownZ);
                            }
                            break;

                        case SKPathVerb.Cubic:
                            // Flatten cubic bezier
                            if (currentPath != null)
                            {
                                var lastPt2 = currentPath.Points.Last();
                                FlattenCubicBezier(currentPath, lastPt2.X, lastPt2.Y,
                                    points[1].X, points[1].Y,
                                    points[2].X, points[2].Y,
                                    points[3].X, points[3].Y, penDownZ);
                            }
                            break;

                        case SKPathVerb.Close:
                            if (currentPath != null && currentPath.Points.Count > 1)
                            {
                                // Close by going back to first point
                                var first = currentPath.Points[0];
                                currentPath.Points.Add(new PlotterPoint(first.X, first.Y, penDownZ));
                            }
                            break;
                    }
                }

                if (currentPath != null && currentPath.Points.Count > 0)
                    paths.Add(currentPath);

                yOffset += fontSize * 1.5;
            }

            return paths;
        }

        private void FlattenQuadBezier(DrawingPath path, double x0, double y0,
            double cx, double cy, double x1, double y1, double z, int segments = 24)
        {
            for (int i = 1; i <= segments; i++)
            {
                double t = (double)i / segments;
                double mt = 1 - t;
                double x = mt * mt * x0 + 2 * mt * t * cx + t * t * x1;
                double y = mt * mt * y0 + 2 * mt * t * cy + t * t * y1;
                path.Points.Add(new PlotterPoint(x, y, z));
            }
        }

        private void FlattenCubicBezier(DrawingPath path, double x0, double y0,
            double cx1, double cy1, double cx2, double cy2, double x1, double y1,
            double z, int segments = 32)
        {
            for (int i = 1; i <= segments; i++)
            {
                double t = (double)i / segments;
                double mt = 1 - t;
                double x = mt * mt * mt * x0 + 3 * mt * mt * t * cx1 + 3 * mt * t * t * cx2 + t * t * t * x1;
                double y = mt * mt * mt * y0 + 3 * mt * mt * t * cy1 + 3 * mt * t * t * cy2 + t * t * t * y1;
                path.Points.Add(new PlotterPoint(x, y, z));
            }
        }
    }
}
