namespace WinFIMLog.Data
{
    public sealed class RetentionOptions
    {
        public int BaselineGenerations { get; set; } = 2;
        public int DeliveredOutboxDays { get; set; } = 7;
    }
}
