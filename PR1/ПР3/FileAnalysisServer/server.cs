using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FileAnalysisServer
{
    public class FileAnalysisResult
    {
        public string FileName { get; set; } = string.Empty; // Инициализация пустой строкой
        public int LineCount { get; set; }
        public int WordCount { get; set; }
        public int CharCount { get; set; }
        public DateTime AnalysisTime { get; set; }

        public override string ToString()
        {
            return $"Имя файла: {FileName}\n" +
                   $"Строк: {LineCount}, Слов: {WordCount}, Символов: {CharCount}";
        }
    }

    public class FileAnalysisServer
    {
        private readonly int _port;
        private readonly string _storagePath;
        private readonly string _resultsPath;
        private TcpListener? _listener; // Делаем nullable
        private bool _isRunning;
        private readonly object _fileLock = new object();

        public FileAnalysisServer(int port = 8888)
        {
            _port = port;
            _storagePath = Path.Combine(Directory.GetCurrentDirectory(), "ReceivedFiles");
            _resultsPath = Path.Combine(Directory.GetCurrentDirectory(), "AnalysisResults");
            
            Directory.CreateDirectory(_storagePath);
            Directory.CreateDirectory(_resultsPath);
        }

        public async Task StartAsync()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                _isRunning = true;

                Console.WriteLine($"[СЕРВЕР] Запущен на порту {_port}");
                Console.WriteLine($"[СЕРВЕР] Директория для файлов: {_storagePath}");
                Console.WriteLine($"[СЕРВЕР] Директория для результатов: {_resultsPath}");
                Console.WriteLine("[СЕРВЕР] Ожидание подключений...\n");

                while (_isRunning)
                {
                    try
                    {
                        if (_listener == null) throw new InvalidOperationException("Сервер не инициализирован");
                        
                        var client = await _listener.AcceptTcpClientAsync();
                        _ = Task.Run(() => HandleClientAsync(client));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ОШИБКА] Принятие клиента: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ОШИБКА] Запуск сервера: {ex.Message}");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            Console.WriteLine("[СЕРВЕР] Остановлен");
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            string clientEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "неизвестен";
            Console.WriteLine($"[КЛИЕНТ] Подключен: {clientEndPoint}");

            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    // 1. Читаем имя файла (до символа '\0')
                    byte[] fileNameBuffer = new byte[1024];
                    int fileNameIndex = 0;
                    
                    while (fileNameIndex < fileNameBuffer.Length)
                    {
                        int readByte = stream.ReadByte();
                        if (readByte == -1) throw new Exception("Соединение закрыто");
                        if (readByte == 0) break; // Конец имени файла
                        
                        fileNameBuffer[fileNameIndex] = (byte)readByte;
                        fileNameIndex++;
                    }
                    
                    string fileName = Encoding.UTF8.GetString(fileNameBuffer, 0, fileNameIndex);
                    Console.WriteLine($"[КЛИЕНТ] Получение файла: {fileName}");

                    // 2. Читаем размер файла (4 байта)
                    byte[] sizeBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(sizeBuffer, 0, 4);
                    if (bytesRead != 4) throw new Exception("Не удалось прочитать размер файла");
                    
                    int fileSize = BitConverter.ToInt32(sizeBuffer, 0);
                    Console.WriteLine($"[КЛИЕНТ] Размер файла: {fileSize} байт");

                    // 3. Получаем содержимое файла
                    string uniqueFileName = GetUniqueFileName(fileName);
                    string filePath = Path.Combine(_storagePath, uniqueFileName);
                    
                    await ReceiveFileAsync(stream, filePath, fileSize);
                    
                    // 4. Анализируем файл
                    var analysisResult = await AnalyzeFileAsync(filePath, uniqueFileName);
                    
                    // 5. Сохраняем результат
                    await SaveAnalysisResultAsync(analysisResult);
                    
                    // 6. Отправляем результат клиенту
                    await SendResultToClientAsync(stream, analysisResult);
                    
                    Console.WriteLine($"[КЛИЕНТ] Обработка завершена: {uniqueFileName}\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ОШИБКА] Обработка клиента {clientEndPoint}: {ex.Message}");
            }
        }

        private string GetUniqueFileName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{name}_{timestamp}{extension}";
        }

        private async Task ReceiveFileAsync(NetworkStream stream, string filePath, int fileSize)
        {
            using (FileStream fileStream = File.Create(filePath))
            {
                byte[] buffer = new byte[8192];
                int totalBytesRead = 0;
                int lastProgress = 0;

                while (totalBytesRead < fileSize)
                {
                    int bytesToRead = Math.Min(buffer.Length, fileSize - totalBytesRead);
                    int bytesRead = await stream.ReadAsync(buffer, 0, bytesToRead);
                    
                    if (bytesRead == 0) throw new Exception("Соединение закрыто при получении файла");
                    
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;
                    
                    int progress = (totalBytesRead * 100) / fileSize;
                    if (progress >= lastProgress + 10)
                    {
                        Console.WriteLine($"[ПРОГРЕСС] Получено {progress}% файла");
                        lastProgress = progress;
                    }
                }
            }
            
            Console.WriteLine($"[СЕРВЕР] Файл сохранен: {filePath}");
        }

        private async Task<FileAnalysisResult> AnalyzeFileAsync(string filePath, string originalFileName)
        {
            string content = await File.ReadAllTextAsync(filePath);
            
            var result = new FileAnalysisResult
            {
                FileName = originalFileName,
                LineCount = content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length,
                WordCount = content.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':' },
                                          StringSplitOptions.RemoveEmptyEntries).Length,
                CharCount = content.Length,
                AnalysisTime = DateTime.Now
            };

            Console.WriteLine($"[АНАЛИЗ] {originalFileName}: {result.LineCount} строк, " +
                            $"{result.WordCount} слов, {result.CharCount} символов");

            return result;
        }

        private async Task SaveAnalysisResultAsync(FileAnalysisResult result)
        {
            string resultFilePath = Path.Combine(_resultsPath, "analysis_results.txt");
            
            lock (_fileLock)
            {
                string resultText = $"[{result.AnalysisTime:yyyy-MM-dd HH:mm:ss}]\n" +
                                   $"Файл: {result.FileName}\n" +
                                   $"Строк: {result.LineCount}, Слов: {result.WordCount}, Символов: {result.CharCount}\n" +
                                   $"{new string('-', 50)}\n";
                
                File.AppendAllText(resultFilePath, resultText);
            }
            
            string jsonPath = Path.Combine(_resultsPath, $"{Path.GetFileNameWithoutExtension(result.FileName)}_result.json");
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonPath, json);
        }

        private async Task SendResultToClientAsync(NetworkStream stream, FileAnalysisResult result)
        {
            string resultMessage = JsonSerializer.Serialize(result);
            byte[] data = Encoding.UTF8.GetBytes(resultMessage);
            
            byte[] lengthPrefix = BitConverter.GetBytes(data.Length);
            await stream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
            
            Console.WriteLine($"[СЕРВЕР] Результат отправлен клиенту");
        }

        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== СЕРВЕР АНАЛИЗА ФАЙЛОВ ===\n");

            int port = args.Length > 0 ? int.Parse(args[0]) : 8888;
            var server = new FileAnalysisServer(port);

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                server.Stop();
                Environment.Exit(0);
            };

            try
            {
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[КРИТИЧЕСКАЯ ОШИБКА] {ex.Message}");
            }
        }
    }
}