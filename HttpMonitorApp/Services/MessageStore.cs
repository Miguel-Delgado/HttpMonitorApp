using System.Collections.Concurrent;

namespace HttpMonitorApp.Services
{
    public class MessageStore
    {
        private readonly ConcurrentDictionary<string, string> _messages = new();

        public string Add(string json)
        {
            var id = Guid.NewGuid().ToString();
            _messages[id] = json;
            return id;
        }

        public string Get(string id) => _messages.GetValueOrDefault(id, "Не найдено");
    }
}