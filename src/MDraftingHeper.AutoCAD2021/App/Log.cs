using System;
using System.Globalization;
using System.IO;

namespace MDraftingHeper.AutoCAD2021.App
{
    /// <summary>Small local log under %LOCALAPPDATA%\MDraftingHeper\logs (never in the install folder).</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 2 * 1024 * 1024;

        public static string FilePath => Path.Combine(Paths.LogDirectory, "mdh.log");

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception ex = null) => Write("ERROR", ex == null ? message : message + ": " + ex);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Paths.LogDirectory);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        var old = FilePath + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                                                 " " + level + " " + message + Environment.NewLine);
                }
            }
            catch (IOException) { /* logging must never break a command */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
