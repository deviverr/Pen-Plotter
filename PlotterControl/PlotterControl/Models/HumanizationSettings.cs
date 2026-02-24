namespace PlotterControl.Models
{
    /// <summary>
    /// Settings controlling how human-like the plotter draws and writes.
    /// All spatial values are in millimeters.
    /// </summary>
    public class HumanizationSettings
    {
        /// <summary>Whether humanization is applied at all.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Maximum micro-jitter amplitude in mm. Each point is displaced by a spatially
        /// correlated random offset (tremor model: 60% previous + 40% new random) so the
        /// noise moves smoothly like a hand tremor rather than pure white noise.
        /// Typical range: 0.1–0.5 mm.
        /// </summary>
        public float JitterMm { get; set; } = 0.35f;

        /// <summary>
        /// Maximum baseline wobble in mm (slow sinusoidal Y drift applied across all strokes).
        /// Simulates slight paper/baseline tilt or handwriting drift.
        /// Typical range: 0.1–0.6 mm.
        /// </summary>
        public float BaselineWobbleMm { get; set; } = 0.5f;

        /// <summary>
        /// ±Z variation for pen pressure simulation in mm.
        /// The pen-down Z shifts slightly for each stroke, simulating variable pen pressure.
        /// Typical range: 0.0–0.5 mm.
        /// </summary>
        public float PenPressureVariationMm { get; set; } = 0.3f;

        /// <summary>
        /// Speed multiplier applied at sharp corners (0.0–1.0).
        /// 1.0 = no slowdown at corners; 0.5 = half-speed at a 180° reversal.
        /// Typical: 0.5–0.8.
        /// </summary>
        public float CornerSlowdown { get; set; } = 0.65f;

        /// <summary>
        /// Random seed for reproducible output. -1 means use a time-based seed
        /// (different result each render). Any non-negative value gives reproducible output.
        /// </summary>
        public int RandomSeed { get; set; } = -1;
    }
}
