using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsSmartTaskbar
{
    public class ProgramItem
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? Arguments { get; set; }
        public string Category { get; set; } = "All programs";
        public Icon? Icon { get; set; }
        public DateTime AddedDate { get; set; } = DateTime.Now;

        public ProgramItem() { }

        public ProgramItem(string name, string filePath, string? arguments = null)
        {
            Name = name;
            FilePath = filePath;
            Arguments = arguments;
            LoadIcon();
        }

        private void LoadIcon()
        {
            try
            {
                // Försök hämta ikon direkt från filen (fungerar för både .exe och .lnk)
                if (File.Exists(FilePath))
                {
                    Icon = Icon.ExtractAssociatedIcon(FilePath);
                    return;
                }

                // Om originalfilen inte finns, försök med målfilen för genvägar
                if (Path.GetExtension(FilePath).ToLower() == ".lnk")
                {
                    string targetPath = GetShortcutTarget(FilePath);
                    if (File.Exists(targetPath))
                        Icon = Icon.ExtractAssociatedIcon(targetPath);
                }
            }
            catch
            {
                // Använd standardikon om extrahering misslyckas
            }
        }

        private string GetShortcutTarget(string shortcutPath)
        {
            try
            {
                // Använd Shell API för att läsa genvägen
                return ReadShortcutTarget(shortcutPath);
            }
            catch
            {
                return shortcutPath;
            }
        }

        private string ReadShortcutTarget(string shortcutPath)
        {
            try
            {
                using (var fileStream = new FileStream(shortcutPath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fileStream))
                {
                    // Hoppa till header
                    reader.ReadUInt32(); // Header size
                    reader.ReadUInt16(); // Link CLSID
                    
                    // Läs flags
                    reader.BaseStream.Seek(0x14, SeekOrigin.Begin);
                    var flags = reader.ReadUInt32();
                    
                    var position = 0x4C; // Start position after header
                    
                    // Hoppa över LinkTargetIDList om det finns
                    if ((flags & 0x01) != 0)
                    {
                        reader.BaseStream.Seek(position, SeekOrigin.Begin);
                        var idListSize = reader.ReadUInt16();
                        position += 2 + idListSize;
                    }
                    
                    // Läs LinkInfo om det finns
                    if ((flags & 0x02) != 0)
                    {
                        reader.BaseStream.Seek(position, SeekOrigin.Begin);
                        var linkInfoSize = reader.ReadUInt32();
                        var linkInfoStart = position + 4;
                        
                        // Hitta path offset
                        reader.BaseStream.Seek(linkInfoStart + 0x10, SeekOrigin.Begin);
                        var pathOffset = reader.ReadUInt32();
                        
                        // Läs sökvägen
                        reader.BaseStream.Seek(linkInfoStart + pathOffset, SeekOrigin.Begin);
                        var pathBytes = reader.ReadBytes(260);
                        var path = Encoding.Default.GetString(pathBytes).TrimEnd('\0');
                        
                        return path;
                    }
                }
            }
            catch
            {
                // Fallback: försök att hitta sökvägen med enklare metod
                try
                {
                    var content = File.ReadAllText(shortcutPath);
                    // Försök att hitta en sökväg i innehållet
                    var lines = content.Split('\0');
                    foreach (var line in lines)
                    {
                        if (line.Length > 3 && line.Contains(":\\") && line.EndsWith(".exe"))
                        {
                            return line;
                        }
                    }
                }
                catch
                {
                    // Om allt misslyckas
                }
            }
            
            return shortcutPath;
        }

        public void Start()
        {
            try
            {
                string targetPath = FilePath;
                string targetArgs = Arguments ?? string.Empty;
                
                // Om det är en genväg, försök att starta den direkt via shell
                if (Path.GetExtension(FilePath).ToLower() == ".lnk")
                {
                    // Försök först att starta genvägen direkt
                    try
                    {
                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = FilePath,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(startInfo);
                        return;
                    }
                    catch
                    {
                        // Om det misslyckas, försök att hitta målfilen
                        targetPath = GetShortcutTarget(FilePath);
                    }
                }

                // Försök att starta programmet
                if (!string.IsNullOrEmpty(targetPath))
                {
                    // Om filen inte finns, försök med shell execute
                    if (!File.Exists(targetPath))
                    {
                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = targetPath,
                            Arguments = targetArgs,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(startInfo);
                    }
                    else
                    {
                        // Filen finns, starta normalt
                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = targetPath,
                            Arguments = targetArgs,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(startInfo);
                    }
                }
                else
                {
                    MessageBox.Show($"Kunde inte hitta målfilen: {targetPath}", "Fel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kunde inte starta programmet: {ex.Message}\nSökväg: {FilePath}", "Fel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
