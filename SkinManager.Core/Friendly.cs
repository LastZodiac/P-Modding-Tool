// Turns what went wrong into a sentence a player can act on. The app's own errors are already written that way; errors
// from Windows or .NET ("The process cannot access the file...", "End of Central Directory record could not be found")
// are recognised by kind and explained; anything else says plainly that it's a problem in the app, with the details kept
// in the log for a bug report.
using System;
using System.IO;
using System.Net;

namespace SkinManager.Core
{
    /// <summary>The app's own message for the user: shown as it is (an IOException/InvalidOperationException the app
    /// throws with a sentence written for the user is also shown as it is).</summary>
    public class PlainException : Exception
    {
        public PlainException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>A save whose new SD card is already in place, but where something after that went wrong: the card HAS
    /// changed, so the message must not say it hasn't.</summary>
    public sealed class SavedButException : PlainException
    {
        public SavedButException(string message, Exception inner) : base(message, inner) { }
    }

    public static class Friendly
    {
        /// <summary>A sentence about what went wrong and what to do.</summary>
        public static string Explain(Exception e)
        {
            if (e == null) return "Something went wrong.";
            if (e is AggregateException ag && ag.InnerExceptions.Count == 1) return Explain(ag.InnerException);
            if (e is System.Reflection.TargetInvocationException ti && ti.InnerException != null) return Explain(ti.InnerException);
            if (e is PlainException) return e.Message;
            int code = e.HResult & 0xFFFF;
            switch (e)
            {
                case FileNotFoundException f:
                    return $"A file the app needs is missing{(string.IsNullOrEmpty(f.FileName) ? "" : $" ({Path.GetFileName(f.FileName)})")}. If it's part of the app, reinstalling it puts it back.";
                case DirectoryNotFoundException _:
                    return "A folder the app needs isn't there any more (was it moved or deleted?).";
                case PathTooLongException _:
                    return "A file's path is too long for Windows. Moving the app's folder closer to the top of the drive (like C:\\Games) fixes this.";
                case UnauthorizedAccessException _:
                    return "Windows didn't let the app change a file. It may be read-only, or in a protected folder (like Program Files): moving the app's folder somewhere like Documents fixes this.";
                case InvalidDataException _:
                    return "This file looks damaged or wasn't fully downloaded. Downloading it again usually fixes it.";
                case WebException w:
                    return w.Status == WebExceptionStatus.Timeout ? "The website took too long to answer. Try again in a bit."
                         : w.Response is HttpWebResponse r && (int)r.StatusCode == 404 ? "That file isn't on the website any more."
                         : "Couldn't reach the website. Check your internet connection and try again.";
                case OperationCanceledException _:
                    return "It was stopped before it finished.";
                case OutOfMemoryException _:
                    return "Your PC ran out of memory for this. Closing other programs and trying again usually helps.";
                case IOException _ when code == 0x20 || code == 0x21:
                    return "Another program has this file open (often Brawl Online or Dolphin). Close it and try again.";
                case IOException _ when code == 0x70 || code == 0x27:
                    return "Your PC's drive is full. Free up some space and try again.";
                case IOException _ when code == 0x15:
                    return "The drive isn't ready (was a USB drive or SD card unplugged?).";
                case IOException _ when !LooksTechnical(e.Message):
                    return e.Message;
                case InvalidOperationException _ when !LooksTechnical(e.Message):
                    return e.Message;
            }
            return "Something unexpected went wrong inside the app. Reporting it (Settings > Report a problem) gets it fixed.";
        }

        /// <summary>A message written by Windows/.NET rather than by the app (the app's own sentences are plain).</summary>
        static bool LooksTechnical(string m) =>
            string.IsNullOrEmpty(m) || m.Contains("HRESULT") || m.Contains("0x8") || m.Contains("Exception") || m.Contains("System.") ||
            m.StartsWith("The process cannot access") || m.StartsWith("Could not find") || m.StartsWith("Access to the path") ||
            m.Contains("Central Directory") || m.Contains("Collection was modified") || m.Contains("Sequence contains") ||
            m.Contains("Object reference") || m.Contains("Index was outside") || m.Contains("Specified argument") || m.Contains("Unable to ");
    }
}
