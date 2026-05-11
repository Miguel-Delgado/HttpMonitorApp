using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HttpMonitorApp.Services
{
    public class HttpServer
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _serverTask;
        private readonly MessageStore _store;
        private readonly Logger _logger;
        private readonly StatsTracker _stats;
        private bool _isRunning = false;

        public HttpServer(MessageStore store, Logger logger, StatsTracker stats)
        {
            _store = store;
            _logger = logger;
            _stats = stats;
        }

        public Task StartAsync(int port)
        {
            if (_isRunning)
            {
                // УБРАЛ: _logger.Add("Сервер уже запущен!"); — дубль
                return Task.CompletedTask;
            }

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/");

            try
            {
                _listener.Start();
                _logger.Add($"Сервер запущен на порту {port}");
                _isRunning = true;
            }
            catch (HttpListenerException ex)
            {
                _logger.Add($"Ошибка запуска: {ex.Message} (код {ex.ErrorCode})");
                throw new Exception($"Не удалось запустить сервер. Запустите VS от администратора.", ex);
            }

            _serverTask = Task.Run(() => RunServerLoop(_cts.Token));
            return Task.CompletedTask;
        }

        private async Task RunServerLoop(CancellationToken cancellationToken)
        {
            if (_listener == null) return;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        _ = Task.Run(() => HandleRequest(context));
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (HttpListenerException ex) when (ex.ErrorCode == 995 || cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                            _logger.Add($"Ошибка в цикле: {ex.Message}");
                    }
                }
            }
            finally
            {
                _isRunning = false;
                _logger.Add("Сервер остановлен");
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var method = context.Request.HttpMethod;
            var url = context.Request.RawUrl ?? "/";
            string body = "";

            try
            {
                _logger.Add($"{method} {url}");

                if (method == "POST" && context.Request.HasEntityBody)
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    body = await reader.ReadToEndAsync();
                }

                string response;
                int status = 200;

                if (url == "/status" && method == "GET")
                {
                    response = JsonSerializer.Serialize(new
                    {
                        time = DateTime.Now.ToString("HH:mm:ss"),
                        getRequests = _stats.GetCount,
                        postRequests = _stats.PostCount,
                        avgTime = _stats.AvgTimeMs.ToString("F2")
                    }, new JsonSerializerOptions { WriteIndented = true });
                }
                else if (url == "/messages" && method == "POST")
                {
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        status = 400;
                        response = "Empty body";
                    }
                    else
                    {
                        var id = _store.Add(body);
                        response = JsonSerializer.Serialize(new { id });
                    }
                }
                else
                {
                    status = 404;
                    response = $"Not Found: {url}";
                }

                await SendResponse(context, status, response);
                _stats.Record(method, sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.Add($"Ошибка обработки: {ex.Message}");
            }
        }

        private async Task SendResponse(HttpListenerContext context, int status, string body)
        {
            try
            {
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";
                var buffer = Encoding.UTF8.GetBytes(body);
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                // УБРАЛ: _logger.Add("Сервер уже остановлен"); — дубль
                return;
            }

            // УБРАЛ: _logger.Add("Остановка сервера..."); — дубль

            try
            {
                _cts?.Cancel();
                _serverTask?.Wait(1000);
                _listener?.Stop();
                _listener?.Close();
                _cts?.Dispose();
            }
            catch { }

            _listener = null;
            _cts = null;
            _serverTask = null;
            _isRunning = false;
        }
    }
}