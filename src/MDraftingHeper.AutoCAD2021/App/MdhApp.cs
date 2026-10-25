using System;
using Autodesk.AutoCAD.Runtime;
using MDraftingHeper.AutoCAD2021.App;
using MDraftingHeper.AutoCAD2021.Metadata;
using MDraftingHeper.AutoCAD2021.Ribbon;
using MDraftingHeper.Core.Settings;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(MdhApp))]
[assembly: CommandClass(typeof(MDraftingHeper.AutoCAD2021.Commands.MdhCommands))]
[assembly: CommandClass(typeof(MDraftingHeper.AutoCAD2021.Commands.SelfTestCommands))]

namespace MDraftingHeper.AutoCAD2021.App
{
    /// <summary>Entry point: settings, Ribbon and steel metadata synchronisation.</summary>
    public sealed class MdhApp : IExtensionApplication
    {
        public static UserSettings Settings { get; private set; } = new UserSettings();

        public void Initialize()
        {
            try
            {
                Log.Info("MDraftingHeper " + typeof(MdhApp).Assembly.GetName().Version + " loading from " + Paths.AssemblyDirectory);
                Settings = SettingsStore.Load(SettingsStore.DefaultPath, out var problem);
                if (problem != null) Log.Warn(problem);
                RibbonManager.Start();
                SteelSync.Start();
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\nMDraftingHeper loaded: MDH_REBAR, MDH_TIE, MDH_STEEL.");
            }
            catch (System.Exception ex)
            {
                // Initialisation must not take AutoCAD down; report and keep commands available.
                Log.Error("Initialisation failed", ex);
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nMDraftingHeper failed to initialise: " + ex.Message);
            }
        }

        public void Terminate()
        {
            try
            {
                RibbonManager.Stop();
                SteelSync.Stop();
                SaveSettings();
            }
            catch (System.Exception ex)
            {
                Log.Error("Terminate", ex);
            }
        }

        public static void SaveSettings()
        {
            try { SettingsStore.Save(SettingsStore.DefaultPath, Settings); }
            catch (System.Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Settings not saved: " + ex.Message);
            }
        }
    }
}
