using Atcbassemblyrecipe.Data;
using Microsoft.Extensions.Caching.Memory;

namespace Atcbassemblyrecipe.Services
{
    // Turns a TBLACCESS user id (NX487878) into the person's name (Muhammad Nazrul
    // Ahmad Shukri) for display.
    //
    // Grids show one id per row, so this loads the whole directory once and answers
    // from memory - a per-row lookup would mean a database round trip for every
    // line of every page. The cache is short-lived because a name only changes when
    // someone is re-provisioned.
    public interface IUserDirectory
    {
        Task<string> GetDisplayNameAsync(string? userId);
        Task<IReadOnlyDictionary<string, string>> GetAllAsync();
    }

    public class UserDirectory : IUserDirectory
    {
        private const string CacheKey = "user-directory";
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

        private readonly IAccessRepository _accessRepository;
        private readonly IMemoryCache _cache;

        public UserDirectory(IAccessRepository accessRepository, IMemoryCache cache)
        {
            _accessRepository = accessRepository;
            _cache = cache;
        }

        // Falls back to the id itself, so a row written by someone no longer in
        // TBLACCESS still shows something meaningful instead of going blank.
        public async Task<string> GetDisplayNameAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return string.Empty;
            }

            var names = await GetAllAsync();
            return names.TryGetValue(userId.Trim(), out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : userId.Trim();
        }

        public async Task<IReadOnlyDictionary<string, string>> GetAllAsync()
        {
            if (_cache.TryGetValue<IReadOnlyDictionary<string, string>>(CacheKey, out var cached) && cached is not null)
            {
                return cached;
            }

            Dictionary<string, string> names;
            try
            {
                var profiles = await _accessRepository.GetAllProfilesAsync();
                names = profiles
                    .Where(profile => !string.IsNullOrWhiteSpace(profile.UserId))
                    .GroupBy(profile => profile.UserId.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().UserName,
                        StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // Never let a directory read break a page: callers fall back to the id.
                names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            _cache.Set(CacheKey, (IReadOnlyDictionary<string, string>)names, CacheFor);
            return names;
        }
    }
}
