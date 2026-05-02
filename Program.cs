using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Diagnostics;

namespace UE5GitHubDownloader
{
    class Program
    {
        private static readonly HttpClient client = new HttpClient();
        private static string githubToken = "";
        private static string installDir = "";

        static async Task Main(string[] args)
        {
            Console.Title = "UE5 GitHub Downloader";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("===============================================");
            Console.WriteLine("   UNREAL ENGINE 5 GITHUB DOWNLOADER");
            Console.WriteLine("===============================================");
            Console.ResetColor();
            Console.WriteLine();

            // Load or setup token
            await LoadOrSetupToken();

            // Select branch
            string branch = SelectBranch();

            // Set install directory
            SetInstallDirectory();

            // Download and install
            await DownloadAndInstallUE5(branch);

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        static async Task LoadOrSetupToken()
        {
            string tokenFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ue5_token.txt");

            if (File.Exists(tokenFile))
            {
                githubToken = File.ReadAllText(tokenFile).Trim();
                Console.Write("Checking saved token");
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(500);
                    Console.Write(".");
                }

                if (await TestToken(githubToken))
                {
                    Console.WriteLine(" ✅ Token is valid!");
                    return;
                }
                else
                {
                    Console.WriteLine(" ❌ Token is invalid or expired");
                    File.Delete(tokenFile);
                }
            }

            // Get new token
            Console.WriteLine("\n[Option 1] Open browser to create GitHub token");
            Console.WriteLine("[Option 2] Enter existing token");
            Console.Write("\nChoose (1-2): ");
            string choice = Console.ReadLine();

            if (choice == "1")
            {
                await CreateTokenInBrowser();
            }
            else
            {
                await EnterExistingToken();
            }

            // Save token
            File.WriteAllText(tokenFile, githubToken);
            Console.WriteLine("\n✅ Token saved! You can use it next time.");
        }

        static async Task CreateTokenInBrowser()
        {
            Console.WriteLine("\nOpening GitHub token creation page...");
            Console.WriteLine("IMPORTANT: Check these scopes:");
            Console.WriteLine("  ✅ repo (Full control of private repositories)");
            Console.WriteLine("  ✅ workflow");
            Console.WriteLine("  ✅ read:org");

            await Task.Delay(1000);
            var psi = new ProcessStartInfo
            {
                FileName = "https://github.com/settings/tokens/new?scopes=repo,workflow,read:org&description=UE5+Downloader",
                UseShellExecute = true
            };
            Process.Start(psi);

            Console.Write("\nAfter creating the token, paste it here: ");
            githubToken = Console.ReadLine()?.Trim();

            while (!await TestToken(githubToken))
            {
                Console.Write("Token invalid! Please paste again: ");
                githubToken = Console.ReadLine()?.Trim();
            }
        }

        static async Task EnterExistingToken()
        {
            Console.Write("\nEnter your GitHub Personal Access Token: ");
            githubToken = Console.ReadLine()?.Trim();

            while (!await TestToken(githubToken))
            {
                Console.Write("Token invalid! Please enter again: ");
                githubToken = Console.ReadLine()?.Trim();
            }
        }

        static async Task<bool> TestToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            try
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UE5Downloader/1.0");

                var response = await client.GetAsync("https://api.github.com/repos/EpicGames/UnrealEngine");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        static string SelectBranch()
        {
            Console.WriteLine("\n===============================================");
            Console.WriteLine("Select UE5 branch to download:");
            Console.WriteLine("===============================================");
            Console.WriteLine("[1] ue5-main (Latest development)");
            Console.WriteLine("[2] 5.8 (Stable release)");
            Console.WriteLine("[3] 5.9 (Stable release)");
            Console.WriteLine("[4] 5.9 (Stable release fallback!!)");
            Console.WriteLine("5: 5.8 only if choosen");

            Console.Write("\nEnter choice (1-4): ");

            string choice = Console.ReadLine();
            return choice == "1" ? "ue5-main" : "5.8";
            return choice == "3" ? "ue5-main" : "5.9";
            return choice == "4" ? "5.9" : "5.9";
            return choice == "5" ? "5.8" : "5.8";
            //fallback
        }

        static void SetInstallDirectory()
        {
            string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "UnrealEngine");
            Console.Write($"\nInstall directory [{defaultDir}]: ");
            string input = Console.ReadLine();
            installDir = string.IsNullOrEmpty(input) ? defaultDir : input;

            Directory.CreateDirectory(installDir);
        }

        static async Task DownloadAndInstallUE5(string branch)
        {
            Console.WriteLine($"\n===============================================");
            Console.WriteLine($"Downloading UE5 - {branch} branch");
            Console.WriteLine($"===============================================\n");

            string tempFile = Path.GetTempFileName() + ".zip";
            string apiUrl = $"https://api.github.com/repos/EpicGames/UnrealEngine/zipball/{branch}";

            Console.WriteLine("[1/5] Downloading source code...");
            Console.WriteLine($"This may take 10-30 minutes (branch size: 500MB-2GB)");

            await DownloadFileWithProgress(apiUrl, tempFile);

            // Check file size
            var fileInfo = new FileInfo(tempFile);
            if (fileInfo.Length < 1000000) // Less than 1MB = error
            {
                Console.WriteLine("\n❌ Download failed! File too small.");
                Console.WriteLine("This usually means:");
                Console.WriteLine("  - Token doesn't have 'repo' scope");
                Console.WriteLine("  - GitHub account not linked to Epic Games");
                Console.WriteLine("  - Need to accept organization invite");
                Console.WriteLine("\nCheck your GitHub notifications: https://github.com/notifications");
                File.Delete(tempFile);
                return;
            }

            Console.WriteLine($"\n✅ Downloaded: {FormatFileSize(fileInfo.Length)}");

            Console.WriteLine("\n[2/5] Extracting source code...");
            await ExtractZipFile(tempFile, installDir);

            Console.WriteLine("\n[3/5] Organizing files...");
            FixFolderStructure();

            Console.WriteLine("\n[4/5] Running Setup.bat...");
            bool setupSuccess = await RunBatchFile("Setup.bat", "Setting up UE5 dependencies...");

            if (setupSuccess)
            {
                Console.WriteLine("\n[5/5] Running GenerateProjectFiles.bat...");
                bool generateSuccess = await RunBatchFile("GenerateProjectFiles.bat", "Generating Visual Studio project files...");

                if (generateSuccess)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n===============================================");
                    Console.WriteLine($"✅ UE5 - {branch} INSTALLATION COMPLETE!");
                    Console.WriteLine($"📍 Location: {installDir}");
                    Console.WriteLine("\nYou can now:");
                    Console.WriteLine($"  1. Open {installDir}\\UE5.sln in Visual Studio");
                    Console.WriteLine("  2. Build the solution");
                    Console.WriteLine("  3. Start developing with Unreal Engine 5!");
                    Console.WriteLine("===============================================");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n❌ GenerateProjectFiles.bat failed!");
                    Console.WriteLine("You may need to run it manually from:");
                    Console.WriteLine($"   cd {installDir}");
                    Console.WriteLine("   GenerateProjectFiles.bat");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n❌ Setup.bat failed!");
                Console.WriteLine("Please check the error messages above.");
                Console.ResetColor();
            }

            // Cleanup
            File.Delete(tempFile);
        }

        static async Task DownloadFileWithProgress(string url, string outputPath)
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    var bytesRead = 0L;
                    var totalRead = 0L;
                    var lastProgress = -1;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, (int)bytesRead);
                        totalRead += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = (int)((double)totalRead / totalBytes * 100);
                            if (progress != lastProgress && progress % 10 == 0)
                            {
                                Console.Write($"\r   Progress: {progress}% ({FormatFileSize(totalRead)} / {FormatFileSize(totalBytes)})");
                                lastProgress = progress;
                            }
                        }
                    }
                }
            }
        }

        static async Task ExtractZipFile(string zipPath, string extractPath)
        {
            await Task.Run(() =>
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var totalEntries = archive.Entries.Count;
                    var extracted = 0;

                    foreach (var entry in archive.Entries)
                    {
                        var destinationPath = Path.Combine(extractPath, entry.FullName);

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                            entry.ExtractToFile(destinationPath, true);
                        }

                        extracted++;
                        if (extracted % 100 == 0 || extracted == totalEntries)
                        {
                            Console.Write($"\r   Extracting: {extracted}/{totalEntries} files");
                        }
                    }
                }
            });
            Console.WriteLine();
        }

        static void FixFolderStructure()
        {
            // GitHub adds a prefix folder like "UnrealEngine-ue5-main" or "EpicGames-UnrealEngine-xxxxx"
            var subfolders = Directory.GetDirectories(installDir, "*UnrealEngine*");
            foreach (var subfolder in subfolders)
            {
                Console.WriteLine($"   Moving contents from {Path.GetFileName(subfolder)}");
                foreach (var file in Directory.GetFiles(subfolder))
                {
                    var destFile = Path.Combine(installDir, Path.GetFileName(file));
                    if (File.Exists(destFile)) File.Delete(destFile);
                    File.Move(file, destFile);
                }

                foreach (var dir in Directory.GetDirectories(subfolder))
                {
                    var destDir = Path.Combine(installDir, Path.GetFileName(dir));
                    if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                    Directory.Move(dir, destDir);
                }

                Directory.Delete(subfolder);
                break; // Only handle the first one
            }
        }

        static async Task<bool> RunBatchFile(string batchFileName, string description)
        {
            string batchPath = Path.Combine(installDir, batchFileName);

            if (!File.Exists(batchPath))
            {
                Console.WriteLine($"   ❌ {batchFileName} not found at {batchPath}");
                return false;
            }

            Console.WriteLine($"   {description}");
            Console.WriteLine($"   Running: {batchFileName}");

            var processInfo = new ProcessStartInfo
            {
                FileName = batchPath,
                WorkingDirectory = installDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false
            };

            using (var process = new Process { StartInfo = processInfo })
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine($"     {e.Data}");
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine($"     ERROR: {e.Data}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    Console.WriteLine($"   ✅ {batchFileName} completed successfully!");
                    return true;
                }
                else
                {
                    Console.WriteLine($"   ❌ {batchFileName} exited with code {process.ExitCode}");
                    return false;
                }
            }
        }

        static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}