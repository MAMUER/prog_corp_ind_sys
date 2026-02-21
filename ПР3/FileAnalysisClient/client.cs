using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FileAnalysisClient
{
    public class FileAnalysisResult
    {
        public string FileName { get; set; } = string.Empty;
        public int LineCount { get; set; }
        public int WordCount { get; set; }
        public int CharCount { get; set; }
        public DateTime AnalysisTime { get; set; }
    }

    public class FileAnalysisClient
    {
        private readonly string _serverAddress;
        private readonly int _serverPort;

        public FileAnalysisClient(string serverAddress = "127.0.0.1", int serverPort = 8888)
        {
            _serverAddress = serverAddress;
            _serverPort = serverPort;
        }

        public async Task<bool> SendFileAsync(string filePath)
        {
            Console.WriteLine($"\n[КЛИЕНТ] Отправка файла: {filePath}");

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[ОШИБКА] Файл не найден: {filePath}");
                return false;
            }

            TcpClient? client = null;
            
            try
            {
                client = new TcpClient();
                await client.ConnectAsync(_serverAddress, _serverPort);
                Console.WriteLine($"[КЛИЕНТ] Подключен к серверу {_serverAddress}:{_serverPort}");

                using (NetworkStream stream = client.GetStream())
                {
                    // 1. Отправка имени файла с нулевым символом в конце
                    string fileName = Path.GetFileName(filePath);
                    byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);
                    await stream.WriteAsync(fileNameBytes, 0, fileNameBytes.Length);
                    await stream.WriteAsync(new byte[] { 0 }, 0, 1);
                    
                    // 2. Отправка размера файла (4 байта)
                    FileInfo fileInfo = new FileInfo(filePath);
                    byte[] sizeBytes = BitConverter.GetBytes((int)fileInfo.Length);
                    await stream.WriteAsync(sizeBytes, 0, sizeBytes.Length);
                    
                    // 3. Отправка содержимого файла
                    await SendFileContentAsync(stream, filePath);
                    
                    // 4. Получение результата анализа
                    FileAnalysisResult? result = await ReceiveAnalysisResultAsync(stream);
                    
                    // 5. Отображение результата
                    if (result != null)
                    {
                        DisplayResult(result);
                        return true;
                    }
                    
                    Console.WriteLine("[ОШИБКА] Не удалось получить результат от сервера");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ОШИБКА] Отправка файла: {ex.Message}");
                return false;
            }
            finally
            {
                client?.Close();
            }
        }

        private async Task SendFileContentAsync(NetworkStream stream, string filePath)
        {
            byte[] buffer = new byte[8192];
            
            using (FileStream fileStream = File.OpenRead(filePath))
            {
                int bytesRead;
                long totalBytesSent = 0;
                long fileSize = fileStream.Length;
                int lastProgress = 0;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesSent += bytesRead;
                    
                    int progress = (int)((totalBytesSent * 100) / fileSize);
                    if (progress >= lastProgress + 10)
                    {
                        Console.WriteLine($"[ПРОГРЕСС] Отправлено {progress}%");
                        lastProgress = progress;
                    }
                }
            }
            
            await stream.FlushAsync();
            Console.WriteLine("[КЛИЕНТ] Файл отправлен полностью");
        }

        private async Task<FileAnalysisResult?> ReceiveAnalysisResultAsync(NetworkStream stream)
        {
            try
            {
                // Получение длины сообщения (4 байта)
                byte[] lengthBuffer = new byte[4];
                int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4);
                if (bytesRead != 4) throw new Exception("Не удалось прочитать длину сообщения");
                
                int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                
                // Получение самого сообщения
                byte[] messageBuffer = new byte[messageLength];
                int totalBytesRead = 0;
                
                while (totalBytesRead < messageLength)
                {
                    bytesRead = await stream.ReadAsync(messageBuffer, totalBytesRead, 
                                                       messageLength - totalBytesRead);
                    if (bytesRead == 0) throw new Exception("Соединение закрыто при получении результата");
                    
                    totalBytesRead += bytesRead;
                }
                
                string json = Encoding.UTF8.GetString(messageBuffer, 0, messageLength);
                return JsonSerializer.Deserialize<FileAnalysisResult>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ОШИБКА] Получение результата: {ex.Message}");
                return null;
            }
        }

        private void DisplayResult(FileAnalysisResult result)
        {
            Console.WriteLine("\n=== РЕЗУЛЬТАТ АНАЛИЗА ===");
            Console.WriteLine($"Имя файла: {result.FileName}");
            Console.WriteLine($"Строк: {result.LineCount}");
            Console.WriteLine($"Слов: {result.WordCount}");
            Console.WriteLine($"Символов: {result.CharCount}");
            Console.WriteLine($"Время анализа: {result.AnalysisTime}");
            Console.WriteLine("========================\n");
        }

        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== КЛИЕНТ АНАЛИЗА ФАЙЛОВ ===\n");

            if (args.Length >= 1)
            {
                var client = new FileAnalysisClient();
                
                foreach (string file in args)
                {
                    await client.SendFileAsync(file);
                }
            }
            else
            {
                await RunInteractiveMode();
            }
        }

        private static async Task RunInteractiveMode()
        {
            var client = new FileAnalysisClient();
            
            while (true)
            {
                Console.WriteLine("\nВведите путь к файлу для отправки (или команду):");
                Console.WriteLine("  'exit' - выход");
                Console.WriteLine("  'test' - базовые тесты");
                Console.WriteLine("  'multitest' - тест множественных подключений");
                Console.WriteLine("  'stress X' - стресс-тест (X файлов)");
                Console.Write("> ");
                
                string? input = Console.ReadLine()?.Trim().ToLower();
                
                if (string.IsNullOrEmpty(input) || input == "exit")
                    break;
                
                if (input == "test")
                {
                    await RunBasicTestsAsync();
                }
                else if (input == "multitest")
                {
                    await RunMultiClientTestAsync();
                }
                else if (input.StartsWith("stress "))
                {
                    if (int.TryParse(input.Substring(7), out int count))
                    {
                        await RunStressTestAsync(count);
                    }
                    else
                    {
                        Console.WriteLine("Неверный формат. Используйте: stress 10");
                    }
                }
                else
                {
                    await client.SendFileAsync(input);
                }
            }
        }

        /// <summary>
        /// Базовые тесты для проверки основной функциональности
        /// </summary>
        private static async Task RunBasicTestsAsync()
        {
            Console.WriteLine("\n=== ЗАПУСК БАЗОВЫХ ТЕСТОВ ===\n");
            
            string testDir = Path.Combine(Path.GetTempPath(), "FileAnalysisTests_" + Guid.NewGuid().ToString().Substring(0, 8));
            Directory.CreateDirectory(testDir);
            
            var testResults = new List<(string TestName, bool Passed, string Message)>();
            
            try
            {
                // Тест 1: Пустой файл
                string emptyFilePath = Path.Combine(testDir, "empty.txt");
                await File.WriteAllTextAsync(emptyFilePath, "");
                
                var client = new FileAnalysisClient();
                Console.WriteLine("Тест 1: Пустой файл");
                bool emptyResult = await client.SendFileAsync(emptyFilePath);
                testResults.Add(("Пустой файл", emptyResult, emptyResult ? "Успешно" : "Ошибка"));
                
                // Тест 2: Файл с одной строкой
                string singleLinePath = Path.Combine(testDir, "single.txt");
                await File.WriteAllTextAsync(singleLinePath, "Hello world!");
                
                Console.WriteLine("\nТест 2: Файл с одной строкой");
                bool singleResult = await client.SendFileAsync(singleLinePath);
                testResults.Add(("Одна строка", singleResult, singleResult ? "Успешно" : "Ошибка"));
                
                // Тест 3: Файл с несколькими строками
                string multiLinePath = Path.Combine(testDir, "multi.txt");
                await File.WriteAllTextAsync(multiLinePath, "Line1\nLine2\nLine3\nLine4\nLine5");
                
                Console.WriteLine("\nТест 3: Файл с несколькими строками");
                bool multiResult = await client.SendFileAsync(multiLinePath);
                testResults.Add(("Несколько строк", multiResult, multiResult ? "Успешно" : "Ошибка"));
                
                // Тест 4: Файл с русским текстом
                string russianPath = Path.Combine(testDir, "russian.txt");
                await File.WriteAllTextAsync(russianPath, "Привет мир!\nЭто тестовый файл.\nОн содержит русский текст.");
                
                Console.WriteLine("\nТест 4: Файл с русским текстом");
                bool russianResult = await client.SendFileAsync(russianPath);
                testResults.Add(("Русский текст", russianResult, russianResult ? "Успешно" : "Ошибка"));
                
                // Тест 5: Несуществующий файл (должен вернуть false без исключения)
                Console.WriteLine("\nТест 5: Несуществующий файл");
                bool notExistsResult = await client.SendFileAsync(Path.Combine(testDir, "notexists.txt"));
                testResults.Add(("Несуществующий файл", !notExistsResult, !notExistsResult ? "Успешно (ошибка обработана)" : "Ошибка"));
                
                // Вывод итогов
                Console.WriteLine("\n=== ИТОГИ ТЕСТИРОВАНИЯ ===");
                int passed = testResults.Count(r => r.Passed);
                foreach (var result in testResults)
                {
                    Console.WriteLine($"{(result.Passed ? "✅" : "❌")} {result.TestName}: {result.Message}");
                }
                Console.WriteLine($"\nРезультат: {passed}/{testResults.Count} тестов пройдено");
            }
            finally
            {
                // Очистка
                try
                {
                    Directory.Delete(testDir, true);
                    Console.WriteLine("\n[ТЕСТ] Тестовые файлы удалены");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ТЕСТ] Не удалось удалить тестовые файлы: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Тест множественных подключений (несколько клиентов одновременно)
        /// </summary>
        private static async Task RunMultiClientTestAsync()
        {
            Console.WriteLine("\n=== ТЕСТ МНОЖЕСТВЕННЫХ ПОДКЛЮЧЕНИЙ ===\n");
            
            string testDir = Path.Combine(Path.GetTempPath(), "MultiClientTest_" + Guid.NewGuid().ToString().Substring(0, 8));
            Directory.CreateDirectory(testDir);
            
            try
            {
                // Создаем тестовые файлы
                int fileCount = 5;
                var filePaths = new List<string>();
                
                for (int i = 1; i <= fileCount; i++)
                {
                    string filePath = Path.Combine(testDir, $"test{i}.txt");
                    string content = $"Файл {i}\n" + string.Join("\n", Enumerable.Range(1, 10).Select(x => $"Строка {x}"));
                    await File.WriteAllTextAsync(filePath, content);
                    filePaths.Add(filePath);
                    Console.WriteLine($"Создан тестовый файл: test{i}.txt");
                }
                
                Console.WriteLine($"\nЗапуск {fileCount} клиентов одновременно...\n");
                
                // Запускаем несколько клиентов параллельно
                var tasks = new List<Task<bool>>();
                var startTime = DateTime.Now;
                
                for (int i = 0; i < fileCount; i++)
                {
                    int clientId = i + 1;
                    string filePath = filePaths[i];
                    
                    // Создаем отдельного клиента для каждого подключения
                    var client = new FileAnalysisClient();
                    
                    tasks.Add(Task.Run(async () => 
                    {
                        Console.WriteLine($"[Клиент {clientId}] Запуск...");
                        bool result = await client.SendFileAsync(filePath);
                        Console.WriteLine($"[Клиент {clientId}] Завершен: {(result ? "Успешно" : "Ошибка")}");
                        return result;
                    }));
                }
                
                // Ожидаем завершения всех клиентов
                var results = await Task.WhenAll(tasks);
                
                var endTime = DateTime.Now;
                var duration = endTime - startTime;
                
                Console.WriteLine($"\n=== РЕЗУЛЬТАТЫ МНОГОКЛИЕНТСКОГО ТЕСТА ===");
                Console.WriteLine($"Всего клиентов: {fileCount}");
                Console.WriteLine($"Успешно: {results.Count(r => r)}");
                Console.WriteLine($"Ошибок: {results.Count(r => !r)}");
                Console.WriteLine($"Время выполнения: {duration.TotalSeconds:F2} сек");
                
                if (results.All(r => r))
                {
                    Console.WriteLine("\n✅ ТЕСТ ПРОЙДЕН: Все клиенты успешно подключились и отправили файлы");
                }
                else
                {
                    Console.WriteLine("\n❌ ТЕСТ НЕ ПРОЙДЕН: Некоторые клиенты завершились с ошибкой");
                }
            }
            finally
            {
                // Очистка
                try
                {
                    Directory.Delete(testDir, true);
                    Console.WriteLine("\n[ТЕСТ] Тестовые файлы удалены");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ТЕСТ] Не удалось удалить тестовые файлы: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Стресс-тест с большим количеством файлов
        /// </summary>
        private static async Task RunStressTestAsync(int fileCount)
        {
            Console.WriteLine($"\n=== СТРЕСС-ТЕСТ ({fileCount} ФАЙЛОВ) ===\n");
            
            if (fileCount > 50)
            {
                Console.WriteLine("Предупреждение: Большое количество файлов может занять много времени.");
                Console.Write("Продолжить? (y/n): ");
                if (Console.ReadLine()?.ToLower() != "y")
                {
                    Console.WriteLine("Тест отменен");
                    return;
                }
            }
            
            string testDir = Path.Combine(Path.GetTempPath(), "StressTest_" + Guid.NewGuid().ToString().Substring(0, 8));
            Directory.CreateDirectory(testDir);
            
            try
            {
                // Создаем тестовые файлы разного размера
                Console.WriteLine($"Создание {fileCount} тестовых файлов...");
                
                var filePaths = new List<string>();
                var random = new Random();
                
                for (int i = 1; i <= fileCount; i++)
                {
                    string filePath = Path.Combine(testDir, $"stress_{i}.txt");
                    
                    // Генерируем случайный контент разного размера
                    int lineCount = random.Next(5, 50);
                    var lines = new List<string>();
                    for (int j = 0; j < lineCount; j++)
                    {
                        int wordCount = random.Next(3, 20);
                        var words = new List<string>();
                        for (int k = 0; k < wordCount; k++)
                        {
                            words.Add($"word{k}_{Guid.NewGuid():N}");
                        }
                        lines.Add(string.Join(" ", words));
                    }
                    
                    await File.WriteAllTextAsync(filePath, string.Join("\n", lines));
                    filePaths.Add(filePath);
                    
                    if (i % 10 == 0)
                    {
                        Console.WriteLine($"Создано {i} файлов...");
                    }
                }
                
                Console.WriteLine($"\nЗапуск стресс-теста с {fileCount} файлами...\n");
                
                // Ограничиваем количество одновременных подключений до 10
                int maxConcurrent = Math.Min(10, fileCount);
                var semaphore = new SemaphoreSlim(maxConcurrent);
                var tasks = new List<Task<bool>>();
                var startTime = DateTime.Now;
                var successCount = 0;
                var errorCount = 0;
                var lockObj = new object();
                
                for (int i = 0; i < fileCount; i++)
                {
                    int fileIndex = i;
                    string filePath = filePaths[i];
                    
                    await semaphore.WaitAsync();
                    
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var client = new FileAnalysisClient();
                            bool result = await client.SendFileAsync(filePath);
                            
                            lock (lockObj)
                            {
                                if (result)
                                    successCount++;
                                else
                                    errorCount++;
                            }
                            
                            semaphore.Release();
                            return result;
                        }
                        catch (Exception)
                        {
                            lock (lockObj)
                            {
                                errorCount++;
                            }
                            semaphore.Release();
                            return false;
                        }
                    }));
                }
                
                // Ожидаем завершения всех задач
                await Task.WhenAll(tasks);
                
                var endTime = DateTime.Now;
                var duration = endTime - startTime;
                
                Console.WriteLine($"\n=== РЕЗУЛЬТАТЫ СТРЕСС-ТЕСТА ===");
                Console.WriteLine($"Всего файлов: {fileCount}");
                Console.WriteLine($"Успешно обработано: {successCount}");
                Console.WriteLine($"Ошибок: {errorCount}");
                Console.WriteLine($"Время выполнения: {duration.TotalSeconds:F2} сек");
                Console.WriteLine($"Скорость: {fileCount / duration.TotalSeconds:F2} файлов/сек");
                
                if (errorCount == 0)
                {
                    Console.WriteLine("\n✅ СТРЕСС-ТЕСТ ПРОЙДЕН: Все файлы успешно обработаны");
                }
                else
                {
                    Console.WriteLine($"\n❌ СТРЕСС-ТЕСТ НЕ ПРОЙДЕН: {errorCount} ошибок");
                }
            }
            finally
            {
                // Очистка
                try
                {
                    Directory.Delete(testDir, true);
                    Console.WriteLine("\n[ТЕСТ] Тестовые файлы удалены");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ТЕСТ] Не удалось удалить тестовые файлы: {ex.Message}");
                }
            }
        }
    }
}