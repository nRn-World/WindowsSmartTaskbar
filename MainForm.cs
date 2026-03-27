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
        private HashSet<ProgramItem> selectedPrograms = new HashSet<ProgramItem>();
        private string searchFilter = "";

        private string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsSmartTaskbar");
        private string ConfigFile => Path.Combine(AppDataFolder, "programs.json");
        private string CategoryFile => Path.Combine(AppDataFolder, "categories.json");
        private string SettingsFile => Path.Combine(AppDataFolder, "settings.json");

        private string currentLanguage = "sv";
        private string currentTheme = "auto";
        
        public class SettingsData {
            public string Language { get; set; } = "auto";
            public string Theme { get; set; } = "auto";
            public bool Autostart { get; set; } = true;
        }
        private SettingsData appSettings = new SettingsData();

        private Label titleLabel = default!;
        private Label catLabel = default!;
        private Panel categoryPanel = default!;
        private Panel programPanel = default!;
        private TextBox searchBox = default!;
        private Label statusLabel = default!;
        private Label copyrightLabel = default!;
        private NotifyIcon? notifyIcon;
        private ContextMenuStrip? contextMenu;
        private ContextMenuStrip? leftClickMenu;
        private System.Windows.Forms.Timer? statusTimer;
        
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (e.CloseReason == CloseReason.UserClosing && !isExiting) {
                e.Cancel = true;
                allowVisible = false;
                this.Hide();
                return;
            }
            if (statusTimer != null) { statusTimer.Stop(); statusTimer.Dispose(); }
            if (notifyIcon != null) { notifyIcon.Visible = false; notifyIcon.Dispose(); }
            base.OnFormClosing(e);
        }
        private TableLayoutPanel actionGrid = default!;
        private Panel titleContainer = default!;
        private Panel titleBack = default!;
        private Panel mainContainer = default!;
        private Panel footer = default!;

        private Control? draggedControl;
        private ProgramItem? draggedProgram;
        private int draggedIndex = -1;
        private Point dragOffset;
        private bool isDragging = false;
        
        private bool currentIsDark = true;
        private Panel scrollTrack = default!;
        private Panel scrollThumb = default!;
        private Panel scrollContainer = default!;
        private bool isScrollDragging = false;
        private int scrollStartY = 0;
        private int currentScrollPos = 0;

        private bool allowVisible = true;
        private bool isExiting = false;
        private bool isAutostart = false;

        public MainForm(bool autostart = false)
        {
            isAutostart = autostart;
            if (autostart)
            {
                allowVisible = false;
            }

            EnsureDataFolder();
            bool firstRun = !File.Exists(SettingsFile);
            LoadSettings();
            if (firstRun) SaveSettings();
            LoadCategories();
            LoadPrograms();
            
            InitializeComponent();

            // Must be set AFTER InitializeComponent to prevent reset
            if (autostart)
            {
                this.ShowInTaskbar = false;
                this.WindowState = FormWindowState.Minimized;
                this.Opacity = 0;
            }

            SetupNotifyIcon();
            
            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_W);
            RefreshCategories();
            RefreshProgramList();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (isAutostart)
            {
                this.Hide();
                this.ShowInTaskbar = false;
                this.Opacity = 1; // Restore opacity for future shows
            }
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

            titleContainer = new Panel { Dock = DockStyle.Top, Height = 85, Padding = new Padding(0, 0, 0, 15) };
            titleContainer.MouseDown += (s, e) => dragAction(s, e);
            
            titleBack = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 52), Padding = new Padding(10) };
            titleBack.SizeChanged += (s, e) => SetRoundedRegion(titleBack, 20);
            titleBack.MouseDown += (s, e) => dragAction(s, e);

            titleLabel = new Label { Text = "WindowsSmartTaskbar", Font = new Font("Segoe UI Semibold", 16), ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            titleLabel.MouseDown += (s, e) => dragAction(s, e);

            var controlButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 70, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent };
            
            var closeBtn = new Label { Text = "\uE8BB", Font = new Font("Segoe MDL2 Assets", 10), ForeColor = Color.White, Size = new Size(35, 30), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            closeBtn.MouseEnter += (s, e) => closeBtn.BackColor = Color.FromArgb(232, 17, 35);
            closeBtn.MouseLeave += (s, e) => closeBtn.BackColor = Color.Transparent;
            closeBtn.Click += (s, e) => { allowVisible = false; this.Hide(); }; // Hide to tray instead of exit for taskbar app

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

            actionGrid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 110, ColumnCount = 4, RowCount = 1 };
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

            programPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = false, BackColor = Color.Transparent, Padding = new Padding(0, 5, 0, 0) };
            programPanel.MouseDown += (s, e) => dragAction(s, e);
            programPanel.MouseWheel += (s, e) => DoScroll(e.Delta > 0 ? 80 : -80);

            scrollContainer = new Panel { Dock = DockStyle.None, Width = 800, BackColor = Color.Transparent, Location = new Point(0,0) };
            scrollContainer.MouseDown += (s, e) => dragAction(s, e);
            scrollContainer.MouseWheel += (s, e) => DoScroll(e.Delta > 0 ? 80 : -80);
            programPanel.Controls.Add(scrollContainer);
            programPanel.SizeChanged += (s, e) => { scrollContainer.Width = programPanel.Width; UpdateScrollBar(); };

            scrollTrack = new Panel { Dock = DockStyle.Right, Width = 8, BackColor = Color.Transparent, Visible = false };
            scrollThumb = new Panel { BackColor = Color.Gray, Width = 8, MinimumSize = new Size(8, 20), Cursor = Cursors.Hand };
            scrollThumb.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { isScrollDragging = true; scrollStartY = e.Y; } };
            scrollThumb.MouseMove += (s, e) => { if (isScrollDragging) SetScrollFromThumb(scrollThumb.Top + (e.Y - scrollStartY)); };
            scrollThumb.MouseUp += (s, e) => isScrollDragging = false;
            scrollTrack.Controls.Add(scrollThumb);
            this.Controls.Add(scrollTrack);

            this.AllowDrop = true;
            this.DragEnter += MainForm_DragEnter;
            this.DragDrop += MainForm_DragDrop;
            
            statusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            statusTimer.Tick += StatusTimer_Tick;
            statusTimer.Start();

            footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(30, 30, 35) };
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
            mainContainer.Resize += (s, e) => { UpdateScrollBar(); DoScroll(0); };
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

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        public static extern bool DeleteObject(IntPtr hObject);

        private void SetRoundedRegion(Control control, int radius)
        {
            try {
                IntPtr ptr = CreateRoundRectRgn(0, 0, control.Width + 1, control.Height + 1, radius, radius);
                control.Region = Region.FromHrgn(ptr);
                DeleteObject(ptr);
            } catch {}
        }

        private void SetupNotifyIcon()
        {
            notifyIcon = new NotifyIcon { Text = "WindowsSmartTaskbar", Visible = true };
            notifyIcon.Icon = GenerateAppIcon();
            
            // Right Click Menu
            contextMenu = new ContextMenuStrip { BackColor = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White, ForeColor = currentIsDark ? Color.White : Color.Black, ShowImageMargin = false, ShowCheckMargin = false };
            var settingsItem = new ToolStripMenuItem(T("settings"));
            settingsItem.Click += (s, e) => ShowSettingsDialog();
            var exitMenuItem = new ToolStripMenuItem(T("exit"));
            exitMenuItem.Click += (s, e) => { isExiting = true; Application.Exit(); };
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(exitMenuItem);
            notifyIcon.ContextMenuStrip = contextMenu;
            
            // Left click handling
            notifyIcon.MouseClick += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    BuildAndShowLeftClickMenu();
                }
            };
        }

        private void BuildAndShowLeftClickMenu()
        {
            if (leftClickMenu != null) leftClickMenu.Dispose();
            leftClickMenu = new ContextMenuStrip { BackColor = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White, ForeColor = currentIsDark ? Color.White : Color.Black, ShowImageMargin = false, ShowCheckMargin = false };
            
            foreach (var cat in categories) {
                var catItem = new ToolStripMenuItem(cat.Name == DefaultCategory ? T("allPrograms") : cat.Name);
                catItem.DropDown.BackColor = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White;
                catItem.DropDown.ForeColor = currentIsDark ? Color.White : Color.Black;
                if (catItem.DropDown is ToolStripDropDownMenu ddm) {
                    ddm.ShowImageMargin = true; ddm.ShowCheckMargin = false;
                    ddm.Renderer = new DarkRenderer(new DarkColorTable(ddm.BackColor));
                }
                
                var catPrograms = cat.Name == DefaultCategory ? programs : programs.Where(p => p.Category == cat.Name).ToList();
                
                if (catPrograms.Count > 0) {
                    foreach (var p in catPrograms) {
                        var pItem = new ToolStripMenuItem(p.Name);
                        try { if (p.Icon != null) pItem.Image = p.Icon.ToBitmap(); } catch {}
                        pItem.Click += (s, e) => p.Start();
                        catItem.DropDownItems.Add(pItem);
                    }
                } else {
                    catItem.DropDownItems.Add(new ToolStripMenuItem("(Tom)") { Enabled = false });
                }
                leftClickMenu.Items.Add(catItem);
            }
            
            leftClickMenu.Items.Add(new ToolStripSeparator());
            var openUiItem = new ToolStripMenuItem(T("showPrograms"));
            openUiItem.Font = new Font(openUiItem.Font, FontStyle.Bold);
            openUiItem.Click += (s, e) => ShowProgramList();
            leftClickMenu.Items.Add(openUiItem);

            Type t = typeof(NotifyIcon);
            var mi = t.GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mi != null) {
                notifyIcon.ContextMenuStrip = leftClickMenu;
                mi.Invoke(notifyIcon, null);
                notifyIcon.ContextMenuStrip = contextMenu;
            } else {
                leftClickMenu.Show(Cursor.Position);
            }
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
            if (this.Visible && this.WindowState != FormWindowState.Minimized) { allowVisible = false; this.Hide(); }
            else ShowProgramList();
        }

        private void ShowProgramList()
        {
            allowVisible = true;
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            this.Show();
            this.TopMost = true; this.Activate(); this.BringToFront(); this.Focus();
            SetForegroundWindow(this.Handle);
            this.TopMost = false;
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible)
            {
                value = false;
                if (!this.IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
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
            
            foreach (Control ctrl in scrollContainer.Controls) {
                if (ctrl is Panel row) {
                    foreach (Control inner in row.Controls) {
                        if (inner is Panel card) {
                            foreach (Control sub in card.Controls) if (sub is PictureBox pb && pb.Image != null) { pb.Image.Dispose(); pb.Image = null; }
                        }
                    }
                }
                ctrl.Dispose();
            }
            scrollContainer.Controls.Clear();
            selectedPrograms.Clear();

            var filtered = currentCategory == DefaultCategory ? programs.ToList() : programs.Where(p => p.Category == currentCategory).ToList();
            if (!string.IsNullOrWhiteSpace(searchFilter)) filtered = filtered.Where(p => p.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) scrollContainer.Controls.Add(CreateProgramRow(filtered[i], i));
            scrollContainer.Height = filtered.Count * 95;
            programPanel.ResumeLayout();
            UpdateStatus();
            UpdateScrollBar();
            ApplyTheme();
        }

        private Panel CreateProgramRow(ProgramItem program, int index)
        {
            var rowWrapper = new Panel { Height = 95, Dock = DockStyle.Top, BackColor = Color.Transparent, Padding = new Padding(0, 5, 0, 5), Tag = program };
            var card = new Panel { Height = 85, Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 52), Cursor = Cursors.Hand, Tag = program };
            rowWrapper.Controls.Add(card); card.SizeChanged += (s, e) => SetRoundedRegion(card, 20);
            card.DoubleClick += (s, e) => program.Start();
            
            var iconBox = new PictureBox { Size = new Size(48, 48), Location = new Point(15, 18), SizeMode = PictureBoxSizeMode.StretchImage, Cursor = Cursors.Hand };
            iconBox.DoubleClick += (s, e) => program.Start();
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
                if (MessageBox.Show($"{T("confirmDelete")}'{program.Name}'?", T("confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
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
            var cms = new ContextMenuStrip { BackColor = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White, ForeColor = currentIsDark ? Color.White : Color.Black, ShowImageMargin = true, ShowCheckMargin = false };
            cms.Renderer = new DarkRenderer(new DarkColorTable(cms.BackColor));
            cms.Opening += (s, e) => {
                cms.Items.Clear();
                var moveMenu = new ToolStripMenuItem(selectedPrograms.Count > 1 ? T("moveMultiple") : T("moveToCat"));
                moveMenu.DropDown.BackColor = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White;
                moveMenu.DropDown.ForeColor = currentIsDark ? Color.White : Color.Black;
                if (moveMenu.DropDown is ToolStripDropDownMenu dm) {
                    dm.ShowImageMargin = true; dm.ShowCheckMargin = false;
                    dm.Renderer = new DarkRenderer(new DarkColorTable(dm.BackColor));
                }
                foreach (var cat in categories.Where(c => c.Name != DefaultCategory)) {
                    var cItem = new ToolStripMenuItem(cat.Name);
                    cItem.Click += (se, ev) => {
                        var targets = selectedPrograms.Count > 0 && selectedPrograms.Contains(program) ? selectedPrograms.ToList() : new List<ProgramItem> { program };
                        foreach (var t in targets) t.Category = cat.Name;
                        selectedPrograms.Clear();
                        SavePrograms(); RefreshProgramList();
                    };
                    moveMenu.DropDownItems.Add(cItem);
                }
                if (moveMenu.DropDownItems.Count == 0) moveMenu.Enabled = false;
                cms.Items.Add(moveMenu);
                
                var editItem = new ToolStripMenuItem(T("editName"));
                editItem.Click += (se, ev) => showRename();
                cms.Items.Add(editItem);

                cms.Items.Add(new ToolStripSeparator());
                var deleteItem = new ToolStripMenuItem(T("remove"));
                deleteItem.Click += (se, ev) => {
                    var targets = selectedPrograms.Count > 0 && selectedPrograms.Contains(program) ? selectedPrograms.ToList() : new List<ProgramItem> { program };
                    if (MessageBox.Show($"{T("confirmDelete")}'{(targets.Count > 1 ? targets.Count + " " + T("programs") : program.Name)}'?", T("confirm"), MessageBoxButtons.YesNo) == DialogResult.Yes) {
                        foreach (var t in targets) programs.Remove(t);
                        selectedPrograms.Clear();
                        SavePrograms(); RefreshProgramList();
                    }
                };
                cms.Items.Add(deleteItem);
            };
            card.ContextMenuStrip = cms;
            iconBox.ContextMenuStrip = cms; nameLabel.ContextMenuStrip = cms; pathLabel.ContextMenuStrip = cms;

            Action toggleSelect = () => {
                if (selectedPrograms.Contains(program)) {
                    selectedPrograms.Remove(program);
                    card.BackColor = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White;
                } else {
                    selectedPrograms.Add(program);
                    card.BackColor = currentIsDark ? Color.FromArgb(20, 60, 90) : Color.FromArgb(200, 220, 255);
                }
            };
            
            if (selectedPrograms.Contains(program)) card.BackColor = currentIsDark ? Color.FromArgb(20, 60, 90) : Color.FromArgb(200, 220, 255);

            // Enhanced Interaction Logic
            Point mouseDownPos = Point.Empty;
            bool dragThresholdMet = false;
            
            var holdTimer = new System.Windows.Forms.Timer { Interval = 600 };
            holdTimer.Tick += (s, e) => {
                holdTimer.Stop();
                if (!isDragging && !dragThresholdMet) toggleSelect();
            };

            MouseEventHandler onDown = (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    toggleSelect();
                    if (Control.ModifierKeys.HasFlag(Keys.Control)) { return; }
                    holdTimer.Start();
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
                        holdTimer.Stop();
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
                holdTimer.Stop();
                if (isDragging) {
                    isDragging = false;
                    card.BackColor = selectedPrograms.Contains(program) ? (currentIsDark ? Color.FromArgb(20, 60, 90) : Color.FromArgb(200, 220, 255)) : (currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White);
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
                    case "settings": return "Inst\u00e4llningar";
                    case "confirmDelete": return "Vill du verkligen ta bort ";
                    case "confirmDeleteCat": return "Vill du verkligen radera kategorin ";
                    case "confirm": return "Bekr\u00e4fta";
                    case "clearCatFirst": return "Rensa kategorin f\u00f6rst!";
                    case "maxReached": return "Maxgr\u00e4ns n\u00e5dd: ";
                    case "moveToCat": return "Flytta till kategori";
                    case "moveMultiple": return "Flytta markerade till...";
                    case "startWithWindows": return "Starta med Windows";
                }
            } else if (currentLanguage == "tr") {
                switch (key) {
                    case "addProgram": return "Ekle";
                    case "remove": return "Sil";
                    case "editName": return "D\u00fczenle";
                    case "addCategory": return "KategoriEkle";
                    case "search": return "Ara...";
                    case "allPrograms": return "T\u00fcm Programlar";
                    case "showPrograms": return "Programlar\u0131 G\u00f6ster";
                    case "exit": return "\u00c7\u0131k\u0131\u015f";
                    case "programs": return "program";
                    case "settings": return "Ayarlar";
                    case "confirmDelete": return "Ger\u00e7ekten silmek istiyor musunuz: ";
                    case "confirmDeleteCat": return "Kategoriyi ger\u00e7ekten silmek istiyor musunuz: ";
                    case "confirm": return "Onayla";
                    case "clearCatFirst": return "\u00d6nce kategoriyi temizleyin!";
                    case "maxReached": return "Maksimum s\u00e4n\u0131ra ula\u015f\u0131ld\u0131: ";
                    case "moveToCat": return "Kategoriye ta\u015f\u0131";
                    case "moveMultiple": return "Se\u00e7ilileri ta\u015f\u0131...";
                    case "startWithWindows": return "Windows ile ba\u015flat";
                }
            }
            switch (key) {
                case "addProgram": return "Add Program";
                case "remove": return "Remove";
                case "editName": return "Edit Name";
                case "addCategory": return "New Category";
                case "search": return "Search...";
                case "allPrograms": return "All Programs";
                case "showPrograms": return "Show Programs";
                case "exit": return "Exit";
                case "programs": return "programs";
                case "settings": return "Settings";
                case "confirmDelete": return "Are you sure you want to delete ";
                case "confirmDeleteCat": return "Are you sure you want to delete category ";
                case "confirm": return "Confirm";
                case "clearCatFirst": return "Clear category first!";
                case "maxReached": return "Max limit reached: ";
                case "moveToCat": return "Move to category";
                case "moveMultiple": return "Move selected to...";
                case "startWithWindows": return "Start with Windows";
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
                MessageBox.Show(T("clearCatFirst")); return;
            }
            if (MessageBox.Show($"{T("confirmDeleteCat")} '{currentCategory}'?", T("confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
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
            if (currentTheme == "auto" || string.IsNullOrEmpty(currentTheme)) {
                currentIsDark = IsSystemDarkTheme();
            } else {
                currentIsDark = currentTheme == "dark";
            }
            Color bgRoot = currentIsDark ? Color.FromArgb(18, 18, 21) : Color.FromArgb(230, 230, 230);
            Color bgMain = currentIsDark ? Color.FromArgb(32, 32, 35) : Color.FromArgb(243, 243, 243);
            Color bgCard = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White;
            Color textMain = currentIsDark ? Color.White : Color.Black;
            Color textDim = currentIsDark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(100, 100, 100);

            this.BackColor = bgRoot;
            if (mainContainer != null) mainContainer.BackColor = bgRoot;
            if (titleContainer != null) titleContainer.BackColor = bgRoot;
            if (titleBack != null) titleBack.BackColor = bgMain;
            if (footer != null) footer.BackColor = currentIsDark ? Color.FromArgb(30, 30, 35) : Color.FromArgb(225, 225, 230);
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
            if (scrollThumb != null) scrollThumb.BackColor = currentIsDark ? Color.FromArgb(80, 80, 90) : Color.FromArgb(180, 180, 190);
        }

        private void DoScroll(int delta)
        {
            if (scrollContainer == null || programPanel == null) return;
            int totalHeight = scrollContainer.Height;
            int viewportHeight = programPanel.Height;
            if (totalHeight <= viewportHeight) { currentScrollPos = 0; scrollContainer.Top = 0; UpdateScrollBar(); return; }

            currentScrollPos = Math.Clamp(currentScrollPos - delta, 0, totalHeight - viewportHeight);
            scrollContainer.Top = -currentScrollPos;
            UpdateScrollBar();
        }

        private void UpdateScrollBar()
        {
            if (programPanel == null || scrollContainer == null) return;
            int totalHeight = scrollContainer.Height;
            int viewportHeight = programPanel.Height;

            if (totalHeight <= viewportHeight) {
                scrollTrack.Visible = false;
                currentScrollPos = 0;
                scrollContainer.Top = 0;
                return;
            }

            scrollTrack.Visible = true;
            scrollTrack.BackColor = currentIsDark ? Color.FromArgb(30, 30, 35) : Color.FromArgb(220, 220, 225);
            
            float ratio = (float)viewportHeight / totalHeight;
            scrollThumb.Height = Math.Max(20, (int)(scrollTrack.Height * ratio));
            
            float scrollRatio = (float)currentScrollPos / (totalHeight - viewportHeight);
            scrollThumb.Top = (int)((scrollTrack.Height - scrollThumb.Height) * scrollRatio);
        }

        private void SetScrollFromThumb(int y)
        {
            int trackMax = scrollTrack.Height - scrollThumb.Height;
            if (trackMax <= 0) return;
            y = Math.Clamp(y, 0, trackMax);
            
            float scrollRatio = (float)y / trackMax;
            int totalHeight = scrollContainer.Height;
            int viewportHeight = programPanel.Height;
            
            currentScrollPos = (int)((totalHeight - viewportHeight) * scrollRatio);
            scrollContainer.Top = -currentScrollPos;
            UpdateScrollBar();
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
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                var exeFiles = files.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)).ToList();
                
                if (programs.Count + exeFiles.Count > MaxPrograms) {
                    MessageBox.Show(T("maxReached") + MaxPrograms);
                    return;
                }

                foreach (var path in exeFiles) {
                    string name = Path.GetFileNameWithoutExtension(path);
                    programs.Add(new ProgramItem(name, path) { Category = currentCategory == DefaultCategory ? "All programs" : currentCategory });
                }
                SavePrograms();
                RefreshProgramList();
            }
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
                foreach (Control rowWrapper in scrollContainer.Controls) {
                    if (rowWrapper.Controls.Count > 0 && rowWrapper.Controls[0] is Panel card) {
                        if (card.Tag is ProgramItem prog) {
                            string exeName = Path.GetFileNameWithoutExtension(prog.FilePath);
                            bool isRunning = running.Contains(exeName);
                            var dot = card.Controls.OfType<Panel>().FirstOrDefault(c => c.Width == 10 && c.Height == 10);
                            if (dot != null) dot.BackColor = isRunning ? Color.LimeGreen : Color.FromArgb(220, 53, 69);
                        }
                    }
                }
            } catch {}
        }

        private void LoadSettings()
        {
            if (File.Exists(SettingsFile)) {
                try {
                    var json = File.ReadAllText(SettingsFile);
                    var node = JsonSerializer.Deserialize<SettingsData>(json);
                    if (node != null) appSettings = node;
                } catch {}
            }
            if (appSettings.Language == "auto") {
                string sysLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
                currentLanguage = (sysLang == "sv" || sysLang == "tr") ? sysLang : "en";
            } else {
                currentLanguage = appSettings.Language;
            }
            currentTheme = appSettings.Theme;
        }

        private void SaveSettings()
        {
            try {
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(appSettings));
                SetAutostart(appSettings.Autostart);
            } catch {}
        }

        private void SetAutostart(bool enable)
        {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                    if (key != null) {
                        if (enable) key.SetValue("WindowsSmartTaskbar", "\"" + Application.ExecutablePath + "\" -autostart");
                        else key.DeleteValue("WindowsSmartTaskbar", false);
                    }
                }
            } catch {}
        }

        private void ApplyLanguage()
        {
            if (searchBox != null) searchBox.PlaceholderText = T("search");
            try {
                if (actionGrid != null && actionGrid.Controls.Count >= 4) {
                    ((Label)((Panel)actionGrid.GetControlFromPosition(0,0)).Controls[1]).Text = T("addProgram");
                    ((Label)((Panel)actionGrid.GetControlFromPosition(1,0)).Controls[1]).Text = T("remove");
                    ((Label)((Panel)actionGrid.GetControlFromPosition(2,0)).Controls[1]).Text = T("editName");
                    ((Label)((Panel)actionGrid.GetControlFromPosition(3,0)).Controls[1]).Text = T("addCategory");
                }
            } catch {}
            
            if (catLabel != null) catLabel.Text = currentCategory == DefaultCategory ? T("allPrograms") : currentCategory;
            UpdateStatus();
            // Note: Do NOT call BuildAndShowLeftClickMenu() here - it would
            // pop open the tray menu unexpectedly when settings are saved.
        }

        private void ShowSettingsDialog()
        {
            using (var form = new Form { Text = T("settings"), Size = new Size(350, 300), StartPosition = FormStartPosition.CenterScreen, BackColor = currentIsDark ? Color.FromArgb(32, 32, 35) : Color.FromArgb(243, 243, 243), ForeColor = currentIsDark ? Color.White : Color.Black, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false }) {
                
                var topPanel = new Panel { Dock = DockStyle.Top, Height = 180, Padding = new Padding(20) };
                
                var lblLang = new Label { Text = "Language / Spr\u00e5k / Dil:", Dock = DockStyle.Top, Height = 30 };
                var cmbLang = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Height = 30, BackColor = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White, ForeColor = currentIsDark ? Color.White : Color.Black };
                cmbLang.Items.AddRange(new string[] { "Auto (System)", "Svenska", "English", "T\u00fcrk\u00e7e" });
                cmbLang.SelectedIndex = appSettings.Language == "sv" ? 1 : appSettings.Language == "en" ? 2 : appSettings.Language == "tr" ? 3 : 0;
                
                var spacer = new Panel { Dock = DockStyle.Top, Height = 10 };
                
                var lblTheme = new Label { Text = "Theme / Tema:", Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 10, 0, 0) };
                var cmbTheme = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Height = 30, BackColor = currentIsDark ? Color.FromArgb(45, 45, 52) : Color.White, ForeColor = currentIsDark ? Color.White : Color.Black };
                cmbTheme.Items.AddRange(new string[] { "Auto (System)", "Dark / M\u00f6rkt", "Light / Ljust" });
                cmbTheme.SelectedIndex = appSettings.Theme == "light" ? 2 : appSettings.Theme == "dark" ? 1 : 0;
                
                var chkAutostart = new CheckBox { Text = T("startWithWindows"), Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 5, 0, 0), Checked = appSettings.Autostart };
                chkAutostart.FlatStyle = FlatStyle.Flat;
                chkAutostart.ForeColor = currentIsDark ? Color.White : Color.Black;
                
                topPanel.Controls.Add(chkAutostart);
                topPanel.Controls.Add(cmbTheme);
                topPanel.Controls.Add(lblTheme);
                topPanel.Controls.Add(spacer);
                topPanel.Controls.Add(cmbLang);
                topPanel.Controls.Add(lblLang);
                
                var btnSave = new Button { Text = "Spara / Save / Kaydet", Dock = DockStyle.Bottom, Height = 50, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White };
                btnSave.Click += (s, e) => {
                    appSettings.Language = cmbLang.SelectedIndex == 1 ? "sv" : cmbLang.SelectedIndex == 2 ? "en" : cmbLang.SelectedIndex == 3 ? "tr" : "auto";
                    appSettings.Theme = cmbTheme.SelectedIndex == 1 ? "dark" : cmbTheme.SelectedIndex == 2 ? "light" : "auto";
                    appSettings.Autostart = chkAutostart.Checked;
                    
                    if (appSettings.Language == "auto") {
                        string sysLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
                        currentLanguage = (sysLang == "sv" || sysLang == "tr") ? sysLang : "en";
                    } else {
                        currentLanguage = appSettings.Language;
                    }
                    currentTheme = appSettings.Theme;
                    SaveSettings();
                    ApplyTheme();
                    ApplyLanguage();
                    form.Close();
                };
                
                form.Controls.Add(topPanel);
                form.Controls.Add(btnSave);
                form.ShowDialog();
            }
        }
        public class DarkRenderer : ToolStripProfessionalRenderer {
            public DarkRenderer(ProfessionalColorTable table) : base(table) { }
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
                using (var b = new SolidBrush(e.ToolStrip.BackColor)) e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) {
                using (var b = new SolidBrush(e.ToolStrip.BackColor)) e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
        }
        public class DarkColorTable : ProfessionalColorTable {
            private Color bg;
            public DarkColorTable(Color c) { bg = c; }
            public override Color ImageMarginGradientBegin => bg;
            public override Color ImageMarginGradientMiddle => bg;
            public override Color ImageMarginGradientEnd => bg;
            public override Color ToolStripDropDownBackground => bg;
            public override Color MenuItemSelected => Color.FromArgb(65, 65, 75);
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(65, 65, 75);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(65, 65, 75);
        }
    }
}
