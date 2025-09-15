using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using BitRPC.Protocol.Generator;
using BitRPC.Protocol.Parser;

namespace BitRPC.GeneratorApp
{
    public class LanguageConfig
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public string Namespace { get; set; }
        public string RuntimePath { get; set; }
    }

    public class GeneratorConfig
    {
        public string ProtocolFile { get; set; }
        public string OutputDirectory { get; set; }
        public List<LanguageConfig> Languages { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("BitRPC Protocol Generator");
            Console.WriteLine("===========================");

            string configPath = "generator-config.json";
            if (args.Length > 0)
            {
                configPath = args[0];
            }

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Error: Config file '{configPath}' not found.");
                return;
            }

            try
            {
                var configContent = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<GeneratorConfig>(configContent);

                if (!File.Exists(config.ProtocolFile))
                {
                    Console.WriteLine($"Error: Protocol file '{config.ProtocolFile}' not found.");
                    return;
                }

                var generator = new ProtocolGenerator();
                
                var optionsList = new List<GenerationOptions>();
                
                foreach (var lang in config.Languages)
                {
                    if (lang.Enabled)
                    {
                        var options = new GenerationOptions
                        {
                            Language = (TargetLanguage)Enum.Parse(typeof(TargetLanguage), lang.Name, true),
                            OutputDirectory = Path.Combine(config.OutputDirectory, lang.Name),
                            Namespace = lang.Namespace,
                            GenerateSerialization = true,
                            GenerateClientServer = true,
                            GenerateFactories = true
                        };
                        
                        optionsList.Add(options);
                    }
                }

                generator.GenerateMultiple(config.ProtocolFile, optionsList);

                Console.WriteLine($"Successfully generated code for '{config.ProtocolFile}'");
                Console.WriteLine($"Output directory: {config.OutputDirectory}");
                
                var generatedLanguages = new List<string>();
                foreach (var lang in config.Languages)
                {
                    if (lang.Enabled)
                    {
                        generatedLanguages.Add(lang.Name);
                    }
                }
                Console.WriteLine($"Generated languages: {string.Join(", ", generatedLanguages)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}