namespace WindowsSmartTaskbar
{
    public class ProgramData
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Category { get; set; } = "All programs";
    }

    public class AppSettings
    {
        public string? Language { get; set; }
        public string? Theme { get; set; }
        public bool? Autostart { get; set; }
    }
}
