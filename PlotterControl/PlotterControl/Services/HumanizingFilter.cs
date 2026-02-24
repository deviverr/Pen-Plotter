using System;
using System.Collections.Generic;
using PlotterControl.Models;

namespace PlotterControl.Services
{
    /// <summary>
    /// Applies human-like imperfections (micro-jitter, baseline wobble) to drawing paths.
    /// Only drawing paths are modified; travel moves are left untouched.
    /// </summary>
    public class HumanizingFilter
    {
        /// <summary>
        /// Apply all humanization effects to a list of drawing paths.
        /// Returns a new list with modified drawing paths; input list is not modified.
        /// </summary>
        public List<DrawingPath> Apply(List<DrawingPath> paths, HumanizationSettings settings)
        {
            if (paths == null || paths.Count == 0) return paths;
            if (settings == null || !settings.Enabled) return paths;

            var rng = settings.RandomSeed >= 0
                ? new Random(settings.RandomSeed)
                : new Random();

            // Determine the total X extent of all drawing strokes to parameterize baseline wobble.
            double xMin = double.MaxValue, xMax = double.MinValue;
            foreach (var path in paths)
            {
                if (path.IsTravelMove) continue;
                foreach (var pt in path.Points)
                {
                    if (pt.X < xMin) xMin = pt.X;
                    if (pt.X > xMax) xMax = pt.X;
                }
            }
            // Guard: if drawing is empty or nearly zero width, use a unit range.
            double xRange = (xMax > xMin) ? (xMax - xMin) : 1.0;

            var result = new List<DrawingPath>(paths.Count);

            // Jitter state for the spatial correlation tremor model.
            // Each step: jitter = 0.6 * prev_jitter + 0.4 * new_random
            // This makes the noise move smoothly (correlated) rather than jumping randomly.
            double jitterX = 0.0, jitterY = 0.0;

            foreach (var path in paths)
            {
                // Travel moves are not modified (they don't draw anything).
                if (path.IsTravelMove || path.Points.Count == 0)
                {
                    result.Add(path);
                    continue;
                }

                var newPath = new DrawingPath { IsTravelMove = false };

                // --- Per-stroke micro-rotation (+-0.8 degrees around centroid) ---
                double rotationRad = (rng.NextDouble() - 0.5) * 2.0 * 0.8 * Math.PI / 180.0;
                double cx = 0, cy = 0;
                foreach (var pt in path.Points) { cx += pt.X; cy += pt.Y; }
                cx /= path.Points.Count;
                cy /= path.Points.Count;
                double cosR = Math.Cos(rotationRad);
                double sinR = Math.Sin(rotationRad);

                for (int i = 0; i < path.Points.Count; i++)
                {
                    var pt = path.Points[i];

                    // Apply rotation around centroid
                    double rx = cx + (pt.X - cx) * cosR - (pt.Y - cy) * sinR;
                    double ry = cy + (pt.X - cx) * sinR + (pt.Y - cy) * cosR;

                    // --- Micro-jitter (spatially correlated tremor model) ---
                    if (settings.JitterMm > 0f)
                    {
                        double newJx = (rng.NextDouble() - 0.5) * 2.0 * settings.JitterMm;
                        double newJy = (rng.NextDouble() - 0.5) * 2.0 * settings.JitterMm;
                        jitterX = 0.6 * jitterX + 0.4 * newJx;
                        jitterY = 0.6 * jitterY + 0.4 * newJy;
                    }

                    // --- Baseline wobble (sinusoidal Y drift based on X position) ---
                    double wobble = 0.0;
                    if (settings.BaselineWobbleMm > 0f)
                    {
                        double phase = (rx - xMin) / xRange * Math.PI * 2.5;
                        wobble = Math.Sin(phase) * settings.BaselineWobbleMm;
                    }

                    double finalX = rx + jitterX;
                    double finalY = ry + jitterY + wobble;

                    // --- Stroke endpoint overshoot/undershoot ---
                    // Shift first/last 2 points along stroke direction (bias toward undershoot)
                    if (path.Points.Count >= 3)
                    {
                        double shiftMm = (rng.NextDouble() - 0.6) * 0.6; // +-0.3mm, biased toward undershoot
                        if (i < 2)
                        {
                            // Shift along direction from first to second point
                            var p0 = path.Points[0];
                            var p1 = path.Points[Math.Min(2, path.Points.Count - 1)];
                            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
                            double len = Math.Sqrt(dx * dx + dy * dy);
                            if (len > 1e-6) { finalX += shiftMm * dx / len; finalY += shiftMm * dy / len; }
                        }
                        else if (i >= path.Points.Count - 2)
                        {
                            // Shift along direction from second-to-last to last point
                            var pN = path.Points[path.Points.Count - 1];
                            var pN1 = path.Points[Math.Max(0, path.Points.Count - 3)];
                            double dx = pN.X - pN1.X, dy = pN.Y - pN1.Y;
                            double len = Math.Sqrt(dx * dx + dy * dy);
                            if (len > 1e-6) { finalX += shiftMm * dx / len; finalY += shiftMm * dy / len; }
                        }
                    }

                    newPath.Points.Add(new PlotterPoint(finalX, finalY, pt.Z));
                }

                result.Add(newPath);
            }

            return result;
        }
    }
}
