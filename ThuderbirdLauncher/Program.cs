using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;

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
                body = body?.Replace("\\r\\n", "\r\n").Replace("\\n", "\n").Replace("\\r", "\r");
                // Gather raw values of --attach (supports --attach <val> and --attach=<val>)
                var attachRaw = GetValues(args, "--attach");

                // Expand semicolon-delimited lists into individual paths
                var attachments = ExpandAttachmentArgs(attachRaw).ToList();

                // Basic validation
                if (string.IsNullOrWhiteSpace(to))
                {
                    Console.Error.WriteLine("Error: --to is required.");
                    Console.Error.WriteLine();
                    PrintHelp();
                    return EXIT_BAD_ARGS;
                }

                // Validate that attachment paths exist
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

        /// <summary>
        /// Expands one or more --attach values into individual paths.
        /// Supports semicolon-delimited lists like: "C:\a.pdf;D:\b.jpg"
        /// To include a literal semicolon in a path, escape it as "\;".
        /// </summary>
        private static IEnumerable<string> ExpandAttachmentArgs(IEnumerable<string> rawValues)
        {
            foreach (var raw in rawValues)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                foreach (var path in SplitSemicolonList(raw))
                {
                    var trimmed = path?.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        yield return trimmed;
                }
            }
        }

        /// <summary>
        /// Splits by semicolon (;) but allows escaping a semicolon using '\;'.
        /// All other backslashes are preserved (important for Windows paths).
        /// Example:  "C:\Temp\file1.txt;C:\Data\semi\;colon.txt"
        ///           -> ["C:\Temp\file1.txt", "C:\Data\semi;colon.txt"]
        /// </summary>
        private static IEnumerable<string> SplitSemicolonList(string s)
        {
            var results = new List<string>();
            var sb = new StringBuilder();

            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];

                // Handle escaped semicolon "\;"
                if (ch == '\\' && i + 1 < s.Length && s[i + 1] == ';')
                {
                    sb.Append(';');
                    i++; // skip the ';' after backslash
                }
                else if (ch == ';')
                {
                    results.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }

            results.Add(sb.ToString());
            return results;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  TbComposeCli --to <email> [--subject <text>] [--body <text>] [--attach <files>]...");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --to <email>           Recipient email address (required).");
            Console.WriteLine("  --subject <text>       Message subject (optional).");
            Console.WriteLine("  --body <text>          Message body (optional). Use quotes for spaces/newlines.");
            Console.WriteLine("  --attach <files>       One or more attachment file paths separated by ';'.");
            Console.WriteLine("                         You can repeat --attach. To include a literal ';' in a file name, use \\;.");
            Console.WriteLine("  --help, -h, /?         Show this help.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine(@"  TbComposeCli --to user@example.com --subject ""Quarterly report"" --body ""Hi,\r\nSee files.""");
            Console.WriteLine(@"  TbComposeCli --to user@example.com --attach ""C:\Temp\Report Q4.pdf;C:\Temp\Revenue Summary.xlsx""");
            Console.WriteLine(@"  TbComposeCli --to user@example.com --attach ""D:\Docs\a.pdf"" --attach ""D:\Docs\b.jpg;D:\Docs\c.xlsx""");
            Console.WriteLine(@"  TbComposeCli --to user@example.com --attach ""C:\Data\semi\;colon.txt""   (file name contains ';')");
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

                var fileUrls = new List<string>();

                foreach (var path in attachmentPaths)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    string absolutePath = Path.GetFullPath(path);

                    // Build a proper file URI (handles spaces, commas, etc.).
                    var fileUri = new Uri(absolutePath);
                    fileUrls.Add(fileUri.AbsoluteUri);
                }

                if (fileUrls.Count > 0)
                {
                    // IMPORTANT: one attachment field, multiple URLs separated by commas
                    var joined = string.Join(",", fileUrls);
                    composeParts.Add($"attachment='{joined}'");
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
