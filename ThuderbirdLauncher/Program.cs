using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace ThuderbirdLauncher
{
    public static class Program
    {
        // Exit codes
        private const int EXIT_OK = 0;
        private const int EXIT_BAD_ARGS = 2;
        private const int EXIT_NOT_FOUND = 3;
        private const int EXIT_ERROR = 1;

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h") || HasFlag(args, "/?"))
                {
                    PrintHelp();
                    return EXIT_OK;
                }

                // Parse arguments
                string to = GetValue(args, "--to");
                string subject = GetValue(args, "--subject") ?? "";
                string body = GetValue(args, "--body") ?? "";

                // Multiple --attach can be provided
                var attachments = GetValues(args, "--attach");

                // Basic validation
                if (string.IsNullOrWhiteSpace(to))
                {
                    Console.Error.WriteLine("Error: --to is required.");
                    Console.Error.WriteLine();
                    PrintHelp();
                    return EXIT_BAD_ARGS;
                }

                // Optional: validate attachment paths exist (you can relax this if needed)
                foreach (var p in attachments)
                {
                    if (!File.Exists(p))
                    {
                        Console.Error.WriteLine($"Error: Attachment not found: {p}");
                        return EXIT_NOT_FOUND;
                    }
                }

                // Launch Thunderbird compose
                MailLauncher.ComposeWithThunderbird(to, subject, body, attachments);
                return EXIT_OK;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (!string.IsNullOrWhiteSpace(ex.FileName))
                    Console.Error.WriteLine($"Path: {ex.FileName}");
                return EXIT_NOT_FOUND;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unexpected error:");
                Console.Error.WriteLine(ex.ToString());
                return EXIT_ERROR;
            }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string GetValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) return args[i + 1];
                    return null;
                }
                // Also support --key=value form
                if (args[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(key.Length + 1);
                }
            }
            return null;
        }

        private static IEnumerable<string> GetValues(string[] args, string key)
        {
            var list = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) list.Add(args[i + 1]);
                }
                else if (args[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(args[i].Substring(key.Length + 1));
                }
            }
            return list;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  TbComposeCli --to <email> [--subject <text>] [--body <text>] [--attach <path>]...");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --to <email>           Recipient email address (required).");
            Console.WriteLine("  --subject <text>       Message subject (optional).");
            Console.WriteLine("  --body <text>          Message body (optional). Use quotes for spaces/newlines.");
            Console.WriteLine("  --attach <path>        Attachment file path. Repeat for multiple files.");
            Console.WriteLine("  --help, -h, /?         Show this help.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine(@"  TbComposeCli --to user@example.com --subject ""Quarterly report"" --body ""Hi,\r\nSee files.""");
            Console.WriteLine(@"  TbComposeCli --to user@example.com --attach ""C:\Temp\Report Q4.pdf"" --attach ""C:\Temp\Revenue,Summary.xlsx""");
            Console.WriteLine();
            Console.WriteLine("Notes:");
            Console.WriteLine("  * This opens Thunderbird's Compose window; user must click Send.");
            Console.WriteLine("  * Attachments are passed as file:// URLs internally to avoid issues with spaces/commas.");
        }
    }

    public static class MailLauncher
    {
        /// <summary>
        /// Opens Thunderbird compose window with prefilled To/Subject/Body and attachments.
        /// Requires Thunderbird to be installed. Does not send automatically.
        /// </summary>
        public static void ComposeWithThunderbird(
            string mailTo,
            string subject,
            string body,
            IEnumerable<string> attachmentPaths)
        {
            string exePath = GuessThunderbirdPath();
            if (!File.Exists(exePath) && !exePath.Equals("thunderbird.exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("Mozilla Thunderbird executable not found.", exePath);

            var composeParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(mailTo))
                composeParts.Add($"to='{EscapeComposeValue(mailTo)}'");

            if (subject != null)
                composeParts.Add($"subject='{EscapeComposeValue(subject)}'");

            if (body != null)
                composeParts.Add($"body='{EscapeComposeValue(body)}'");

            if (attachmentPaths != null)
            {
                foreach (var path in attachmentPaths)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    // We validated existence in Main; do a soft check anyway
                    // Convert to file:// URL and let URI handle proper escaping (spaces -> %20, commas -> %2C).
                    var fileUri = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                    string fileUrl = fileUri.AbsoluteUri;

                    composeParts.Add($"attachment='{fileUrl}'");
                }
            }

            string composeArg = string.Join(",", composeParts);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"-compose \"{composeArg}\"",
                UseShellExecute = false
            };

            Process.Start(psi);
        }

        private static string GuessThunderbirdPath()
        {
            var candidates = new[]
            {
                @"C:\Program Files\Mozilla Thunderbird\thunderbird.exe",
                @"C:\Program Files (x86)\Mozilla Thunderbird\thunderbird.exe"
            };

            foreach (var c in candidates)
                if (File.Exists(c)) return c;

            // Fallback to PATH
            return "thunderbird.exe";
        }

        /// <summary>
        /// Escapes single quotes to avoid breaking the compose string.
        /// Here we replace a straight apostrophe with a typographic one.
        /// </summary>
        private static string EscapeComposeValue(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("'", "’");
        }
    }
}