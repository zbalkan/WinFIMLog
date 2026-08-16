namespace WinFIMLog.Data
{
    public sealed class RetentionOptions
    {
        public int DeliveredOutboxDays { get; set; } = 7;
        public int BaselineGenerations { get; set; } = 2;
    }
}
