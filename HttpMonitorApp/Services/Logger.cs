using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HttpMonitorApp.Services
{
    public class Logger
    {
        private readonly List<string> _logs = new();
        private readonly object _lock = new();
        public event Action<string>? LogAdded;

        public void Add(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            lock (_lock)
            {
                _logs.Add(entry);
                LogAdded?.Invoke(entry);
            }
        }

        public void SaveToFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Путь не может быть пустым", nameof(path));

            // Создаём папку если нет
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            lock (_lock)
                File.WriteAllLines(path, _logs);
        }

        public IEnumerable<string> GetLogs(string? filter = null)
        {
            lock (_lock)
            {
                var result = _logs.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(filter))
                    result = result.Where(l => l.Contains(filter));
                return result.ToList();
            }
        }
    }
}