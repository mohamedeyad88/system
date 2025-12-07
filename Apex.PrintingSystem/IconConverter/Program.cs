using System;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        string pngPath = args[0];
        string icoPath = args[1];
        Console.WriteLine($"Converting {pngPath} to {icoPath}...");
        IcoConverter.Convert(pngPath, icoPath);
        Console.WriteLine("Done.");
    }
}
