using System;

namespace WindowsSmartTaskbar
{
    public class Category
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#007ACC";
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public Category() { }

        public Category(string name, string color = "#007ACC")
        {
            Name = name;
            Color = color;
        }
    }
}
