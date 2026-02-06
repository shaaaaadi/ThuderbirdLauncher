using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;           // for WebUtility.UrlEncode (subject/body)
using System.Runtime.InteropServices;

    public static class MailLauncher
    {
        /// <summary>
        /// Opens Thunderbird compose window with prefilled To/Subject/Body and attachments.
        /// Requires Thunderbird to be installed. Does not send automatically.
        /// </summary>
        /// <param name="mailTo">Single recipient (use comma to separate multiple recipients if needed).</param>
        /// <param name="subject">Email subject (plain text).</param>
        /// <param name="body">Email body (plain text).</param>
        /// <param name="attachmentPaths">List of absolute file paths for attachments.</param>
        public static void ComposeWithThunderbird(
            string mailTo,
            string subject,
            string body,
            IEnumerable<string> attachmentPaths)
        {
            // 1) Locate Thunderbird executable (typical Windows paths).
            string exePath = GuessThunderbirdPath();
            if (!File.Exists(exePath))
                throw new FileNotFoundException("Mozilla Thunderbird executable not found.", exePath);

            // 2) Build -compose value. Fields are comma-separated.
            //    Thunderbird accepts file URLs for attachments. We'll convert paths to file:// URLs
            //    and URL-encode them to avoid issues with spaces/commas. (See notes.)
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

                    // Validate file exists; comment this out if you want to allow non-existing paths.
                    if (!File.Exists(path))
                        throw new FileNotFoundException("Attachment not found.", path);

                    // Convert to file:// URL and URL-encode the local path.
                    // Using Uri handles drive letters and slashes properly.
                    var fileUri = new Uri(path, UriKind.Absolute);
                    // Uri.AbsoluteUri returns a properly escaped file URL (spaces -> %20, commas -> %2C, etc.).
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
            // Common install locations on Windows (64-bit first, then 32-bit).
            var candidates = new[]
            {
            @"C:\Program Files\Mozilla Thunderbird\thunderbird.exe",
            @"C:\Program Files (x86)\Mozilla Thunderbird\thunderbird.exe"
        };

            foreach (var c in candidates)
                if (File.Exists(c)) return c;

            // Fallback: let PATH resolve it if available.
            return "thunderbird.exe";
        }

        /// <summary>
        /// Escapes a value for placement inside single quotes in the -compose string.
        /// Thunderbird’s -compose uses comma-separated key=value entries; we guard against
        /// embedded single quotes by doubling them (') -> (\') style would be interpreted by cmd,
        /// so we replace single quote with escaped sequence Thunderbird tolerates.
        /// Minimal approach: replace single quote with U+2019 or double it.
        /// </summary>
        private static string EscapeComposeValue(string value)
        {
            if (value == null) return string.Empty;
            // Replace single quotes with a typographic apostrophe to avoid breaking the quoted field.
            // Alternatively, you can double single quotes: value.Replace("'", "''")
            return value.Replace("'", "’");
        }
    }

