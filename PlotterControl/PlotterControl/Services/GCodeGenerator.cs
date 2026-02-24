// PlotterControl/PlotterControl/Services/GCodeGenerator.cs

using PlotterControl.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// HumanizationSettings is in PlotterControl.Models

namespace PlotterControl.Services
{
    public class GCodeGenerator
    {
        private readonly ConfigManager _configManager;

        public GCodeGenerator(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        /// <summary>
        /// Optimizes the order of drawing paths using a basic Nearest Neighbor algorithm
        /// to minimize travel distance (pen-up moves).
        /// </summary>
        /// <param name="drawingPaths">The list of paths to optimize.</param>
        /// <returns>A new list of optimized drawing paths.</returns>
        public List<DrawingPath> OptimizePaths(List<DrawingPath> drawingPaths)
        {
            if (drawingPaths == null || drawingPaths.Count <= 1)
            {
                return drawingPaths; // No optimization needed
            }

            List<DrawingPath> optimizedPaths = new List<DrawingPath>();
            HashSet<DrawingPath> remainingPaths = new HashSet<DrawingPath>(drawingPaths);

            // Start with the first path as a reference
            DrawingPath currentPath = remainingPaths.First();
            remainingPaths.Remove(currentPath);
            optimizedPaths.Add(currentPath);

            PlotterPoint currentPenPosition = currentPath.Points.Last(); // End of the first path

            while (remainingPaths.Any())
            {
                DrawingPath nearestPath = null;
                double minDistance = double.MaxValue;

                foreach (DrawingPath nextPath in remainingPaths)
                {
                    PlotterPoint startOfNextPath = nextPath.Points.First();
                    double distance = CalculateDistance(currentPenPosition, startOfNextPath);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearestPath = nextPath;
                    }
                }

                if (nearestPath != null)
                {
                    // Add a travel move (pen up) from current position to the start of the nearest path
                    optimizedPaths.Add(new DrawingPath
                    {
                        Points = new List<PlotterPoint> { currentPenPosition, nearestPath.Points.First() },
                        IsTravelMove = true
                    });
                    
                    optimizedPaths.Add(nearestPath);
                    remainingPaths.Remove(nearestPath);
                    currentPenPosition = nearestPath.Points.Last();
                }
                else
                {
                    // Should not happen if remainingPaths is not empty, but as a safeguard
                    break;
                }
            }

            return optimizedPaths;
        }

        /// <summary>
        /// Generates G-code from a list of drawing paths.
        /// </summary>
        /// <param name="drawingPaths">The list of drawing paths, ideally optimized.</param>
        /// <param name="humanSettings">Optional humanization settings for variable feedrate and pen pressure.</param>
        /// <returns>A string containing the generated G-code.</returns>
        public string GenerateGCode(List<DrawingPath> drawingPaths, HumanizationSettings humanSettings = null)
        {
            StringBuilder gcode = new StringBuilder();
            var config = _configManager.CurrentConfig;
            bool humanize = humanSettings?.Enabled == true;

            var rng = humanize
                ? (humanSettings.RandomSeed >= 0 ? new Random(humanSettings.RandomSeed) : new Random())
                : null;

            // Initial setup commands
            gcode.AppendLine("G90 ; Absolute positioning");
            gcode.AppendLine("M84 S0 ; Disable stepper timeout"); // Keep steppers engaged

            PlotterPoint currentPos = new PlotterPoint(0, 0, config.PenUpZ);

            foreach (DrawingPath path in drawingPaths)
            {
                if (path.Points == null || path.Points.Count == 0) continue;

                PlotterPoint firstPoint = path.Points.First();

                if (path.IsTravelMove)
                {
                    // Pen lift: use a slower feedrate for humanization (more natural lift)
                    if (currentPos.Z != config.PenUpZ)
                    {
                        double liftFeedrate = humanize ? Math.Min(config.ZFeedrate, 300.0) : config.ZFeedrate;
                        gcode.AppendLine($"G0 Z{config.PenUpZ:F3} F{liftFeedrate:F0}");
                        currentPos = new PlotterPoint(currentPos.X, currentPos.Y, config.PenUpZ);
                    }
                    // Rapid move to start of next drawing segment
                    gcode.AppendLine($"G0 X{firstPoint.X:F3} Y{firstPoint.Y:F3} F{config.RapidFeedrate:F0}");
                    currentPos = new PlotterPoint(firstPoint.X, firstPoint.Y, currentPos.Z);
                }
                else // Drawing move
                {
                    // Compute pen-down Z for this stroke (with optional per-stroke pressure variation)
                    double penDownZ = config.PenDownZ;
                    if (humanize && humanSettings.PenPressureVariationMm > 0f && rng != null)
                    {
                        double variation = (rng.NextDouble() - 0.5) * 2.0 * humanSettings.PenPressureVariationMm;
                        // Clamp so we never go above PenUpZ (which would lift the pen unintentionally)
                        penDownZ = Math.Min(penDownZ + variation, config.PenUpZ - 0.05);
                    }

                    // Lower pen if needed
                    if (Math.Abs(currentPos.Z - penDownZ) > 0.001)
                    {
                        gcode.AppendLine($"G0 Z{penDownZ:F3} F{config.ZFeedrate:F0}");
                        currentPos = new PlotterPoint(currentPos.X, currentPos.Y, penDownZ);
                    }

                    var points = path.Points;

                    // Move to first point of the stroke
                    if (Math.Abs(currentPos.X - firstPoint.X) > 0.001 || Math.Abs(currentPos.Y - firstPoint.Y) > 0.001)
                    {
                        gcode.AppendLine($"G1 X{firstPoint.X:F3} Y{firstPoint.Y:F3} F{config.DrawFeedrate:F0}");
                        currentPos = new PlotterPoint(firstPoint.X, firstPoint.Y, currentPos.Z);
                    }

                    // Draw the rest of the stroke with speed variation
                    int totalPts = points.Count;
                    for (int i = 1; i < totalPts; i++)
                    {
                        var p = points[i];
                        double feedrate = config.DrawFeedrate;

                        // Variable feedrate: slow down at sharp corners
                        if (humanize && humanSettings.CornerSlowdown < 1.0f && i < totalPts - 1)
                        {
                            double angle = ComputeAngleDegrees(points[i - 1], p, points[i + 1]);
                            double t = angle / 180.0; // 0 = straight, 1 = U-turn
                            feedrate = Lerp(config.DrawFeedrate, config.DrawFeedrate * humanSettings.CornerSlowdown, t);
                        }

                        // Start-of-stroke slowdown (first 3 points: 50% -> 100% ramp)
                        if (humanize && i <= 3)
                        {
                            double rampFactor = 0.5 + 0.5 * (i / 3.0);
                            feedrate *= rampFactor;
                        }
                        // End-of-stroke slowdown (last 3 points: 100% -> 50% ramp)
                        else if (humanize && i >= totalPts - 3)
                        {
                            double remaining = totalPts - 1 - i; // 2,1,0
                            double rampFactor = 0.5 + 0.5 * (remaining / 2.0);
                            feedrate *= rampFactor;
                        }

                        gcode.AppendLine($"G1 X{p.X:F3} Y{p.Y:F3} F{feedrate:F0}");
                        currentPos = new PlotterPoint(p.X, p.Y, currentPos.Z);
                    }
                }
            }

            // Final: pen up, return to origin, restore stepper timeout
            gcode.AppendLine($"G0 Z{config.PenUpZ:F3} F{config.ZFeedrate:F0}");
            gcode.AppendLine($"G0 X0 Y0 F{config.RapidFeedrate:F0}");
            gcode.AppendLine("M84 S10 ; Enable stepper timeout after 10 seconds");

            return gcode.ToString();
        }

        private double CalculateDistance(PlotterPoint p1, PlotterPoint p2)
        {
            return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
        }

        /// <summary>
        /// Computes the angle in degrees between vectors (prev→curr) and (curr→next).
        /// Returns 0 for a straight line, 180 for a U-turn.
        /// </summary>
        private static double ComputeAngleDegrees(PlotterPoint prev, PlotterPoint curr, PlotterPoint next)
        {
            double dx1 = curr.X - prev.X, dy1 = curr.Y - prev.Y;
            double dx2 = next.X - curr.X, dy2 = next.Y - curr.Y;
            double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
            if (len1 < 1e-9 || len2 < 1e-9) return 0.0;
            double dot = (dx1 * dx2 + dy1 * dy2) / (len1 * len2);
            dot = Math.Clamp(dot, -1.0, 1.0);
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>
        /// Estimates plot time from generated G-code by parsing moves and calculating distance/feedrate.
        /// Returns estimated time, total lines, and total distance.
        /// </summary>
        public (TimeSpan estimatedTime, int totalLines, double totalDistanceMm) EstimatePlotTime(string gcode)
        {
            if (string.IsNullOrEmpty(gcode)) return (TimeSpan.Zero, 0, 0);

            var config = _configManager.CurrentConfig;
            var lines = gcode.Split('\n');
            double totalTimeSec = 0;
            double totalDist = 0;
            int commandCount = 0;
            double curX = 0, curY = 0, curZ = config.PenUpZ;
            double curFeedrate = config.RapidFeedrate; // mm/min

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";")) continue;

                // Strip inline comments
                int semi = line.IndexOf(';');
                if (semi >= 0) line = line.Substring(0, semi).Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (!line.StartsWith("G0") && !line.StartsWith("G1")) continue;
                commandCount++;

                double newX = curX, newY = curY, newZ = curZ, newF = curFeedrate;
                ParseGCodeParams(line, ref newX, ref newY, ref newZ, ref newF);

                double dx = newX - curX, dy = newY - curY, dz = newZ - curZ;
                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                totalDist += dist;

                if (newF > 0 && dist > 0.001)
                {
                    totalTimeSec += (dist / newF) * 60.0; // feedrate is mm/min
                }

                curX = newX; curY = newY; curZ = newZ; curFeedrate = newF;
            }

            return (TimeSpan.FromSeconds(totalTimeSec), commandCount, totalDist);
        }

        private static void ParseGCodeParams(string line, ref double x, ref double y, ref double z, ref double f)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.Length < 2) continue;
                char letter = char.ToUpper(part[0]);
                if (double.TryParse(part.Substring(1), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    switch (letter)
                    {
                        case 'X': x = val; break;
                        case 'Y': y = val; break;
                        case 'Z': z = val; break;
                        case 'F': f = val; break;
                    }
                }
            }
        }
    }
}
