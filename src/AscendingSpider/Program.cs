using System;
using System.IO;

try
{
    using var game = new global::AscendingSpider.Game1();
    game.Run();
}
catch (Exception ex)
{
    File.WriteAllText("error_log.txt", ex.ToString());
    Console.WriteLine("Игра упала. Ошибка записана в файл error_log.txt");
}