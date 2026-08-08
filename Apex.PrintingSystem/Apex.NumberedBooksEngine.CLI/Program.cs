using Apex.NumberedBooksEngine;
using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apex.NumberedBooksEngine.CLI
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: NumberedBooksEngine.CLI generate --template <path> --slots <json_path> ...");
                return;
            }

            var command = args[0];
            if (command == "generate")
            {
                await RunGenerate(args);
            }
        }

        static async Task RunGenerate(string[] args)
        {
            // Simple manual parsing for now
            string templatePath = GetArg(args, "--template");
            string slotsPath = GetArg(args, "--slots");
            string outputPath = GetArg(args, "--out");
            long start = long.Parse(GetArg(args, "--start") ?? "1");
            long total = long.Parse(GetArg(args, "--total") ?? "100");
            int copies = int.Parse(GetArg(args, "--copies") ?? "1");
            string modeStr = GetArg(args, "--mode") ?? "Auto";

            if (string.IsNullOrEmpty(templatePath) || string.IsNullOrEmpty(slotsPath) || string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine("Missing required arguments.");
                return;
            }

            // Load Slots
            var slotsJson = File.ReadAllText(slotsPath);
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var slots = JsonSerializer.Deserialize<List<SlotSpec>>(slotsJson, jsonOptions);

            using var templateStream = File.OpenRead(templatePath);

            var options = new BookJobOptions(
                TemplateStream: templateStream,
                TemplatePath: templatePath,
                TemplateFormat: templatePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? TemplateFormat.Pdf : TemplateFormat.Image,
                Layout: LayoutSpec.A4, // Default
                Slots: slots,
                StartNumber: start,
                TotalNumbers: total,
                PagesPerBook: 50, // Default
                CopiesPerPage: copies,
                Mode: Enum.Parse<NumberingMode>(modeStr),
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 500,
                OutputMode: "SinglePdf",
                OutputPath: outputPath
            );

            var service = new NumberedBooksService();
            var progress = new Progress<ProgressInfo>(p =>
            {
                Console.Write($"\rProgress: {p.Percent:F1}% ({p.PagesGenerated}/{p.TotalPages})");
            });

            Console.WriteLine("Starting job...");
            var result = await service.GenerateNumberedBooksAsync(options, progress, CancellationToken.None);

            Console.WriteLine();
            if (result.Success)
            {
                Console.WriteLine($"Job Completed! Output: {result.OutputPath}");
            }
            else
            {
                Console.WriteLine("Job Failed:");
                foreach (var err in result.Errors) Console.WriteLine($"- {err}");
            }
        }

        static string? GetArg(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == key) return args[i + 1];
            }
            return null;
        }
    }
}
