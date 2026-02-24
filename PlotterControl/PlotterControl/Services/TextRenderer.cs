// PlotterControl/PlotterControl/Services/TextRenderer.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using PlotterControl.Models; // For Font, Glyph, DrawingPath, PlotterPoint
using PlotterControl.Utils; // For Logger
// HumanizationSettings is in PlotterControl.Models

namespace PlotterControl.Services
{
    public class TextRenderer
    {
        private readonly ConfigManager _configManager;
        private Dictionary<string, Font> _loadedFonts;

        public TextRenderer(ConfigManager configManager)
        {
            _configManager = configManager;
            _loadedFonts = new Dictionary<string, Font>();
        }

        public async Task<Font> LoadFontAsync(string fontName)
        {
            if (_loadedFonts.ContainsKey(fontName))
            {
                return _loadedFonts[fontName];
            }

            string resourcePath = $"PlotterControl.Resources.Fonts.{fontName}.json";
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
                {
                    if (stream == null)
                    {
                        Logger.Error($"Font resource '{resourcePath}' not found.");
                        return null;
                    }
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string jsonString = await reader.ReadToEndAsync();
                        Font font = JsonSerializer.Deserialize<Font>(jsonString);
                        _loadedFonts[fontName] = font;
                        Logger.Info($"Font '{fontName}' loaded successfully.");
                        return font;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading font '{fontName}': {ex.Message}", ex);
                return null;
            }
        }

        public List<DrawingPath> RenderText(string text, Font font, double fontSize,
            HumanizationSettings humanSettings = null)
        {
            List<DrawingPath> drawingPaths = new List<DrawingPath>();
            if (font == null || string.IsNullOrEmpty(text)) return drawingPaths;

            double currentX = 0;
            double currentY = 0;
            double scale = fontSize / 100.0; // Assuming font glyphs are normalized to a 100-unit height

            // Per-character variation state (only active with humanization)
            bool humanize = humanSettings?.Enabled == true;
            var rng = humanize
                ? (humanSettings.RandomSeed >= 0 ? new Random(humanSettings.RandomSeed) : new Random())
                : null;
            double baselineDrift = 0.0; // Accumulated Y drift (clamped to ±0.5mm)

            foreach (char c in text)
            {
                if (c == '\n')
                {
                    currentY += fontSize * 1.5;
                    currentX = 0;
                    baselineDrift = 0.0; // Reset drift on newline
                    continue;
                }

                Glyph glyph = font.Glyphs.FirstOrDefault(g => g.Character == c);

                if (glyph == null)
                {
                    Logger.Warning($"Glyph for character '{c}' not found in font '{font.Name}'.");
                    currentX += fontSize * 0.7;
                    continue;
                }

                // Per-character baseline drift: small incremental Y wander clamped to ±0.5mm
                if (humanize && rng != null)
                {
                    baselineDrift += (rng.NextDouble() - 0.5) * 0.3; // ±0.15mm step
                    baselineDrift = Math.Clamp(baselineDrift, -0.5, 0.5);
                }

                double charBaselineY = currentY + baselineDrift;

                foreach (var pathSegments in glyph.Paths)
                {
                    DrawingPath drawingPath = new DrawingPath();
                    bool firstPoint = true;

                    foreach (PlotterPoint p in pathSegments)
                    {
                        double transformedX = currentX + (p.X * scale);
                        double transformedY = charBaselineY + (p.Y * scale);

                        if (firstPoint)
                        {
                            drawingPaths.Add(new DrawingPath
                            {
                                Points = new List<PlotterPoint> { new PlotterPoint(transformedX, transformedY, _configManager.CurrentConfig.PenUpZ) },
                                IsTravelMove = true
                            });
                            firstPoint = false;
                        }
                        drawingPath.Points.Add(new PlotterPoint(transformedX, transformedY, _configManager.CurrentConfig.PenDownZ));
                    }
                    if (drawingPath.Points.Any())
                    {
                        drawingPaths.Add(drawingPath);
                    }
                }

                // Advance X with optional per-character spacing variation (±6%)
                double spacingMult = 1.0;
                if (humanize && rng != null)
                    spacingMult = 1.0 + (rng.NextDouble() - 0.5) * 0.12;

                currentX += fontSize * 0.8 * spacingMult;
            }
            return drawingPaths;
        }
    }
}
