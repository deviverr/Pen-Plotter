// PlotterControl/PlotterControl/Models/DrawingPath.cs

using System.Collections.Generic;

namespace PlotterControl.Models
{
    public class DrawingPath
    {
        public List<PlotterPoint> Points { get; set; }
        public bool IsTravelMove { get; set; } // True if this path is a rapid/travel move (pen up)

        public DrawingPath()
        {
            Points = new List<PlotterPoint>();
        }
    }
}
