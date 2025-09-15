using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BitRPC.Protocol.Parser
{
    public enum FieldType
    {
        Int32, Int64, Float, Double, Bool, String,
        Struct, List, Map, Vector3, DateTime
    }

    public class ProtocolField
    {
        public string Name { get; set; }
        public FieldType Type { get; set; }
        public int Id { get; set; }
        public bool IsRepeated { get; set; }
        public string CustomType { get; set; }
        public object DefaultValue { get; set; }
    }

    public class ProtocolMessage
    {
        public string Name { get; set; }
        public List<ProtocolField> Fields { get; set; } = new List<ProtocolField>();
    }

    public class ProtocolMethod
    {
        public string Name { get; set; }
        public string RequestType { get; set; }
        public string ResponseType { get; set; }
    }

    public class ProtocolService
    {
        public string Name { get; set; }
        public List<ProtocolMethod> Methods { get; set; } = new List<ProtocolMethod>();
    }

    public class ProtocolDefinition
    {
        public string Namespace { get; set; }
        public List<ProtocolMessage> Messages { get; set; } = new List<ProtocolMessage>();
        public List<ProtocolService> Services { get; set; } = new List<ProtocolService>();
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    }

    public class PDLParser
    {
        private readonly Dictionary<string, FieldType> _typeMapping = new Dictionary<string, FieldType>
        {
            { "int32", FieldType.Int32 },
            { "int64", FieldType.Int64 },
            { "float", FieldType.Float },
            { "double", FieldType.Double },
            { "bool", FieldType.Bool },
            { "string", FieldType.String },
            { "Vector3", FieldType.Vector3 },
            { "DateTime", FieldType.DateTime }
        };

        public ProtocolDefinition Parse(string content)
        {
            var definition = new ProtocolDefinition();
            var lines = content.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("//"))
                .ToList();

            int currentLine = 0;
            while (currentLine < lines.Count)
            {
                var line = lines[currentLine];
                
                if (line.StartsWith("namespace"))
                {
                    definition.Namespace = ParseNamespace(line);
                }
                else if (line.StartsWith("message"))
                {
                    var message = ParseMessage(lines, ref currentLine);
                    definition.Messages.Add(message);
                }
                else if (line.StartsWith("service"))
                {
                    var service = ParseService(lines, ref currentLine);
                    definition.Services.Add(service);
                }
                else if (line.StartsWith("option"))
                {
                    ParseOption(line, definition);
                }
                
                currentLine++;
            }

            return definition;
        }

        private string ParseNamespace(string line)
        {
            var match = Regex.Match(line, @"namespace\s+([\w\.]+)");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private ProtocolMessage ParseMessage(List<string> lines, ref int currentLine)
        {
            var message = new ProtocolMessage();
            var match = Regex.Match(lines[currentLine], @"message\s+(\w+)\s*\{");
            if (match.Success)
            {
                message.Name = match.Groups[1].Value;
            }

            currentLine++;
            while (currentLine < lines.Count && !lines[currentLine].Contains("}"))
            {
                var line = lines[currentLine];
                if (!string.IsNullOrEmpty(line) && !line.StartsWith("//"))
                {
                    var field = ParseField(line);
                    if (field != null)
                    {
                        message.Fields.Add(field);
                    }
                }
                currentLine++;
            }

            return message;
        }

        private ProtocolField ParseField(string line)
        {
            var repeatedMatch = Regex.Match(line, @"repeated\s+(\w+)\s+(\w+)\s*=\s*(\d+)");
            if (repeatedMatch.Success)
            {
                var typeName = repeatedMatch.Groups[1].Value;
                var fieldType = MapFieldType(typeName);
                return new ProtocolField
                {
                    Name = repeatedMatch.Groups[2].Value,
                    Type = fieldType,
                    Id = int.Parse(repeatedMatch.Groups[3].Value),
                    IsRepeated = true,
                    CustomType = fieldType == FieldType.Struct ? typeName : null
                };
            }

            var normalMatch = Regex.Match(line, @"(\w+)\s+(\w+)\s*=\s*(\d+)");
            if (normalMatch.Success)
            {
                var typeName = normalMatch.Groups[1].Value;
                var fieldType = MapFieldType(typeName);
                return new ProtocolField
                {
                    Name = normalMatch.Groups[2].Value,
                    Type = fieldType,
                    Id = int.Parse(normalMatch.Groups[3].Value),
                    IsRepeated = false,
                    CustomType = fieldType == FieldType.Struct ? typeName : null
                };
            }

            return null;
        }

        private ProtocolService ParseService(List<string> lines, ref int currentLine)
        {
            var service = new ProtocolService();
            var match = Regex.Match(lines[currentLine], @"service\s+(\w+)\s*\{");
            if (match.Success)
            {
                service.Name = match.Groups[1].Value;
            }

            currentLine++;
            while (currentLine < lines.Count && !lines[currentLine].Contains("}"))
            {
                var line = lines[currentLine];
                if (!string.IsNullOrEmpty(line) && !line.StartsWith("//"))
                {
                    var method = ParseMethod(line);
                    if (method != null)
                    {
                        service.Methods.Add(method);
                    }
                }
                currentLine++;
            }

            return service;
        }

        private ProtocolMethod ParseMethod(string line)
        {
            var match = Regex.Match(line, @"rpc\s+(\w+)\s*\((\w+)\)\s+returns\s*\((\w+)\)");
            if (match.Success)
            {
                return new ProtocolMethod
                {
                    Name = match.Groups[1].Value,
                    RequestType = match.Groups[2].Value,
                    ResponseType = match.Groups[3].Value
                };
            }
            return null;
        }

        private void ParseOption(string line, ProtocolDefinition definition)
        {
            var match = Regex.Match(line, @"option\s+(\w+)\s*=\s*[""]([^""]+)[""]");
            if (match.Success)
            {
                definition.Options[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        private FieldType MapFieldType(string typeName)
        {
            if (_typeMapping.TryGetValue(typeName, out var fieldType))
            {
                return fieldType;
            }
            return FieldType.Struct;
        }
    }
}