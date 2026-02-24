// PlotterControl/PlotterControl/Models/Font.cs

using System.Collections.Generic;
using System.Text.Json.Serialization; // For [JsonConstructor]

namespace PlotterControl.Models
{
    public class Font
    {
        public string Name { get; set; }
        public List<Glyph> Glyphs { get; set; }

        public Font()
        {
            Glyphs = new List<Glyph>();
        }
    }

    public class Glyph
    {
        public char Character { get; set; }
        public List<List<PlotterPoint>> Paths { get; set; } // Each inner list is a continuous stroke

        [JsonConstructor]
        public Glyph(char character, List<List<PlotterPoint>> paths)
        {
            Character = character;
            Paths = paths;
        }
    }
}
