using System;
using System.Collections.Generic;
using System.Linq;

namespace Network.Codegen;

internal static class CommandIdManager
{
    public static uint GetOrAssignId(string fullName)
    {
        unchecked
        {
            int hash = 5381;
            foreach (char c in fullName)
            {
                hash = ((hash << 5) + hash) + c;
            }
            return (uint)((Math.Abs(hash) % 65534) + 1);
        }
    }
    public static Dictionary<uint, List<string>> CheckCollisions(IEnumerable<string> names)
    {
        var collisions = new Dictionary<uint, List<string>>();
        foreach (var name in names)
        {
            var id = GetOrAssignId(name);
            if (!collisions.ContainsKey(id)) collisions[id] = new List<string>();
            collisions[id].Add(name);
        }
        return collisions.Where(x => x.Value.Count > 1).ToDictionary(x => x.Key, x => x.Value);
    }
}