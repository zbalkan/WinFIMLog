namespace WinFIMLog.Events
{
    public sealed class BurstAggregationOptions
    {
        public bool Enabled { get; set; } = true;
        public int Threshold { get; set; } = 100;
        public int WindowSeconds { get; set; } = 10;
    }
}
