using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PlotterControl.Models;

namespace PlotterControl.Controls
{
    public class PlotPreviewCanvas : Canvas
    {
        // --- Dependency Properties ---

        public static readonly DependencyProperty DrawingPathsProperty =
            DependencyProperty.Register(nameof(DrawingPaths), typeof(List<DrawingPath>), typeof(PlotPreviewCanvas),
                new PropertyMetadata(null, OnDrawingPathsChanged));

        public static readonly DependencyProperty BedWidthProperty =
            DependencyProperty.Register(nameof(BedWidth), typeof(double), typeof(PlotPreviewCanvas),
                new PropertyMetadata(200.0, OnPropertyChangedRedraw));

        public static readonly DependencyProperty BedHeightProperty =
            DependencyProperty.Register(nameof(BedHeight), typeof(double), typeof(PlotPreviewCanvas),
                new PropertyMetadata(200.0, OnPropertyChangedRedraw));

        public static readonly DependencyProperty MachineWidthProperty =
            DependencyProperty.Register(nameof(MachineWidth), typeof(double), typeof(PlotPreviewCanvas),
                new PropertyMetadata(220.0, OnPropertyChangedRedraw));

        public static readonly DependencyProperty MachineHeightProperty =
            DependencyProperty.Register(nameof(MachineHeight), typeof(double), typeof(PlotPreviewCanvas),
                new PropertyMetadata(220.0, OnPropertyChangedRedraw));

        public static readonly DependencyProperty PaperOriginXProperty =
            DependencyProperty.Register(nameof(PaperOriginX), typeof(double), typeof(PlotPreviewCanvas),
                new PropertyMetadata(0.0, OnPropertyChangedRedraw));

        public static readonly DependencyProperty PaperOriginYProperty =
            DependencyProperty.Register(nameof(PaperOriginY), typeof(double), typeof(PlotPreviewCanvas),
                new PropertyMetadata(0.0, OnPropertyChangedRedraw));

        public static readonly DependencyProperty ShowGridProperty =
            DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(PlotPreviewCanvas),
                new PropertyMetadata(true, OnPropertyChangedRedraw));

        public static readonly DependencyProperty CurrentXProperty =
            DependencyProperty.Register(nameof(CurrentX), typeof(double), typeof(PlotPreviewCanvas),
                new PropertyMetadata(0.0, OnPropertyChangedRedraw));

        public static readonly DependencyProperty CurrentYProperty =
            DependencyProperty.Register(nameof(CurrentY), typeof(double), typeof(PlotPreviewCanvas),
                new PropertyMetadata(0.0, OnPropertyChangedRedraw));

        // --- CLR Properties ---

        public List<DrawingPath> DrawingPaths
        {
            get => (List<DrawingPath>)GetValue(DrawingPathsProperty);
            set => SetValue(DrawingPathsProperty, value);
        }

        /// <summary>Paper/drawable width in mm (calibration area).</summary>
        public double BedWidth
        {
            get => (double)GetValue(BedWidthProperty);
            set => SetValue(BedWidthProperty, value);
        }

        /// <summary>Paper/drawable height in mm (calibration area).</summary>
        public double BedHeight
        {
            get => (double)GetValue(BedHeightProperty);
            set => SetValue(BedHeightProperty, value);
        }

        /// <summary>Full machine travel width in mm.</summary>
        public double MachineWidth
        {
            get => (double)GetValue(MachineWidthProperty);
            set => SetValue(MachineWidthProperty, value);
        }

        /// <summary>Full machine travel height in mm.</summary>
        public double MachineHeight
        {
            get => (double)GetValue(MachineHeightProperty);
            set => SetValue(MachineHeightProperty, value);
        }

        /// <summary>Paper origin X offset in machine coordinates (mm).</summary>
        public double PaperOriginX
        {
            get => (double)GetValue(PaperOriginXProperty);
            set => SetValue(PaperOriginXProperty, value);
        }

        /// <summary>Paper origin Y offset in machine coordinates (mm).</summary>
        public double PaperOriginY
        {
            get => (double)GetValue(PaperOriginYProperty);
            set => SetValue(PaperOriginYProperty, value);
        }

        public bool ShowGrid
        {
            get => (bool)GetValue(ShowGridProperty);
            set => SetValue(ShowGridProperty, value);
        }

        public double CurrentX
        {
            get => (double)GetValue(CurrentXProperty);
            set => SetValue(CurrentXProperty, value);
        }

        public double CurrentY
        {
            get => (double)GetValue(CurrentYProperty);
            set => SetValue(CurrentYProperty, value);
        }

        // --- Change handlers ---

        private static void OnDrawingPathsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PlotPreviewCanvas)d).InvalidateVisual();
        }

        private static void OnPropertyChangedRedraw(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PlotPreviewCanvas)d).InvalidateVisual();
        }

        // --- Rendering ---

        // Reusable pens/brushes (frozen for performance)
        private static readonly Pen FramePen = CreateFrozenPen(Color.FromRgb(100, 100, 100), 2);
        private static readonly Brush FrameFill = CreateFrozenBrush(Color.FromRgb(50, 50, 55));
        private static readonly Brush CanvasBg = CreateFrozenBrush(Color.FromRgb(35, 35, 40));
        private static readonly Pen PaperPen = CreateFrozenPen(Color.FromRgb(180, 180, 180), 1);
        private static readonly Brush PaperFill = CreateFrozenBrush(Color.FromRgb(250, 248, 240));
        private static readonly Pen GridPen = CreateFrozenPen(Color.FromArgb(30, 120, 120, 120), 0.5);
        private static readonly Pen DrawPen = CreateFrozenPen(Color.FromRgb(60, 130, 230), 1.5);
        private static readonly Pen TravelPen = CreateFrozenDashedPen(Color.FromArgb(40, 180, 180, 180), 0.5);
        private static readonly Pen CrosshairPen = CreateFrozenPen(Color.FromRgb(240, 60, 60), 1.2);
        private static readonly Brush HomeBrush = CreateFrozenBrush(Color.FromRgb(80, 200, 80));
        private static readonly Pen RailPen = CreateFrozenPen(Color.FromRgb(90, 90, 100), 1.5);
        private static readonly Typeface LabelTypeface = new Typeface("Segoe UI");

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double canvasW = ActualWidth;
            double canvasH = ActualHeight;
            if (canvasW < 20 || canvasH < 20) return;

            double margin = 20;
            double availW = canvasW - margin * 2;
            double availH = canvasH - margin * 2;

            // Use machine dimensions for the overall coordinate space
            double machW = Math.Max(MachineWidth, 1);
            double machH = Math.Max(MachineHeight, 1);

            // Scale to fit machine area in canvas
            double scaleX = availW / machW;
            double scaleY = availH / machH;
            double scale = Math.Min(scaleX, scaleY);

            double ox = margin + (availW - machW * scale) / 2;
            double oy = margin + (availH - machH * scale) / 2;

            // Helper to convert machine-mm to screen pixels
            Point ToScreen(double mmX, double mmY) => new Point(ox + mmX * scale, oy + mmY * scale);

            // --- Background ---
            dc.DrawRectangle(CanvasBg, null, new Rect(0, 0, canvasW, canvasH));

            // --- Machine frame (full travel area) ---
            var machRect = new Rect(ox, oy, machW * scale, machH * scale);
            dc.DrawRectangle(FrameFill, FramePen, machRect);

            // --- Rail lines along edges ---
            double railInset = 3;
            // Left rail
            dc.DrawLine(RailPen, new Point(ox + railInset, oy), new Point(ox + railInset, oy + machH * scale));
            // Bottom rail
            dc.DrawLine(RailPen, new Point(ox, oy + machH * scale - railInset), new Point(ox + machW * scale, oy + machH * scale - railInset));

            // --- Paper/drawable area ---
            double paperSx = ox + PaperOriginX * scale;
            double paperSy = oy + PaperOriginY * scale;
            double paperSw = BedWidth * scale;
            double paperSh = BedHeight * scale;
            var paperRect = new Rect(paperSx, paperSy, paperSw, paperSh);
            dc.DrawRectangle(PaperFill, PaperPen, paperRect);

            // --- Grid (on paper area only) ---
            if (ShowGrid)
            {
                double gridStep = 10; // 10mm
                for (double gx = gridStep; gx < BedWidth; gx += gridStep)
                {
                    double sx = paperSx + gx * scale;
                    dc.DrawLine(GridPen, new Point(sx, paperSy), new Point(sx, paperSy + paperSh));
                }
                for (double gy = gridStep; gy < BedHeight; gy += gridStep)
                {
                    double sy = paperSy + gy * scale;
                    dc.DrawLine(GridPen, new Point(paperSx, sy), new Point(paperSx + paperSw, sy));
                }
            }

            // --- Dimension labels ---
            if (scale > 0.3)
            {
                double fontSize = Math.Max(9, Math.Min(11, scale * 8));
                // Paper width label (top)
                var widthText = new FormattedText($"{BedWidth:F0}mm",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, fontSize,
                    Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(widthText, new Point(paperSx + (paperSw - widthText.Width) / 2, paperSy - widthText.Height - 2));

                // Paper height label (left)
                var heightText = new FormattedText($"{BedHeight:F0}mm",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, fontSize,
                    Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.PushTransform(new RotateTransform(-90, paperSx - 3, paperSy + paperSh / 2));
                dc.DrawText(heightText, new Point(paperSx - 3 - heightText.Width / 2, paperSy + paperSh / 2 - heightText.Height));
                dc.Pop();
            }

            // --- Home indicator (origin corner) ---
            var homePos = ToScreen(0, 0);
            dc.DrawEllipse(HomeBrush, null, homePos, 4, 4);

            // --- Drawing paths (offset by paper origin) ---
            if (DrawingPaths != null)
            {
                // Clip to paper area
                dc.PushClip(new RectangleGeometry(paperRect));

                foreach (var path in DrawingPaths)
                {
                    if (path == null || path.Points == null || path.Points.Count < 2) continue;
                    var pen = path.IsTravelMove ? TravelPen : DrawPen;

                    for (int i = 0; i < path.Points.Count - 1; i++)
                    {
                        var p0 = path.Points[i];
                        var p1 = path.Points[i + 1];

                        // Path coordinates are relative to paper origin
                        var sp0 = new Point(paperSx + p0.X * scale, paperSy + p0.Y * scale);
                        var sp1 = new Point(paperSx + p1.X * scale, paperSy + p1.Y * scale);
                        dc.DrawLine(pen, sp0, sp1);
                    }
                }

                dc.Pop(); // end clip
            }

            // --- Pen head crosshair (in machine coordinates) ---
            double penScreenX = ox + CurrentX * scale;
            double penScreenY = oy + CurrentY * scale;
            double chSize = 8;

            // Crosshair lines
            dc.DrawLine(CrosshairPen, new Point(penScreenX - chSize, penScreenY), new Point(penScreenX + chSize, penScreenY));
            dc.DrawLine(CrosshairPen, new Point(penScreenX, penScreenY - chSize), new Point(penScreenX, penScreenY + chSize));
            // Center dot
            dc.DrawEllipse(Brushes.Red, null, new Point(penScreenX, penScreenY), 2.5, 2.5);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            InvalidateVisual();
        }

        // --- Helper methods for frozen resources ---

        private static Pen CreateFrozenPen(Color color, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(color), thickness);
            pen.Freeze();
            return pen;
        }

        private static Pen CreateFrozenDashedPen(Color color, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(color), thickness) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            return pen;
        }

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
