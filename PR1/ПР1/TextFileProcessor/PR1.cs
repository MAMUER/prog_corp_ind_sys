using System;
using System.IO;
using System.Linq;

namespace TextFileProcessor
{
    /// <summary>
    /// Основной класс приложения для обработки текстовых файлов
    /// </summary>
    class Program
    {
        /// <summary>
        /// Точка входа в приложение
        /// </summary>
        /// <param name="args">Аргументы командной строки: [путь_к_файлу] [слово_для_поиска]</param>
        static void Main(string[] args)
        {
            Console.WriteLine("=== Программа обработки текстовых файлов ===\n");

            // Проверка аргументов командной строки
            if (args.Length < 2)
            {
                Console.WriteLine("Ошибка: Недостаточно параметров.");
                Console.WriteLine("Использование: TextFileProcessor.exe <путь_к_файлу> <слово_для_поиска>");
                return;
            }

            string filePath = args[0];
            string searchWord = args[1];

            try
            {
                // Шаг 1: Чтение файла с передачей владения (демонстрация концепции)
                string content = ReadFileWithOwnership(filePath);
                
                // Шаг 2: Подсчет общего количества слов (используем неизменяемую ссылку)
                int totalWords = CountTotalWords(in content);
                
                // Шаг 3: Подсчет повторений искомого слова (используем неизменяемую ссылку)
                int wordCount = CountWordOccurrences(in content, searchWord);
                
                // Шаг 4: Вывод результатов
                DisplayResults(totalWords, wordCount, searchWord);
                
                // Шаг 5: Запуск базового теста
                RunBasicTest();
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"Ошибка: Файл '{filePath}' не найден.");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Ошибка ввода-вывода: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Неожиданная ошибка: {ex.Message}");
            }
            
            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        /// <summary>
        /// Чтение содержимого файла с демонстрацией концепции "владения"
        /// (здесь владение содержимым возвращается вызывающему коду)
        /// </summary>
        /// <param name="path">Путь к файлу</param>
        /// <returns>Содержимое файла как строку</returns>
        static string ReadFileWithOwnership(string path)
        {
            Console.WriteLine($"Чтение файла: {path}");
            
            if (!File.Exists(path))
                throw new FileNotFoundException($"Файл не найден: {path}");
            
            // Владение строкой передается вызывающему методу
            return File.ReadAllText(path);
        }

        /// <summary>
        /// Подсчет общего количества слов в тексте с использованием неизменяемой ссылки
        /// </summary>
        /// <param name="text">Ссылка на текст (in - неизменяемая ссылка)</param>
        /// <returns>Количество слов</returns>
        static int CountTotalWords(in string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;
                
            // Разделяем текст по пробельным символам и считаем непустые элементы
            return text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        /// <summary>
        /// Подсчет количества повторений заданного слова с использованием неизменяемой ссылки
        /// </summary>
        /// <param name="text">Ссылка на текст (in - неизменяемая ссылка)</param>
        /// <param name="word">Слово для поиска</param>
        /// <returns>Количество повторений</returns>
        static int CountWordOccurrences(in string text, string word)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(word))
                return 0;
                
            // Разделяем текст на слова (учитываем знаки препинания)
            var words = text.Split(new[] { ' ', '.', ',', '!', '?', ';', ':', '\n', '\r', '\t' }, 
                                   StringSplitOptions.RemoveEmptyEntries);
            
            // Подсчитываем совпадения (регистронезависимо)
            return words.Count(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Вывод результатов в консоль
        /// </summary>
        /// <param name="totalWords">Общее количество слов</param>
        /// <param name="wordCount">Количество повторений искомого слова</param>
        /// <param name="searchWord">Искомое слово</param>
        static void DisplayResults(int totalWords, int wordCount, string searchWord)
        {
            Console.WriteLine("\n=== РЕЗУЛЬТАТЫ ===");
            Console.WriteLine($"Общее количество слов в файле: {totalWords}");
            Console.WriteLine($"Количество повторений слова '{searchWord}': {wordCount}");
        }

        /// <summary>
        /// Базовый тест для проверки функции поиска слова
        /// </summary>
        static void RunBasicTest()
        {
            Console.WriteLine("\n=== ЗАПУСК ТЕСТА ===");
            
            // Тестовые данные
            string testText = "Hello world! Hello everyone. This is a test hello.";
            string testSearchWord = "hello";
            int expectedCount = 3; // Hello, Hello, hello
            
            // Вызов тестируемой функции
            int actualCount = CountWordOccurrences(in testText, testSearchWord);
            
            // Проверка результата
            bool testPassed = actualCount == expectedCount;
            
            Console.WriteLine($"Тестовый текст: \"{testText}\"");
            Console.WriteLine($"Поиск слова: '{testSearchWord}'");
            Console.WriteLine($"Ожидаемое количество: {expectedCount}");
            Console.WriteLine($"Фактическое количество: {actualCount}");
            Console.WriteLine($"Результат теста: {(testPassed ? "ПРОЙДЕН" : "НЕ ПРОЙДЕН")}");
            
            // Дополнительный тест с пустой строкой
            string emptyText = "";
            int emptyCount = CountWordOccurrences(in emptyText, testSearchWord);
            Console.WriteLine($"\nТест с пустой строкой: {(emptyCount == 0 ? "ПРОЙДЕН" : "НЕ ПРОЙДЕН")}");
        }
    }
}