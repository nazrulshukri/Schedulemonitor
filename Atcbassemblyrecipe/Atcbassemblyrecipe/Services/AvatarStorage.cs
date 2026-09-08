using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Atcbassemblyrecipe.Services
{
    public interface IAvatarStorage
    {
        string? GetUrl(string userId);
        Task<(bool Success, string Message)> SaveAsync(string userId, IFormFile? photo);
        (bool Success, string Message) SetPreset(string userId, string? presetId);
        void Delete(string userId);
    }

    // Built-in avatar choices shipped with the app (wwwroot/images/avatars/*.svg).
    // Picking one copies that file into the user's avatar slot, so preset and
    // uploaded photos are read back through exactly the same path.
    public static class PresetAvatars
    {
        public static readonly string[] All =
        [
            "avatar-wafer",
            "avatar-die",
            "avatar-package",
            "avatar-leadframe",
            "avatar-probe",
            "avatar-robot",
            "avatar-microscope",
            "avatar-operator"
        ];

        // Shown as the picker tooltip and the image alt text. Without this the
        // page fell back to the raw file id ("avatar-teal"), which is what the
        // broken-image alt text was showing.
        public static string Label(string presetId) => presetId switch
        {
            "avatar-wafer" => "Silicon wafer",
            "avatar-die" => "Chip die",
            "avatar-package" => "IC package",
            "avatar-leadframe" => "Lead frame",
            "avatar-probe" => "Probe card",
            "avatar-robot" => "Handler arm",
            "avatar-microscope" => "Inspection scope",
            "avatar-operator" => "Cleanroom operator",
            _ => presetId
        };

        public static string Url(string presetId) => $"~/images/avatars/{presetId}.svg";
    }

    // Profile photos live on disk under wwwroot/uploads/avatars - TBLACCESS has no
    // column for them, so there is nothing to store in the database beyond the
    // user's id, which is also the filename.
    public class AvatarStorage : IAvatarStorage
    {
        private const long MaxBytes = 2 * 1024 * 1024;

        // Uploads deliberately exclude .svg: an SVG is executable markup and would let
        // an uploaded file run script in the browser. Presets are .svg but ship with
        // the app, so they are never attacker-supplied.
        private static readonly string[] UploadExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];
        private static readonly string[] ReadableExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"];

        private readonly string _avatarsRoot;
        private readonly string _presetsRoot;

        public AvatarStorage(IWebHostEnvironment environment)
        {
            _avatarsRoot = Path.Combine(environment.WebRootPath, "uploads", "avatars");
            _presetsRoot = Path.Combine(environment.WebRootPath, "images", "avatars");
            Directory.CreateDirectory(_avatarsRoot);
        }

        public string? GetUrl(string userId)
        {
            var existing = FindExistingFile(userId);
            if (existing is null)
            {
                return null;
            }

            var fileName = Path.GetFileName(existing);
            var version = File.GetLastWriteTimeUtc(existing).Ticks;
            return $"~/uploads/avatars/{fileName}?v={version}";
        }

        public async Task<(bool Success, string Message)> SaveAsync(string userId, IFormFile? photo)
        {
            if (photo is null || photo.Length == 0)
            {
                return (false, "Choose a photo to upload.");
            }

            if (photo.Length > MaxBytes)
            {
                return (false, "Photo must be 2 MB or smaller.");
            }

            var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!UploadExtensions.Contains(extension))
            {
                return (false, "Only PNG, JPG, GIF, or WEBP photos are allowed.");
            }

            Delete(userId);

            var destination = Path.Combine(_avatarsRoot, Sanitize(userId) + extension);
            await using var stream = File.Create(destination);
            await photo.CopyToAsync(stream);

            return (true, "Photo updated.");
        }

        public (bool Success, string Message) SetPreset(string userId, string? presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId) || !PresetAvatars.All.Contains(presetId))
            {
                return (false, "Choose one of the built-in avatars.");
            }

            var source = Path.Combine(_presetsRoot, presetId + ".svg");
            if (!File.Exists(source))
            {
                return (false, "That avatar image is missing from the app.");
            }

            Delete(userId);
            File.Copy(source, Path.Combine(_avatarsRoot, Sanitize(userId) + ".svg"), overwrite: true);

            return (true, "Avatar updated.");
        }

        public void Delete(string userId)
        {
            foreach (var file in FindAllFiles(userId))
            {
                File.Delete(file);
            }
        }

        private string? FindExistingFile(string userId)
        {
            return FindAllFiles(userId).FirstOrDefault();
        }

        private IEnumerable<string> FindAllFiles(string userId)
        {
            var safeId = Sanitize(userId);
            return ReadableExtensions
                .Select(extension => Path.Combine(_avatarsRoot, safeId + extension))
                .Where(File.Exists);
        }

        private static string Sanitize(string userId)
        {
            var chars = userId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray();
            return chars.Length == 0 ? "unknown" : new string(chars).ToUpperInvariant();
        }
    }
}
