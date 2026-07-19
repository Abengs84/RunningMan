using System;
using System.Collections.Generic;

namespace RunningMan
{
    /// <summary>
    /// Recent command feedback shown in the F6 GUI log panel.
    /// </summary>
    public static class RaceGuiLog
    {
        private const int MaxLines = 30;
        private static readonly List<string> _lines = new List<string>();

        public static event Action Updated;

        public static IReadOnlyList<string> Lines => _lines;

        public static void Add(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            foreach (var line in message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                _lines.Insert(0, line.Trim());
            }

            while (_lines.Count > MaxLines)
            {
                _lines.RemoveAt(_lines.Count - 1);
            }

            Updated?.Invoke();
        }

        public static void Clear()
        {
            _lines.Clear();
            Updated?.Invoke();
        }
    }
}
