using System.Collections.Generic;

namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// Table for localized strings, can be swapped at runtime.
    /// </summary>
    public class LocalizedStringTable
    {
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();

        /// <summary>
        /// Gets the localized string for the given key, or the key itself if not found.
        /// </summary>
        public string Get(string key) => _strings.TryGetValue(key, out var value) ? value : key;

        /// <summary>
        /// Sets or adds a localized string.
        /// </summary>
        public void Set(string key, string value) => _strings[key] = value;

        /// <summary>
        /// Clears all strings.
        /// </summary>
        public void Clear() => _strings.Clear();
    }
}