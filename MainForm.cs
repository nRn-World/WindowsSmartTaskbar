using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics; // Added for Drag-and-Drop logic

namespace WindowsSmartTaskbar
{
    public partial class MainForm : Form
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")] private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_W = 0x57; 
        private const int WM_HOTKEY = 0x0312;
        public const int WM_NCLBUTTONDOWN = 0xA1; public const int HT_CAPTION = 0x2;

        private List<ProgramItem> programs = new List<ProgramItem>();
        private List<Category> categories = new List<Category>();
        private string currentCategory = "All programs";
        private const string DefaultCategory = "All programs";
        private int MaxPrograms = 100;
        private HashSet<int> selectedIndices = new HashSet<int>();
        private string searchFilter = "";

        private string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsSmartTaskbar");
        private string ConfigFile => Path.Combine(AppDataFolder, "programs.json");
        private string CategoryFile => Path.Combine(AppDataFolder, "categories.json");
        private string SettingsFile => Path.Combine(AppDataFolder, "settings.json");

        private string currentLanguage = "sv";
        private string currentTheme = "dark";

        private Label titleLabel = default!;
        private Label catLabel = default!;
        private Panel categoryPanel = default!;
        private Panel programPanel = default!;
        private TextBox searchBox = default!;
        private Label statusLabel = default!;
        private Label copyrightLabel = default!;
        private NotifyIcon notifyIcon = default!;
        private ContextMenuStrip contextMenu = default!;
        private Panel mainContainer = default!;

        private Control? draggedControl;
        private ProgramItem? draggedProgram;
        private int draggedIndex = -1;
        private Point dragOffset;
        private bool isDragging = false;
        
        private System.Windows.Forms.Timer statusTimer;
        private bool currentIsDark = true;

        public MainForm()
        {
            EnsureDataFolder();
            LoadSettings();
            LoadCategories();
            LoadPrograms();
            
            InitializeComponent();
            SetupNotifyIcon();
            
            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_W);
            RefreshCategories();
            RefreshProgramList();
        }

        private void EnsureDataFolder()
        {
            if (!Directory.Exists(AppDataFolder)) Directory.CreateDirectory(AppDataFolder);
        }

        private void InitializeComponent()
        {
            this.Text = "WindowsSmartTaskbar";
            this.Size = new Size(500, 850);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.FromArgb(18, 18, 21);
            
            Action<object, MouseEventArgs> dragAction = (s, e) => {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); }
            };

            this.MouseDown += (s, e) => dragAction(s, e);

            mainContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            mainContainer.MouseDown += (s, e) => dragAction(s, e);
            this.Controls.Add(mainContainer);

            var titleContainer = new Panel { Dock = DockStyle.Top, Height = 85, Padding = new Padding(0, 0, 0, 15) };
            titleContainer.MouseDown += (s, e) => dragAction(s, e);
            
            var titleBack = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 52), Padding = new Padding(10) };
            titleBack.SizeChanged += (s, e) => SetRoundedRegion(titleBack, 20);
            titleBack.MouseDown += (s, e) => dragAction(s, e);

            titleLabel = new Label { Text = "WindowsSmartTaskbar", Font = new Font("Segoe UI Semibold", 16), ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            titleLabel.MouseDown += (s, e) => dragAction(s, e);

            var controlButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 70, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent };
            
            var closeBtn = new Label { Text = "\uE8BB", Font = new Font("Segoe MDL2 Assets", 10), ForeColor = Color.White, Size = new Size(35, 30), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            closeBtn.MouseEnter += (s, e) => closeBtn.BackColor = Color.FromArgb(232, 17, 35);
            closeBtn.MouseLeave += (s, e) => closeBtn.BackColor = Color.Transparent;
            closeBtn.Click += (s, e) => this.Hide(); // Hide to tray instead of exit for taskbar app

            var minBtn = new Label { Text = "\uE921", Font = new Font("Segoe MDL2 Assets", 10), ForeColor = Color.White, Size = new Size(35, 30), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            minBtn.MouseEnter += (s, e) => minBtn.BackColor = Color.FromArgb(64, 64, 64);
            minBtn.MouseLeave += (s, e) => minBtn.BackColor = Color.Transparent;
            minBtn.Click += (s, e) => this.WindowState = FormWindowState.Minimized;

            controlButtons.Controls.Add(closeBtn);
            controlButtons.Controls.Add(minBtn);
            
            titleBack.Controls.Add(controlButtons);
            titleBack.Controls.Add(titleLabel);
            titleContainer.Controls.Add(titleBack);

            categoryPanel = new Panel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(0, 5, 0, 15), BackColor = Color.Transparent };
            var catCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 52), Cursor = Cursors.Hand };
            catCard.SizeChanged += (s, e) => { SetRoundedRegion(catCard, 20); catLabel.Width = catCard.Width - 60; };
            catLabel = new Label { Text = currentCategory, Font = new Font("Segoe UI Semibold", 13), ForeColor = Color.White, Dock = DockStyle.Left, Width = 350, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(15, 0, 0, 0), Enabled = false, BackColor = Color.Transparent };
            var catChevron = new Label { Text = "\uE70D", Font = new Font("Segoe MDL2 Assets", 12), ForeColor = Color.Gray, Dock = DockStyle.Right, Width = 60, TextAlign = ContentAlignment.MiddleCenter, Enabled = false, BackColor = Color.Transparent };
            catCard.Controls.Add(catLabel); catCard.Controls.Add(catChevron);
            catCard.Click += (s, e) => ShowCategoryMenu(catCard);
            categoryPanel.Controls.Add(catCard);
            categoryPanel.MouseDown += (s, e) => dragAction(s, e);

            var actionGrid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 110, ColumnCount = 4, RowCount = 1 };
            // ... existing grid setup ...
            actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            var btnAdd = CreateActionItem("\uE109", T("addProgram"), Color.FromArgb(0, 120, 215), AddButton_Click);
            var btnRemove = CreateActionItem("\uE107", T("remove"), Color.FromArgb(220, 53, 69), RemoveButton_Click);
            var btnEdit = CreateActionItem("\uE104", T("editName"), Color.FromArgb(255, 193, 7), EditButton_Click);
            var btnCat = CreateActionItem("\uE188", T("addCategory"), Color.FromArgb(40, 167, 69), CategoryButton_Click);

            actionGrid.Controls.Add(btnAdd, 0, 0);
            actionGrid.Controls.Add(btnRemove, 1, 0);
            actionGrid.Controls.Add(btnEdit, 2, 0);
            actionGrid.Controls.Add(btnCat, 3, 0);

            var searchContainer = new Panel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(0, 15, 0, 15) };
            searchContainer.MouseDown += (s, e) => dragAction(s, e);

            var searchPill = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 52), Padding = new Padding(15, 10, 15, 5) };
            searchPill.SizeChanged += (s, e) => SetRoundedRegion(searchPill, 22);
            searchBox = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(45, 45, 52), ForeColor = Color.White, Font = new Font("Segoe UI", 12), PlaceholderText = T("search") };
            searchBox.TextChanged += (s, e) => { searchFilter = searchBox.Text; RefreshProgramList(); };
            searchPill.Controls.Add(searchBox);
            searchContainer.Controls.Add(searchPill);

            programPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent, Padding = new Padding(0, 5, 0, 0) };
            programPanel.MouseDown += (s, e) => dragAction(s, e);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(30,30,35) };
            footer.MouseDown += (s, e) => dragAction(s, e);
            statusLabel = new Label { Text = "0/20 Programs", Dock = DockStyle.Left, Width = 150, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(20, 0, 0, 0), Font = new Font("Segoe UI", 10) };
            copyrightLabel = new Label { Text = "Created 2026 by \u00A9 nRn World", Dock = DockStyle.Fill, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 20, 0), Font = new Font("Segoe UI", 10) };
            footer.Controls.Add(statusLabel);
            footer.Controls.Add(copyrightLabel);

            mainContainer.Controls.Add(programPanel);
            mainContainer.Controls.Add(footer);
            mainContainer.Controls.Add(searchContainer);
            mainContainer.Controls.Add(actionGrid);
            mainContainer.Controls.Add(categoryPanel);
            mainContainer.Controls.Add(titleContainer);
        }

        private Control CreateActionItem(string icon, string labelText, Color iconBack, EventHandler handler)
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Cursor = Cursors.Hand };
            var iconBox = new Label { Text = icon, Font = new Font("Segoe MDL2 Assets", 18), ForeColor = Color.White, BackColor = iconBack, Size = new Size(50, 50), TextAlign = ContentAlignment.MiddleCenter };
            iconBox.SizeChanged += (s, e) => SetRoundedRegion(iconBox, 15);
            var lbl = new Label { Text = labelText, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8), Dock = DockStyle.Bottom, Height = 35, TextAlign = ContentAlignment.TopCenter };
            pnl.Controls.Add(iconBox); pnl.Controls.Add(lbl);
            pnl.Resize += (s, e) => { iconBox.Left = (pnl.Width - iconBox.Width) / 2; iconBox.Top = 5; };
            
            pnl.Click += handler;
            iconBox.Click += (s, e) => handler(pnl, e);
            lbl.Click += (s, e) => handler(pnl, e);
            return pnl;
        }

        private void SetRoundedRegion(Control control, int radius)
        {
            try {
                IntPtr ptr = CreateRoundRectRgn(0, 0, control.Width + 1, control.Height + 1, radius, radius);
                control.Region = Region.FromHrgn(ptr);
            } catch {}
        }

        private void SetupNotifyIcon()
        {
            notifyIcon = new NotifyIcon { Text = "WindowsSmartTaskbar", Visible = true };
            notifyIcon.Icon = GenerateAppIcon();
            contextMenu = new ContextMenuStrip();
            var showMenuItem = new ToolStripMenuItem(T("showPrograms"));
            showMenuItem.Click += (s, e) => ShowProgramList();
            contextMenu.Items.Add(showMenuItem);
            var exitMenuItem = new ToolStripMenuItem(T("exit"));
            exitMenuItem.Click += (s, e) => Application.Exit();
            contextMenu.Items.Add(exitMenuItem);
            notifyIcon.ContextMenuStrip = contextMenu;
            
            notifyIcon.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowProgramList(); };
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0084 && (int)m.Result == 0x1) { m.Result = (IntPtr)0x2; } // HTCLIENT to HTCAPTION for smooth dragging
            if (m.Msg == 0x001A) { // WM_SETTINGCHANGE
                ApplyTheme();
            }
        }

        private void ShowCategoryMenu(Control target)
        {
            var cms = new ContextMenuStrip { BackColor = Color.FromArgb(45, 45, 52), ForeColor = Color.White, ShowImageMargin = false, ShowCheckMargin = false };
            foreach (var cat in categories) {
                var item = new ToolStripMenuItem(cat.Name == DefaultCategory ? T("allPrograms") : cat.Name);
                item.Click += (s, e) => {
                    currentCategory = cat.Name;
                    RefreshCategories();
                    RefreshProgramList();
                };
                cms.Items.Add(item);
            }
            cms.Show(target, new Point(0, target.Height));
        }

        private void ToggleVisibility()
        {
            if (this.Visible && this.WindowState != FormWindowState.Minimized) this.Hide();
            else ShowProgramList();
        }

        private void ShowProgramList()
        {
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            this.Show();
            this.TopMost = true; this.Activate(); this.BringToFront(); this.Focus();
            SetForegroundWindow(this.Handle);
            this.TopMost = false;
        }

        private void RefreshCategories()
        {
            if (categoryPanel == null || categoryPanel.Controls.Count == 0) return;
            var card = categoryPanel.Controls[0];
            var lbl = card.Controls.OfType<Label>().FirstOrDefault(l => l.Dock == DockStyle.Left);
            if (lbl != null) lbl.Text = currentCategory == DefaultCategory ? T("allPrograms") : currentCategory;
        }

        private void RefreshProgramList()
        {
            if (programPanel == null) return;
            programPanel.SuspendLayout();
            programPanel.Controls.Clear(); selectedIndices.Clear();
            var filtered = currentCategory == DefaultCategory ? programs.ToList() : programs.Where(p => p.Category == currentCategory).ToList();
            if (!string.IsNullOrWhiteSpace(searchFilter)) filtered = filtered.Where(p => p.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = filtered.Count - 1; i >= 0; i--) programPanel.Controls.Add(CreateProgramRow(filtered[i], i));
            programPanel.ResumeLayout();
            UpdateStatus();
            
            this.AllowDrop = true;
            this.DragEnter += MainForm_DragEnter;
            this.DragDrop += MainForm_DragDrop;

            statusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            statusTimer.Tick += StatusTimer_Tick;
            statusTimer.Start();

            ApplyTheme();
        }

        private Panel CreateProgramRow(ProgramItem program, int index)
        {
            var rowWrapper = new Panel { Height = 95, Dock = DockStyle.Top, BackColor = Color.Transparent, Padding = new Padding(0, 5, 0, 5), Tag = program };
            var card = new Panel { Height = 85, Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 52), Cursor = Cursors.Hand, Tag = program };
            rowWrapper.Controls.Add(card); card.SizeChanged += (s, e) => SetRoundedRegion(card, 20);
            
            var iconBox = new PictureBox { Size = new Size(48, 48), Location = new Point(15, 18), SizeMode = PictureBoxSizeMode.StretchImage };
            try { if (program.Icon != null) iconBox.Image = program.Icon.ToBitmap(); } catch {}
            var nameLabel = new Label { Text = program.Name, Font = new Font("Segoe UI Semibold", 12), ForeColor = currentIsDark ? Color.White : Color.Black, Location = new Point(80, 18), AutoSize = true, BackColor = Color.Transparent };
            var pathLabel = new Label { Text = Path.GetFileName(program.FilePath), Font = new Font("Segoe UI", 9), ForeColor = currentIsDark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(100, 100, 100), Location = new Point(80, 45), AutoSize = true, BackColor = Color.Transparent };
            
            var statusDot = new Panel { Size = new Size(10, 10), BackColor = Color.Transparent };
            statusDot.Location = new Point(card.Width - 30, (card.Height - 10) / 2);
            statusDot.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            statusDot.SizeChanged += (s, e) => SetRoundedRegion(statusDot, 10);
            
            var deleteBtn = new Label { Text = "\uE74D", Font = new Font("Segoe MDL2 Assets", 12), ForeColor = currentIsDark ? Color.Gray : Color.DarkGray, BackColor = Color.Transparent, AutoSize = true, Cursor = Cursors.Hand };
            deleteBtn.Location = new Point(card.Width - 70, (card.Height - 15) / 2);
            deleteBtn.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            deleteBtn.MouseEnter += (s, e) => deleteBtn.ForeColor = Color.FromArgb(220, 53, 69);
            deleteBtn.MouseLeave += (s, e) => deleteBtn.ForeColor = currentIsDark ? Color.Gray : Color.DarkGray;
            deleteBtn.Click += (s, e) => {
                if (MessageBox.Show($"Vill du verkligen ta bort {program.Name}?", "Bekr\u00e4fta", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                    programs.Remove(program); SavePrograms(); RefreshProgramList();
                }
            };
            
            card.Controls.AddRange(new Control[] { iconBox, nameLabel, pathLabel, statusDot, deleteBtn });

            // Context Menu for ProgramsHelper for Renaming
            Action showRename = () => {
                using (var form = new Form { Text = "Redigera program", Size = new Size(300, 180), StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(32, 32, 35), ForeColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog }) {
                    var lbl = new Label { Text = "Program-namn:", Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(10, 0, 0, 5) };
                    var txt = new TextBox { Dock = DockStyle.Top, Text = program.Name, BackColor = Color.FromArgb(45, 45, 52), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 12) };
                    var btn = new Button { Text = "Spara", Dock = DockStyle.Bottom, Height = 50, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White };
                    btn.Click += (se, ev) => { program.Name = txt.Text; form.DialogResult = DialogResult.OK; };
                    form.Controls.Add(txt); form.Controls.Add(lbl); form.Controls.Add(btn);
                    if (form.ShowDialog() == DialogResult.OK) { SavePrograms(); RefreshProgramList(); }
                }
            };
            // Context Menu for Programs
            var cms = new ContextMenuStrip { BackColor = Color.FromArgb(45, 45, 52), ForeColor = Color.White };
            var editItem = new ToolStripMenuItem("Byt namn på program");
            editItem.Click += (s, e) => {
                showRename();
            };
            var deleteItem = new ToolStripMenuItem("Ta bort program");
            deleteItem.Click += (s, e) => {
                if (MessageBox.Show($"Vill du ta bort {program.Name}?", "Bekr\u00e4fta", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                    programs.Remove(program); SavePrograms(); RefreshProgramList();
                }
            };
            cms.Items.Add(editItem);
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add(deleteItem);
            card.ContextMenuStrip = cms;
            iconBox.ContextMenuStrip = cms; nameLabel.ContextMenuStrip = cms; pathLabel.ContextMenuStrip = cms;

            // Enhanced Interaction Logic
            Point mouseDownPos = Point.Empty;
            bool dragThresholdMet = false;

            MouseEventHandler onDown = (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    isDragging = false; 
                    dragThresholdMet = false;
                    draggedProgram = program; 
                    draggedControl = rowWrapper; 
                    mouseDownPos = Cursor.Position;
                    dragOffset = card.PointToClient(Cursor.Position);
                }
            };

            MouseEventHandler onMove = (s, e) => {
                if (draggedControl != null && Control.MouseButtons == MouseButtons.Left) {
                    var curPos = Cursor.Position;
                    if (!dragThresholdMet && (Math.Abs(curPos.X - mouseDownPos.X) > 5 || Math.Abs(curPos.Y - mouseDownPos.Y) > 5)) {
                        dragThresholdMet = true;
                        isDragging = true;
                        card.BackColor = Color.FromArgb(60, 60, 70);
                    }

                    if (isDragging) {
                        var clientPos = programPanel.PointToClient(Cursor.Position);
                        CheckDragPositionOptimized(clientPos.Y);
                    }
                }
            };

            MouseEventHandler onUp = (s, e) => {
                if (isDragging) {
                    isDragging = false; card.BackColor = Color.FromArgb(45, 45, 52);
                    var currentOrder = programPanel.Controls.OfType<Panel>().Select(p => (ProgramItem)p.Tag).Reverse().ToList();
                    programs = currentOrder;
                    SavePrograms(); RefreshProgramList();
                }
            };

            card.MouseDown += onDown; card.MouseMove += onMove; card.MouseUp += onUp;
            iconBox.MouseDown += onDown; iconBox.MouseMove += onMove; iconBox.MouseUp += onUp;
            nameLabel.MouseDown += onDown; nameLabel.MouseMove += onMove; nameLabel.MouseUp += onUp;
            pathLabel.MouseDown += onDown; pathLabel.MouseMove += onMove; pathLabel.MouseUp += onUp;

            nameLabel.Click += (s, e) => { if (!dragThresholdMet) showRename(); };
            card.DoubleClick += (s, e) => program.Start();
            iconBox.DoubleClick += (s, e) => program.Start();
            nameLabel.DoubleClick += (s, e) => program.Start();
            pathLabel.DoubleClick += (s, e) => program.Start();
            return rowWrapper;
        }

        private void CheckDragPositionOptimized(int mouseY)
        {
            if (draggedControl == null) return;
            
            foreach (Control ctrl in programPanel.Controls) {
                if (ctrl == draggedControl) continue;
                if (mouseY > ctrl.Top && mouseY < ctrl.Bottom) {
                    var targetIdx = programPanel.Controls.GetChildIndex(ctrl);
                    var currentIdx = programPanel.Controls.GetChildIndex(draggedControl);
                    if (targetIdx != currentIdx) {
                        programPanel.Controls.SetChildIndex(draggedControl, targetIdx);
                    }
                    return;
                }
            }
        }

        private void UpdateStatus() { if (statusLabel != null) statusLabel.Text = $"{programs.Count}/{MaxPrograms} {T("programs")}"; }

        private void LoadPrograms()
        {
            if (File.Exists(ConfigFile)) {
                var json = File.ReadAllText(ConfigFile);
                try {
                var items = JsonSerializer.Deserialize<List<ProgramData>>(json);
                if (items != null) programs = items.Select(d => new ProgramItem(d.Name, d.FilePath) { Category = d.Category }).ToList();
                } catch {}
            }
        }

        private void SavePrograms()
        {
            var data = programs.Select(p => new ProgramData { Name = p.Name, FilePath = p.FilePath, Category = p.Category }).ToList();
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void LoadCategories()
        {
            categories = new List<Category> { new Category(DefaultCategory) };
            if (File.Exists(CategoryFile)) {
                var json = File.ReadAllText(CategoryFile);
                try {
                var saved = JsonSerializer.Deserialize<List<Category>>(json);
                if (saved != null) categories.AddRange(saved.Where(c => c.Name != DefaultCategory));
                } catch {}
            }
        }

        private void SaveCategories()
        {
            var toSave = categories.Where(c => c.Name != DefaultCategory).ToList();
            File.WriteAllText(CategoryFile, JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void LoadSettings() { if (File.Exists(SettingsFile)) { var json = File.ReadAllText(SettingsFile); try { var s = JsonSerializer.Deserialize<AppSettings>(json); if (s != null) { currentLanguage = s.Language ?? "sv"; currentTheme = s.Theme ?? "dark"; } } catch {} } }
        private void SaveSettings() { var s = new AppSettings { Language = currentLanguage, Theme = currentTheme }; File.WriteAllText(SettingsFile, JsonSerializer.Serialize(s)); }

        private string T(string key)
        {
            if (currentLanguage == "sv") {
                switch (key) {
                    case "addProgram": return "L\u00e4gg till";
                    case "remove": return "Ta bort";
                    case "editName": return "Redigera";
                    case "addCategory": return "Ny kategori";
                    case "search": return "S\u00f6k...";
                    case "allPrograms": return "Alla program";
                    case "showPrograms": return "Visa program";
                    case "exit": return "Avsluta";
                    case "programs": return "program";
                    default: return key;
                }
            }
            return key;
        }

        private Icon GenerateAppIcon() {
            using (Bitmap bmp = new Bitmap(256, 256)) {
                using (Graphics g = Graphics.FromImage(bmp)) {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.Clear(Color.Transparent);

                    // Premium Background Gradient
                    var rect = new Rectangle(10, 10, 236, 236);
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath()) {
                        path.AddEllipse(rect);
                        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(rect, Color.FromArgb(0, 120, 215), Color.FromArgb(0, 40, 100), 45f)) {
                            g.FillPath(brush, path);
                        }
                    }

                    // Stylized "S" / Checkmark Symbol
                    using (Pen pen = new Pen(Color.White, 18)) {
                        pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        g.DrawLine(pen, 70, 130, 110, 170);
                        g.DrawLine(pen, 110, 170, 180, 80);
                    }
                }
                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private void AddButton_Click(object? sender, EventArgs e) { 
            using (var ofd = new OpenFileDialog { Multiselect = true }) {
                if (ofd.ShowDialog() == DialogResult.OK) {
                    foreach (var file in ofd.FileNames) programs.Insert(0, new ProgramItem(Path.GetFileNameWithoutExtension(file), file) { Category = currentCategory });
                    SavePrograms(); RefreshProgramList();
                }
            }
        }
        private void RemoveButton_Click(object? sender, EventArgs e) {
            if (currentCategory == DefaultCategory) return;
            if (programs.Any(p => p.Category == currentCategory)) {
                MessageBox.Show("Rensa kategorin f\u00f6rst!") ; return;
            }
            if (MessageBox.Show($"Vill du verkligen radera kategorin '{currentCategory}'?", "Bekr\u00e4fta", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                categories.RemoveAll(c => c.Name == currentCategory);
                currentCategory = DefaultCategory;
                SaveCategories(); RefreshCategories(); RefreshProgramList();
            }
        }

        private void EditButton_Click(object? sender, EventArgs e) {
            if (currentCategory == DefaultCategory) return;
            using (var form = new Form { Text = "Byt namn", Size = new Size(300, 180), StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(32, 32, 35), ForeColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog }) {
                var lbl = new Label { Text = "Nytt namn:", Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(10, 0, 0, 5) };
                var txt = new TextBox { Dock = DockStyle.Top, Text = currentCategory, BackColor = Color.FromArgb(45, 45, 52), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 12) };
                var btn = new Button { Text = "Spara", Dock = DockStyle.Bottom, Height = 50, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White };
                btn.Click += (s, ev) => {
                    string newName = txt.Text;
                    foreach (var p in programs.Where(p => p.Category == currentCategory)) p.Category = newName;
                    var cat = categories.FirstOrDefault(c => c.Name == currentCategory);
                    if (cat != null) cat.Name = newName;
                    currentCategory = newName;
                    form.DialogResult = DialogResult.OK;
                };
                form.Controls.Add(txt); form.Controls.Add(lbl); form.Controls.Add(btn);
                if (form.ShowDialog() == DialogResult.OK) { SavePrograms(); SaveCategories(); RefreshCategories(); RefreshProgramList(); }
            }
        }

        private void CategoryButton_Click(object? sender, EventArgs e) {
            using (var form = new Form { Text = "Ny kategori", Size = new Size(300, 180), StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(32, 32, 35), ForeColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog }) {
                var lbl = new Label { Text = "Kategori-namn:", Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(10, 0, 0, 5) };
                var txt = new TextBox { Dock = DockStyle.Top, BackColor = Color.FromArgb(45, 45, 52), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 12) };
                var btn = new Button { Text = "Skapa", Dock = DockStyle.Bottom, Height = 50, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White };
                btn.Click += (s, ev) => { if (!string.IsNullOrWhiteSpace(txt.Text)) categories.Add(new Category(txt.Text)); form.DialogResult = DialogResult.OK; };
                form.Controls.Add(txt); form.Controls.Add(lbl); form.Controls.Add(btn);
                if (form.ShowDialog() == DialogResult.OK) { SaveCategories(); RefreshCategories(); }
            }
        }

        private bool IsSystemDarkTheme()
        {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                    if (key?.GetValue("AppsUseLightTheme") is int val) {
                        return val == 0;
                    }
                }
            } catch {}
            return true;
        }

        private void ApplyTheme()
        {
            currentIsDark = IsSystemDarkTheme();
            Color bgMain = currentIsDark ? Color.FromArgb(32, 32, 35) : Color.FromArgb(243, 243, 243);
            Color bgCard = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White;
            Color textMain = currentIsDark ? Color.White : Color.Black;
            Color textDim = currentIsDark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(100, 100, 100);

            this.BackColor = bgMain;
            if (mainContainer != null) mainContainer.BackColor = bgMain;
            if (titleLabel != null) titleLabel.ForeColor = textMain;
            
            if (searchBox != null) {
                searchBox.BackColor = bgCard;
                searchBox.ForeColor = textMain;
                if (searchBox.Parent is Panel pnl) pnl.BackColor = bgCard;
            }
            if (categoryPanel?.Controls.Count > 0 && categoryPanel.Controls[0] is Panel catCard) {
                catCard.BackColor = bgCard;
                foreach (Control c in catCard.Controls) {
                    if (c is Label l && l.Text != "\uE70D") l.ForeColor = textMain;
                }
            }
            if (programPanel != null) {
                foreach (Control row in programPanel.Controls) {
                    if (row.Controls.Count > 0 && row.Controls[0] is Panel card) {
                        card.BackColor = bgCard;
                        foreach (Control c in card.Controls) {
                            if (c is Label l) {
                                if (l.Text == "\uE74D") l.ForeColor = currentIsDark ? Color.Gray : Color.DarkGray;
                                else l.ForeColor = l.Font.Size >= 12 ? textMain : textDim;
                            }
                        }
                    }
                }
            }
            if (statusLabel != null) statusLabel.ForeColor = textDim;
            if (copyrightLabel != null) copyrightLabel.ForeColor = textDim;
        }

        private void MainForm_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (files.Any(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))) {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
            }
            e.Effect = DragDropEffects.None;
        }

        private void MainForm_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            bool added = false;
            foreach (string file in files) {
                if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
                    if (programs.Count >= MaxPrograms) { MessageBox.Show($"Maxgräns på {MaxPrograms} program nådd."); break; }
                    if (!programs.Any(p => p.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase))) {
                        string name = Path.GetFileNameWithoutExtension(file);
                        programs.Add(new ProgramItem(name, file) { Category = currentCategory });
                        added = true;
                    }
                }
            }
            if (added) { SavePrograms(); RefreshProgramList(); }
        }

        private void StatusTimer_Tick(object? sender, EventArgs e)
        {
            if (programs.Count == 0 || programPanel == null || programPanel.Controls.Count == 0) return;
            try {
                var processes = Process.GetProcesses();
                var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in processes) {
                    try { running.Add(p.ProcessName); } catch {}
                }
                foreach (Control rowWrapper in programPanel.Controls) {
                    if (rowWrapper.Controls.Count > 0 && rowWrapper.Controls[0] is Panel card) {
                        if (card.Tag is ProgramItem prog) {
                            string exeName = Path.GetFileNameWithoutExtension(prog.FilePath);
                            bool isRunning = running.Contains(exeName);
                            var dot = card.Controls.OfType<Panel>().FirstOrDefault(c => c.Width == 10 && c.Height == 10);
                            if (dot != null) dot.BackColor = isRunning ? Color.LimeGreen : Color.Transparent;
                        }
                    }
                }
            } catch {}
        }
    }
}
