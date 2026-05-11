using System;
using System.Collections.Generic;
using System.Linq;

namespace HttpMonitorApp.Services
{
    public class StatsTracker
    {
        private int _getCount, _postCount;
        private double _totalTimeMs;
        private readonly List<DateTime> _timestamps = new();
        private readonly object _lock = new();

        public int GetCount => _getCount;
        public int PostCount => _postCount;
        public double AvgTimeMs => _totalTimeMs / Math.Max(1, _getCount + _postCount);

        public IReadOnlyList<DateTime> Timestamps
        {
            get { lock (_lock) return _timestamps.ToList(); }
        }

        public int GetRequestsLastMinute()
        {
            lock (_lock)
            {
                var minuteAgo = DateTime.Now.AddMinutes(-1);
                return _timestamps.Count(t => t >= minuteAgo);
            }
        }

        public int GetRequestsLastSecond()
        {
            lock (_lock)
            {
                var secondAgo = DateTime.Now.AddSeconds(-1);
                return _timestamps.Count(t => t >= secondAgo);
            }
        }

        public void Record(string method, double timeMs)
        {
            lock (_lock)
            {
                // ИСПРАВЛЕНИЕ: явная проверка POST, иначе игнорируем
                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                    _getCount++;
                else if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                    _postCount++;
                // Остальные методы (PUT, DELETE и т.д.) не считаем

                _totalTimeMs += timeMs;
                _timestamps.Add(DateTime.Now);

                // Очистка старых записей (старше 1 часа)
                var oldTime = DateTime.Now.AddHours(-1);
                _timestamps.RemoveAll(t => t < oldTime);
            }
        }
    }
}