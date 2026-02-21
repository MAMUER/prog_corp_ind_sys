using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MultiThreadFileAnalyzer
{
    /// <summary>
    /// Структура для хранения результатов анализа файла
    /// </summary>
    public readonly struct FileAnalysis
    {
        public string FileName { get; }
        public int WordCount { get; }
        public int CharCount { get; }

        public FileAnalysis(string fileName, int wordCount, int charCount)
        {
            FileName = fileName;
            WordCount = wordCount;
            CharCount = charCount;
        }

        public override string ToString()
        {
            return $"{FileName}: {WordCount} слов, {CharCount} символов";
        }
    }

    /// <summary>
    /// Класс для агрегации общих результатов
    /// </summary>
    public class AggregateResult
    {
        private int _totalWords;
        private int _totalChars;
        private readonly Mutex _mutex = new Mutex();

        public void AddResult(FileAnalysis result)
        {
            _mutex.WaitOne(); // Синхронизация доступа к разделяемым данным
            try
            {
                _totalWords += result.WordCount;
                _totalChars += result.CharCount;
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        public (int TotalWords, int TotalChars) GetTotals()
        {
            _mutex.WaitOne();
            try
            {
                return (_totalWords, _totalChars);
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        public void Reset()
        {
            _mutex.WaitOne();
            try
            {
                _totalWords = 0;
                _totalChars = 0;
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }
    }

    /// <summary>
    /// Основной класс программы
    /// </summary>
    class Program
    {
        private static readonly AggregateResult GlobalResult = new AggregateResult();
        private static readonly List<FileAnalysis> Results = new List<FileAnalysis>();
        private static readonly Mutex ResultsMutex = new Mutex();

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Многопоточный анализатор текстовых файлов ===\n");

            // Получение списка файлов для анализа
            string[] filesToAnalyze = GetFilesToAnalyze(args);
            
            if (filesToAnalyze.Length == 0)
            {
                Console.WriteLine("Не указаны файлы для анализа. Укажите пути к файлам через аргументы командной строки.");
                Console.WriteLine("Пример: MultiThreadFileAnalyzer.exe file1.txt file2.txt file3.txt");
                return;
            }

            Console.WriteLine($"Найдено файлов для анализа: {filesToAnalyze.Length}\n");

            try
            {
                // Запуск многопоточной обработки
                await ProcessFilesAsync(filesToAnalyze);

                // Вывод результатов
                DisplayResults();
                
                // Запуск тестов
                await RunTestsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка: {ex.Message}");
            }

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        /// <summary>
        /// Получение списка файлов для анализа из аргументов командной строки
        /// </summary>
        static string[] GetFilesToAnalyze(string[] args)
        {
            if (args.Length > 0)
                return args;
            
            // Для тестирования используем файлы из текущей директории или создаем тестовые
            return Directory.GetFiles(Directory.GetCurrentDirectory(), "*.txt");
        }

        /// <summary>
        /// Асинхронная многопоточная обработка файлов
        /// </summary>
        static async Task ProcessFilesAsync(string[] filePaths)
        {
            Console.WriteLine("Начало обработки файлов...\n");
            
            var tasks = new List<Task>();
            
            foreach (string filePath in filePaths)
            {
                // Запускаем обработку каждого файла в отдельной задаче
                tasks.Add(ProcessSingleFileAsync(filePath));
            }

            // Ожидаем завершения всех задач
            await Task.WhenAll(tasks);
            
            Console.WriteLine("\nОбработка всех файлов завершена.\n");
        }

        /// <summary>
        /// Асинхронная обработка одного файла
        /// </summary>
        static async Task ProcessSingleFileAsync(string filePath)
        {
            try
            {
                Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] Обработка файла: {filePath}");
                
                // Асинхронное чтение файла
                string content = await ReadFileContentAsync(filePath);
                
                // Анализ содержимого
                var analysis = AnalyzeContent(filePath, content);
                
                // Сохранение результатов с синхронизацией
                await Task.Run(() =>
                {
                    ResultsMutex.WaitOne();
                    try
                    {
                        Results.Add(analysis);
                    }
                    finally
                    {
                        ResultsMutex.ReleaseMutex();
                    }
                    
                    // Обновление глобальных итогов
                    GlobalResult.AddResult(analysis);
                });
                
                Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] Завершена обработка: {Path.GetFileName(filePath)}");
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"[ОШИБКА] Файл не найден: {filePath}");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"[ОШИБКА] Нет доступа к файлу: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ОШИБКА] Не удалось обработать файл {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Асинхронное чтение содержимого файла
        /// </summary>
        static async Task<string> ReadFileContentAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл не найден: {filePath}");

            using (var reader = new StreamReader(filePath))
            {
                return await reader.ReadToEndAsync();
            }
        }

        /// <summary>
        /// Анализ содержимого файла (подсчет слов и символов)
        /// </summary>
        static FileAnalysis AnalyzeContent(string filePath, string content)
        {
            // Подсчет символов (без учета пробелов)
            int charCount = content.Count(c => !char.IsWhiteSpace(c));
            
            // Подсчет слов
            int wordCount = content.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':' },
                                          StringSplitOptions.RemoveEmptyEntries).Length;

            return new FileAnalysis(Path.GetFileName(filePath), wordCount, charCount);
        }

        /// <summary>
        /// Вывод результатов анализа в консоль
        /// </summary>
        static void DisplayResults()
        {
            Console.WriteLine("\n=== РЕЗУЛЬТАТЫ АНАЛИЗА ===\n");
            
            // Сортируем результаты по имени файла для удобства
            var sortedResults = Results.OrderBy(r => r.FileName).ToList();
            
            for (int i = 0; i < sortedResults.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {sortedResults[i]}");
            }
            
            var totals = GlobalResult.GetTotals();
            Console.WriteLine($"\nИТОГ: {totals.TotalWords} слов, {totals.TotalChars} символов.");
        }

        /// <summary>
        /// Тестирование функциональности
        /// </summary>
        static async Task RunTestsAsync()
        {
            Console.WriteLine("\n=== ЗАПУСК ТЕСТОВ ===\n");
            
            // Тест 1: Создание тестовых файлов
            await CreateTestFilesAsync();
            
            // Тест 2: Обработка тестовых файлов
            string[] testFiles = { "test1.txt", "test2.txt", "test3.txt" };
            
            // Сброс результатов
            GlobalResult.Reset();
            Results.Clear();
            
            // Обработка тестовых файлов
            await ProcessFilesAsync(testFiles);
            
            // Тест 3: Проверка корректности подсчета
            Console.WriteLine("\n=== ПРОВЕРКА КОРРЕКТНОСТИ ===");
            
            var testTotals = GlobalResult.GetTotals();
            int expectedWords = 15; // 5 + 5 + 5 слов в тестовых файлах
            int expectedChars = 60; // 20 + 20 + 20 символов в тестовых файлах
            
            Console.WriteLine($"Ожидаемый итог: {expectedWords} слов, {expectedChars} символов");
            Console.WriteLine($"Фактический итог: {testTotals.TotalWords} слов, {testTotals.TotalChars} символов");
            
            bool wordsMatch = testTotals.TotalWords == expectedWords;
            bool charsMatch = testTotals.TotalChars == expectedChars;
            
            Console.WriteLine($"Тест подсчета слов: {(wordsMatch ? "ПРОЙДЕН" : "НЕ ПРОЙДЕН")}");
            Console.WriteLine($"Тест подсчета символов: {(charsMatch ? "ПРОЙДЕН" : "НЕ ПРОЙДЕН")}");
            
            // Тест 4: Обработка ошибок
            Console.WriteLine("\n=== ТЕСТ ОБРАБОТКИ ОШИБОК ===");
            await ProcessFilesAsync(new[] { "nonexistentfile.txt" });
            
            // Очистка тестовых файлов
            CleanupTestFiles(testFiles);
        }

        /// <summary>
        /// Создание тестовых файлов
        /// </summary>
        static async Task CreateTestFilesAsync()
        {
            var testData = new[]
            {
                ("test1.txt", "Hello world! This is test file one."),
                ("test2.txt", "C# programming is fun and interesting."),
                ("test3.txt", "Multithreading with async/await works great!")
            };

            foreach (var (fileName, content) in testData)
            {
                await File.WriteAllTextAsync(fileName, content);
                Console.WriteLine($"Создан тестовый файл: {fileName}");
            }
        }

        /// <summary>
        /// Очистка тестовых файлов
        /// </summary>
        static void CleanupTestFiles(string[] testFiles)
        {
            foreach (string file in testFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        Console.WriteLine($"Удален тестовый файл: {file}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Не удалось удалить {file}: {ex.Message}");
                }
            }
        }
    }
}