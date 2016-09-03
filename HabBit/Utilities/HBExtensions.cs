using System;

namespace HabBit.Utilities
{
    public static class HBExtensions
    {
        public static void WriteLineResult(this bool value)
        {
            Console.Write(" | ");

            ConsoleColor oldClr = Console.ForegroundColor;
            Console.ForegroundColor = value ? ConsoleColor.Green : ConsoleColor.Red;

            Console.WriteLine(value ? "Success!" : "Failed!");
            Console.ForegroundColor = oldClr;
        }
        public static void WriteLineResult(this int value, string suffix)
        {
            Console.Write(" | ");

            ConsoleColor oldClr = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;

            Console.Write($"{value:n0}");
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                Console.Write(" " + suffix);
            }
            Console.WriteLine();
            Console.ForegroundColor = oldClr;
        }
    }
}