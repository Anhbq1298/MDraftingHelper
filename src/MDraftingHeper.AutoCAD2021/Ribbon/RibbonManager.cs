using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Ribbon;
using Autodesk.Windows;
using MDraftingHeper.AutoCAD2021.App;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MDraftingHeper.AutoCAD2021.Ribbon
{
    /// <summary>
    /// Owns the MDraftingHeper Ribbon tab. The tab is created once per Ribbon instance, looked up by a
    /// stable id before every add (no duplicates) and re-added after a workspace switch removes it.
    /// </summary>
    public static class RibbonManager
    {
        public const string TabId = "MDraftingHeper.Tab";
        public const string TabTitle = "MDraftingHeper";

        private static bool _started;
        private static bool _idleQueued;

        public static void Start()
        {
            if (_started) return;
            _started = true;
            AcApp.SystemVariableChanged += OnSystemVariableChanged;
            if (ComponentManager.Ribbon == null)
                RibbonServices.RibbonPaletteSetCreated += OnRibbonPaletteSetCreated;
            QueueEnsure();
        }

        public static void Stop()
        {
            if (!_started) return;
            _started = false;
            AcApp.SystemVariableChanged -= OnSystemVariableChanged;
            RibbonServices.RibbonPaletteSetCreated -= OnRibbonPaletteSetCreated;
            if (RibbonServices.RibbonPaletteSet != null)
                RibbonServices.RibbonPaletteSet.WorkspaceLoaded -= OnWorkspaceLoaded;
            if (_idleQueued) { AcApp.Idle -= OnIdle; _idleQueued = false; }
        }

        /// <summary>Number of MDraftingHeper tabs currently in the Ribbon (for self-tests).</summary>
        public static int TabCount()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return 0;
            int n = 0;
            foreach (var t in ribbon.Tabs) if (t.Id == TabId) n++;
            return n;
        }

        private static void OnRibbonPaletteSetCreated(object sender, EventArgs e)
        {
            RibbonServices.RibbonPaletteSetCreated -= OnRibbonPaletteSetCreated;
            RibbonServices.RibbonPaletteSet.WorkspaceLoaded -= OnWorkspaceLoaded;
            RibbonServices.RibbonPaletteSet.WorkspaceLoaded += OnWorkspaceLoaded;
            QueueEnsure();
        }

        private static void OnWorkspaceLoaded(object sender, EventArgs e) => QueueEnsure();

        private static void OnSystemVariableChanged(object sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
        {
            if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) QueueEnsure();
        }

        /// <summary>Defers Ribbon changes to Idle, when the Ribbon is in a stable state.</summary>
        private static void QueueEnsure()
        {
            if (_idleQueued || !_started) return;
            _idleQueued = true;
            AcApp.Idle += OnIdle;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon == null) return; // keep waiting
            AcApp.Idle -= OnIdle;
            _idleQueued = false;
            try
            {
                EnsureNow();
            }
            catch (System.Exception ex)
            {
                Log.Error("Ribbon creation failed", ex);
            }
        }

        /// <summary>Adds the tab if the Ribbon exists and lacks it. Idempotent; returns false without a Ribbon.</summary>
        public static bool EnsureNow()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return false;
            if (RibbonServices.RibbonPaletteSet != null)
            {
                RibbonServices.RibbonPaletteSet.WorkspaceLoaded -= OnWorkspaceLoaded;
                RibbonServices.RibbonPaletteSet.WorkspaceLoaded += OnWorkspaceLoaded;
            }
            if (ribbon.FindTab(TabId) == null) ribbon.Tabs.Add(CreateTab());
            return true;
        }

        private static RibbonTab CreateTab()
        {
            var tab = new RibbonTab { Title = TabTitle, Id = TabId };
            var source = new RibbonPanelSource { Title = "Drafting tools", Id = TabId + ".Panel" };
            source.Items.Add(Button("Rebar\nPercentage", "MDH_REBAR", "rebar", "Count rebar Circles in a section boundary and calculate the reinforcement ratio."));
            source.Items.Add(Button("Tie &\nHook", "MDH_TIE", "tie", "Draw a tie with 90°, 135° or 180° hooks between two longitudinal bars."));
            source.Items.Add(Button("Steel\nSection", "MDH_STEEL", "steel", "Insert CHS, SHS, RHS, UB or UC sections as native Dynamic Blocks."));
            source.Items.Add(Button("Quick Draw\nRectangular", "MDH_RECT", "rebar", "Draft a rectangular reinforced-concrete column section with named bars and ties."));
            tab.Panels.Add(new RibbonPanel { Source = source });
            return tab;
        }

        private static RibbonButton Button(string text, string command, string icon, string tip)
        {
            return new RibbonButton
            {
                Id = TabId + "." + command,
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = LoadIcon(icon + "_32.png"),
                Image = LoadIcon(icon + "_16.png"),
                ToolTip = tip,
                CommandParameter = command,
                CommandHandler = new CommandHandler()
            };
        }

        private static BitmapImage LoadIcon(string file)
        {
            var path = Paths.Icon(file);
            if (!File.Exists(path)) return null;
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad; // do not keep the file locked
            img.UriSource = new Uri(path);
            img.EndInit();
            img.Freeze();
            return img;
        }

        /// <summary>Runs the command in the active document; cancels any running command first.</summary>
        private sealed class CommandHandler : ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter)
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                var cmd = (parameter as RibbonButton)?.CommandParameter as string ?? parameter as string;
                if (string.IsNullOrEmpty(cmd)) return;
                doc.SendStringToExecute("\x03\x03_" + cmd + " ", true, false, false);
            }
        }
    }
}
