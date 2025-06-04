using DocumentValidator.Tests;

namespace DocumentValidator.TestConsole
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Document Validator Test Console");
            Console.WriteLine("==============================");
            
            await TestFunction.RunValidationTest();
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
} 