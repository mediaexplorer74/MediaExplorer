using System;
using NiL.JS;
using NiL.JS.Core;

class Program
{
    static void Main()
    {
        var filePath = @"C:\Users\Admin\source\repos\!OpenCode\MediaExplorer\Src\NilJsTest\main-BE-aXEfW.js";
        var fileContent = System.IO.File.ReadAllText(filePath);
        
        // Test full bundle
        Console.WriteLine($"=== Full bundle ({fileContent.Length} chars) ===");
        try
        {
            var module = new Module(fileContent);
            module.Run();
            Console.WriteLine("OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
