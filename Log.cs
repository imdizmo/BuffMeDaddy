using BepInEx.Logging;

namespace Jatify.BuffMeDaddy
{
    internal static class Log
    {
        private static ManualLogSource _s;
        internal static void Bind(ManualLogSource s) { if (_s == null) _s = s; }
        internal static void Info(string m) { _s?.LogInfo(m); }
        internal static void Warn(string m) { _s?.LogWarning(m); }
    }
}
