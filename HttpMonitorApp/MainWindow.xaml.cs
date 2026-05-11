using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using HttpMonitorApp.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace HttpMonitorApp
{
    public partial class MainWindow : Window
    {
        private readonly MessageStore _store = new();
        private readonly Logger _logger = new();
        private readonly StatsTracker _stats = new();
        private HttpServer? _server;
        private readonly HttpClient _client = new();

        private readonly ObservableCollection<int> _getData = new();
        private readonly ObservableCollection<int> _postData = new();

        private int _lastGetCount = 0;
        private int _lastPostCount = 0;
        private DispatcherTimer? _monitoringTimer;

        public MainWindow()
        {
            InitializeComponent();

            _client.Timeout = TimeSpan.FromSeconds(30);
            _client.DefaultRequestHeaders.Add("User-Agent", "HttpMonitorApp/1.0");

            for (int i = 0; i < 60; i++)
            {
                _getData.Add(0);
                _postData.Add(0);
            }

            GetChart.Series = new ISeries[]
            {
                new LineSeries<int>
                {
                    Values = _getData,
                    Fill = new SolidColorPaint(new SKColor(0, 0, 255, 50)),
                    Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 2 },
                    GeometrySize = 0,
                    LineSmoothness = 0
                }
            };

            PostChart.Series = new ISeries[]
            {
                new LineSeries<int>
                {
                    Values = _postData,
                    Fill = new SolidColorPaint(new SKColor(255, 0, 0, 50)),
                    Stroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 2 },
                    GeometrySize = 0,
                    LineSmoothness = 0
                }
            };

            _logger.LogAdded += (msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ServerLogBox.AppendText(msg + Environment.NewLine);
                    ServerLogBox.ScrollToEnd();
                });
            };

            _monitoringTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _monitoringTimer.Tick += (s, e) => UpdateMonitoring();
            _monitoringTimer.Start();
        }

        private void UpdateMonitoring()
        {
            GetCountText.Text = _stats.GetCount.ToString();
            PostCountText.Text = _stats.PostCount.ToString();
            AvgTimeText.Text = $"{_stats.AvgTimeMs:F2} ms";

            int newGetRequests = _stats.GetCount - _lastGetCount;
            if (newGetRequests < 0) newGetRequests = 0;
            _lastGetCount = _stats.GetCount;

            int newPostRequests = _stats.PostCount - _lastPostCount;
            if (newPostRequests < 0) newPostRequests = 0;
            _lastPostCount = _stats.PostCount;

            if (_getData.Count >= 60) _getData.RemoveAt(0);
            _getData.Add(newGetRequests);

            if (_postData.Count >= 60) _postData.RemoveAt(0);
            _postData.Add(newPostRequests);
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PortBox.Text, out int port) || port < 1024 || port > 65535)
            {
                MessageBox.Show("Введите порт в диапазоне 1024-65535");
                return;
            }

            try
            {
                _server = new HttpServer(_store, _logger, _stats);
                await _server.StartAsync(port);

                StartBtn.IsEnabled = false;
                StopBtn.IsEnabled = true;
                PortBox.IsEnabled = false;

                // УБРАЛ: _logger.Add($"Сервер запущен на порту {port}"); — дубль из HttpServer
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
                _logger.Add($"Ошибка запуска: {ex.Message}");
            }
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            _server?.Stop();

            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
            PortBox.IsEnabled = true;

            // УБРАЛ: _logger.Add("Сервер остановлен"); — дубль из HttpServer
        }

        private async void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            SendBtn.IsEnabled = false;

            try
            {
                string url = UrlBox.Text.Trim();
                string method = ((ComboBoxItem)MethodBox.SelectedItem).Content.ToString()!;

                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    MessageBox.Show("Некорректный URL");
                    return;
                }

                ResponseBox.Text = "Отправка...";
                HttpResponseMessage response;

                if (method == "GET")
                {
                    response = await _client.GetAsync(url);
                }
                else
                {
                    var body = BodyBox.Text;
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        MessageBox.Show("Тело запроса пустое");
                        return;
                    }

                    try { JsonDocument.Parse(body); }
                    catch (JsonException)
                    {
                        MessageBox.Show("Неверный JSON");
                        return;
                    }

                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    response = await _client.PostAsync(url, content);
                }

                string result = await response.Content.ReadAsStringAsync();
                ResponseBox.Text = $"Status: {(int)response.StatusCode}\n\n{result}";
            }
            catch (Exception ex)
            {
                ResponseBox.Text = $"Ошибка: {ex.Message}";
            }
            finally
            {
                await Task.Delay(300);
                SendBtn.IsEnabled = true;
            }
        }

        private void SaveLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                FileName = "logs.txt",
                DefaultExt = ".txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Сохранить логи"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _logger.SaveToFile(dlg.FileName);
                    MessageBox.Show($"Логи сохранены:\n{dlg.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}");
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _server?.Stop();
            _monitoringTimer?.Stop();
            _client.Dispose();
            base.OnClosing(e);
        }
    }
}