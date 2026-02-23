# WindowsSmartTaskbar

En smart taskbar-applikation för Windows som låter dig samla och starta dina favoritprogram från ett enda ställe.

## Funktioner

- **Lägg till program**: Lägg till upp till 20 .exe-filer manuellt
- **Programstart**: Dubbelklicka på ett program för att starta det
- **Systemtray**: Kör i bakgrunden med en ikon i systemtray
- **Spara inställningar**: Programlistan sparas automatiskt
- **Modern UI**: Ren och användarvänlig gränssnitt

## Installation

1. Se till att du har .NET 6.0 eller senare installerat
2. Bygg projektet med `dotnet build`
3. Kör `WindowsSmartTaskbar.exe`

## Användning

1. **Starta applikationen**: Den körs i bakgrunden och visas som en ikon i systemtray
2. **Lägg till program**: 
   - Högerklicka på tray-ikonen och välj "Visa program"
   - Klicka på "Lägg till program"-knappen
   - Välj .exe-filer du vill lägga till (max 20 st)
3. **Starta program**: 
   - Dubbelklicka på ett program i listan
   - eller högerklicka på tray-ikonen och välj "Visa program"
4. **Ta bort program**: Markera ett eller flera program och klicka på "Ta bort"

## Teknisk information

- **Framework**: .NET 6.0 Windows Forms
- **Språk**: C#
- **Konfigurationsfil**: `programs.json` (sparas i samma mapp som .exe)
- **Max antal program**: 20

## Filer

- `Program.cs` - Startpunkt för applikationen
- `MainForm.cs` - Huvudformuläret med UI och logik
- `ProgramItem.cs` - Klass för att representera ett program
- `WindowsSmartTaskbar.csproj` - Projektfil
- `README.md` - Denna fil

## Systemkrav

- Windows 10 eller senare
- .NET 6.0 Runtime eller senare
