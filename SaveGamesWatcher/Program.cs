using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.IO.Compression;
using System.Globalization;
using System.Drawing.Imaging;

namespace SaveGamesWatcher
{
    internal static class Program
    {
        private static CancellationTokenSource timeoutTrigger;
        private static string sourceFolder = null;
        private static string backupsFolder = null;
        private static int timeoutMilliseconds = 5000;
        private readonly static bool isRu = CultureInfo.CurrentCulture.Parent.Name == "ru";

        private static readonly string resSelectSourceFolder = isRu ? "Выберите папку для наблюдения" : "Select folder for watching";
        private static readonly string resSelectBackupFolder = isRu ? "Выберите папку резервных копий" : "Select backup folder";
        private static readonly string resNoFolderSelected = isRu ? "Папка не выбрана" : "No folder selected";
        private static readonly string resFolderNotFound = isRu ? "Папка не найдена" : "Folder not found";
        private static readonly string resWrongTimeout = isRu ? "Значение таймаута в миллисекундах должно быть больше 0" : "Timeout milliseconds value must be more than 0";
        private static readonly string resNotIntTimeout = isRu ? "Значение таймаута в миллисекундах должно быть целым числом" : "Timeout milliseconds value must be integer";
        private static readonly string resPressAnyKey = isRu ? "Для продолжения нажмите любую клавишу . . ." : "Press any key to continue . . .";
        private static readonly string resSource = isRu ? "Источник: " : "Source: ";
        private static readonly string resDestination = isRu ? "Цель: " : "Destination: ";

        [STAThread]
        static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            if (args.Length > 0)
            {
                sourceFolder = args[0];
                if (args.Length > 1) backupsFolder = args[1];
                if (string.IsNullOrEmpty(backupsFolder)) backupsFolder = ".";
                if (!Directory.Exists(backupsFolder)) Directory.CreateDirectory(backupsFolder);
                backupsFolder = Path.GetFullPath(backupsFolder);

                if (args.Length > 2)
                {
                    int timeout = 0;
                    try
                    {
                        timeout = int.Parse(args[2]);
                        if (timeout <= 0) Exit(resWrongTimeout);
                        timeoutMilliseconds = timeout;
                    }
                    catch
                    {
                        Exit(resNotIntTimeout);
                    }
                }
            }

            if (sourceFolder == null)
            {
                using var dialog = new CommonOpenFileDialog();
                dialog.Title = resSelectSourceFolder;
                dialog.IsFolderPicker = true;
                dialog.EnsurePathExists = true;
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok) sourceFolder = dialog.FileName;
            }

            if (string.IsNullOrEmpty(sourceFolder)) Exit(resNoFolderSelected);
            else if (!Directory.Exists(sourceFolder)) Exit(resFolderNotFound);

            if (backupsFolder == null)
            {
                using var dialog = new CommonOpenFileDialog();
                dialog.Title = resSelectBackupFolder;
                dialog.InitialDirectory = Directory.GetCurrentDirectory();
                dialog.IsFolderPicker = true;
                dialog.EnsurePathExists = true;
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok) backupsFolder = dialog.FileName;
            }

            if (string.IsNullOrEmpty(backupsFolder)) Exit(resNoFolderSelected);
            else if (!Directory.Exists(backupsFolder)) Exit(resFolderNotFound);

            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(resSource);
            Console.ForegroundColor = color;
            Console.WriteLine(sourceFolder);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(resDestination);
            Console.ForegroundColor = color;
            Console.WriteLine(backupsFolder);
            Console.WriteLine();
            Console.ForegroundColor = color;

            using var watcher = new FileSystemWatcher(sourceFolder);
            watcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 //| NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 //| NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size;
            watcher.Changed += OnFilesystemEvent;
            watcher.Created += OnFilesystemEvent;
            watcher.Deleted += OnFilesystemEvent;
            watcher.Renamed += OnFilesystemEvent;
            watcher.Error += OnError;
            watcher.Filter = "";
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            Thread.Sleep(Timeout.Infinite);
        }

        private static void Exit(string message, int code = 0)
        {
            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ForegroundColor = color;
            Console.Write(resPressAnyKey);
            Console.ReadKey(true);
            Environment.Exit(code);
        }
        private static void OnFilesystemEvent(object sender, FileSystemEventArgs e)
        {
            if (timeoutTrigger != null) return;
            if (Directory.Exists(e.FullPath)) return; // ignore folder changes

            var subfolderName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");

            // screenshot
            ScreenCapture.CaptureActiveWindow().Save(Path.Combine(backupsFolder, $"{subfolderName}.jpg"), ImageFormat.Jpeg);

            timeoutTrigger = SetTimeout(() => Backup(subfolderName), timeoutMilliseconds);
            Console.Write(subfolderName);
        }

        private static void Backup(string subfolder)
        {
            var destinationFolder = Path.Combine(backupsFolder, subfolder);
            if (!Directory.Exists(destinationFolder)) Directory.CreateDirectory(destinationFolder);
            CopyFilesRecursively(sourceFolder, $@"{destinationFolder}\");
            Console.WriteLine(" ok");
            timeoutTrigger = null;
            ZipFile.CreateFromDirectory(destinationFolder, $"{destinationFolder}.zip");
            Directory.Delete(destinationFolder, true);

        }

        public static CancellationTokenSource SetTimeout(Action action, int millis)
        {

            var cts = new CancellationTokenSource();
            var ct = cts.Token;
            _ = Task.Run(() =>
            {
                Thread.Sleep(millis);
                if (!ct.IsCancellationRequested) action();
            }, ct);

            return cts;
        }

        public static void ClearTimeout(CancellationTokenSource cts) => cts.Cancel();

        private static void CopyFilesRecursively(string sourcePath, string targetPath)
        {
            //Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
            }
        }

        private static void OnError(object sender, ErrorEventArgs e) => PrintException(e.GetException());

        private static void PrintException(Exception ex)
        {
            if (ex != null)
            {
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine("Stacktrace:");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine();
                PrintException(ex.InnerException);
            }
        }
    }
}
