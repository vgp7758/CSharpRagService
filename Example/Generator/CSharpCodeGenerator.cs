using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BitRPC.Protocol.Parser;

namespace BitRPC.Protocol.Generator
{
    public class CSharpCodeGenerator : BaseCodeGenerator
    {
        public CSharpCodeGenerator() : base("Templates/CSharp")
        {
        }

        public override void Generate(ProtocolDefinition definition, GenerationOptions options)
        {
            var namespacePath = GetNamespacePath(options.Namespace);
            var baseDir = GetOutputPath(options, namespacePath);

            if (options.GenerateSerialization)
            {
                GenerateDataStructures(definition, options, baseDir);
                GenerateSerializationCode(definition, options, baseDir);
            }

            if (options.GenerateClientServer)
            {
                GenerateClientCode(definition, options, baseDir);
                GenerateServerCode(definition, options, baseDir);
            }

            if (options.GenerateFactories)
            {
                GenerateFactoryCode(definition, options, baseDir);
            }
        }

        private void GenerateDataStructures(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var dataDir = Path.Combine(baseDir, "Data");
            EnsureDirectoryExists(dataDir);

            foreach (var message in definition.Messages)
            {
                var filePath = Path.Combine(dataDir, $"{message.Name}.cs");
                var content = GenerateMessageClass(message, options);
                File.WriteAllText(filePath, content);
            }
        }

        private string GenerateMessageClass(ProtocolMessage message, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"{message.Name}.cs", options));
            sb.AppendLine($"using System;");
            sb.AppendLine($"using System.Collections.Generic;");
            sb.AppendLine($"using BitRPC.Serialization;");
            sb.AppendLine();
            
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine($"namespace {options.Namespace}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    public class {message.Name}");
            sb.AppendLine("    {");

            foreach (var field in message.Fields)
            {
                sb.AppendLine($"        public {GetCSharpType(field)} {field.Name} {{ get; set; }}");
            }

            sb.AppendLine();
            sb.AppendLine($"        public {message.Name}()");
            sb.AppendLine("        {");

            foreach (var field in message.Fields)
            {
                var defaultValue = GetDefaultValue(field);
                if (defaultValue != null)
                {
                    sb.AppendLine($"            {field.Name} = {defaultValue};");
                }
            }

            sb.AppendLine("        }");

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private void GenerateSerializationCode(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var serializationDir = Path.Combine(baseDir, "Serialization");
            EnsureDirectoryExists(serializationDir);

            foreach (var message in definition.Messages)
            {
                var filePath = Path.Combine(serializationDir, $"{message.Name}Serializer.cs");
                var content = GenerateMessageSerializer(message, options);
                File.WriteAllText(filePath, content);
            }

            GenerateSerializerRegistry(definition, options, serializationDir);
        }

        private string GenerateMessageSerializer(ProtocolMessage message, GenerationOptions options)
        {
            var sb = new StringBuilder();
            var fieldGroups = message.Fields.Select((f, i) => new { Field = f, Index = i })
                                           .GroupBy(x => x.Index / 32)
                                           .ToList();
            
            sb.AppendLine(GenerateFileHeader($"{message.Name}Serializer.cs", options));
            sb.AppendLine($"using System;");
            sb.AppendLine($"using System.IO;");
            sb.AppendLine($"using BitRPC.Serialization;");
            sb.AppendLine();
            
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine($"namespace {options.Namespace}.Serialization");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    public class {message.Name}Serializer : ITypeHandler");
            sb.AppendLine("    {");
            sb.AppendLine($"        public int HashCode => typeof({message.Name}).GetHashCode();");
            sb.AppendLine();
            sb.AppendLine("        public void Write(object obj, BitRPC.Serialization.StreamWriter writer)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var message = ({message.Name})obj;");
            sb.AppendLine($"            BitRPC.Serialization.BitMask mask = null;");
            sb.AppendLine($"            try");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                mask = BitRPC.Serialization.BitMaskPool.Get({fieldGroups.Count});");

            for (int group = 0; group < fieldGroups.Count; group++)
            {
                var fields = fieldGroups[group].ToList();
                sb.AppendLine($"            // Bit mask group {group}");
                foreach (var fieldInfo in fields)
                {
                    var field = fieldInfo.Field;
                    var bitIndex = fieldInfo.Index % 32;
                    sb.AppendLine($"            mask.SetBit({bitIndex}, !IsDefault(message.{field.Name}));");
                }
                sb.AppendLine($"            mask.Write(writer);");
                sb.AppendLine();
            }

            sb.AppendLine("            // Write field values");
            foreach (var field in message.Fields)
            {
                var fieldIndex = message.Fields.IndexOf(field);
                var bitIndex = fieldIndex % 32;
                sb.AppendLine($"            if (mask.GetBit({bitIndex}))");
                sb.AppendLine("            {");
                sb.AppendLine($"                {GenerateWriteField(field)}");
                sb.AppendLine("            }");
            }

            sb.AppendLine("            }");
            sb.AppendLine("            finally");
            sb.AppendLine("            {");
            sb.AppendLine("                if (mask != null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    BitRPC.Serialization.BitMaskPool.Return(mask);");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public object Read(BitRPC.Serialization.StreamReader reader)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var message = new {message.Name}();");
            sb.AppendLine();

            sb.AppendLine($"            // Read bit masks using object pool");
            sb.AppendLine($"            BitRPC.Serialization.BitMask[] masks = new BitRPC.Serialization.BitMask[{fieldGroups.Count}];");
            sb.AppendLine($"            try");
            sb.AppendLine($"            {{");
            for (int group = 0; group < fieldGroups.Count; group++)
            {
                sb.AppendLine($"            // Read bit mask group {group}");
                sb.AppendLine($"            masks[{group}] = BitRPC.Serialization.BitMaskPool.Get(1);");
                sb.AppendLine($"            masks[{group}].Read(reader);");
            }

            foreach (var field in message.Fields)
            {
                var fieldIndex = message.Fields.IndexOf(field);
                var groupIndex = fieldIndex / 32;
                var bitIndex = fieldIndex % 32;
                sb.AppendLine($"            if (masks[{groupIndex}].GetBit({bitIndex}))");
                sb.AppendLine("            {");
                sb.AppendLine($"                {GenerateReadField(field)}");
                sb.AppendLine("            }");
            }

            sb.AppendLine("            return message;");
            sb.AppendLine("            }");
            sb.AppendLine("            finally");
            sb.AppendLine("            {");
            sb.AppendLine("                foreach (var mask in masks)");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (mask != null) BitRPC.Serialization.BitMaskPool.Return(mask);");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Add default check methods for each unique field type
            var uniqueFieldTypes = new Dictionary<string, (string typeName, string defaultValue)>();
            foreach (var field in message.Fields)
            {
                var typeName = GetCSharpType(field);
                var defaultValue = GetDefaultValue(field);
                
                if (!uniqueFieldTypes.ContainsKey(typeName))
                {
                    uniqueFieldTypes[typeName] = (typeName, defaultValue);
                }
            }
            
            foreach (var (typeName, defaultValue) in uniqueFieldTypes.Values)
            {
                sb.AppendLine($"        private bool IsDefault({typeName} value)");
                sb.AppendLine("        {");
                if (typeName == "string")
                {
                    sb.AppendLine($"            return string.IsNullOrEmpty(value);");
                }
                else
                {
                    sb.AppendLine($"            return value == {defaultValue};");
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Add static helper methods
            sb.AppendLine($"        private static readonly {message.Name}Serializer _instance = new {message.Name}Serializer();");
            sb.AppendLine();
            sb.AppendLine($"        public static void Write({message.Name} obj, BitRPC.Serialization.StreamWriter writer)");
            sb.AppendLine("        {");
            sb.AppendLine("            _instance.Write(obj, writer);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        public static {message.Name} ReadStatic(BitRPC.Serialization.StreamReader reader)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return ({message.Name})_instance.Read(reader);");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private void GenerateSerializerRegistry(ProtocolDefinition definition, GenerationOptions options, string serializationDir)
        {
            var filePath = Path.Combine(serializationDir, "SerializerRegistry.cs");
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader("SerializerRegistry.cs", options));
            sb.AppendLine("using System;");
            sb.AppendLine("using BitRPC.Serialization;");
            sb.AppendLine();
            
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine($"namespace {options.Namespace}.Serialization");
                sb.AppendLine("{");
            }

            sb.AppendLine("    public static class SerializerRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void RegisterSerializers(IBufferSerializer serializer)");
            sb.AppendLine("        {");

            foreach (var message in definition.Messages)
            {
                sb.AppendLine($"            serializer.RegisterHandler<{message.Name}>(new {message.Name}Serializer());");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine("}");
            }

            File.WriteAllText(filePath, sb.ToString());
        }

        private void GenerateClientCode(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var clientDir = Path.Combine(baseDir, "Client");
            EnsureDirectoryExists(clientDir);

            foreach (var service in definition.Services)
            {
                var filePath = Path.Combine(clientDir, $"{service.Name}Client.cs");
                var content = GenerateServiceClient(service, options);
                File.WriteAllText(filePath, content);
            }
        }

        private string GenerateServiceClient(ProtocolService service, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"{service.Name}Client.cs", options));
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using BitRPC.Client;");
            sb.AppendLine();
            
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine($"namespace {options.Namespace}.Client");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    public class {service.Name}Client : BaseClient");
            sb.AppendLine("    {");
            sb.AppendLine($"        public {service.Name}Client(IRpcClient client) : base(client)");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine();

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"        public async Task<{method.ResponseType}> {method.Name}Async({method.RequestType} request)");
                sb.AppendLine("        {");
                sb.AppendLine($"            return await CallAsync<{method.RequestType}, {method.ResponseType}>(\"{method.Name}\", request);");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private void GenerateServerCode(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var serverDir = Path.Combine(baseDir, "Server");
            EnsureDirectoryExists(serverDir);

            foreach (var service in definition.Services)
            {
                var filePath = Path.Combine(serverDir, $"I{service.Name}Service.cs");
                var content = GenerateServiceInterface(service, options);
                File.WriteAllText(filePath, content);

                var implFilePath = Path.Combine(serverDir, $"{service.Name}ServiceBase.cs");
                var implContent = GenerateServiceBase(service, options);
                File.WriteAllText(implFilePath, implContent);
            }
        }

        private string GenerateServiceInterface(ProtocolService service, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"I{service.Name}Service.cs", options));
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine();
            
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine($"namespace {options.Namespace}.Server");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    public interface I{service.Name}Service");
            sb.AppendLine("    {");

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"        Task<{method.ResponseType}> {method.Name}Async({method.RequestType} request);");
            }

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private string GenerateServiceBase(ProtocolService service, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"{service.Name}ServiceBase.cs", options));
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using BitRPC.Server;");
            sb.AppendLine();
            
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine($"namespace {options.Namespace}.Server");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    public abstract class {service.Name}ServiceBase : BaseService, I{service.Name}Service");
            sb.AppendLine("    {");

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"        public abstract Task<{method.ResponseType}> {method.Name}Async({method.RequestType} request);");
            }

            sb.AppendLine();
            sb.AppendLine("        protected override void RegisterMethods()");
            sb.AppendLine("        {");

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"            RegisterMethod<{method.RequestType}, {method.ResponseType}>(\"{method.Name}\", {method.Name}Async);");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private void GenerateFactoryCode(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var factoryDir = Path.Combine(baseDir, "Factory");
            EnsureDirectoryExists(factoryDir);

            var filePath = Path.Combine(factoryDir, "ProtocolFactory.cs");
            var content = GenerateProtocolFactory(definition, options);
            File.WriteAllText(filePath, content);
        }

        private string GenerateProtocolFactory(ProtocolDefinition definition, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader("ProtocolFactory.cs", options));
            sb.AppendLine("using System;");
            sb.AppendLine("using BitRPC.Serialization;");
            sb.AppendLine();
            
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine($"namespace {options.Namespace}.Factory");
                sb.AppendLine("{");
            }

            sb.AppendLine("    public static class ProtocolFactory");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void Initialize()");
            sb.AppendLine("        {");
            sb.AppendLine("            var serializer = BufferSerializer.Instance;");
            sb.AppendLine($"            {options.Namespace}.Serialization.SerializerRegistry.RegisterSerializers(serializer);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private string GetCSharpTypeName(FieldType type)
        {
            return type switch
            {
                FieldType.Int32 => "int",
                FieldType.Int64 => "long",
                FieldType.Float => "float",
                FieldType.Double => "double",
                FieldType.Bool => "bool",
                FieldType.String => "string",
                FieldType.Vector3 => "Vector3",
                FieldType.DateTime => "DateTime",
                _ => "object"
            };
        }

        private string GetCSharpType(ProtocolField field)
        {
            if (field.IsRepeated)
            {
                return $"List<{GetCSharpTypeNameForField(field)}>";
            }
            return GetCSharpTypeNameForField(field);
        }

        private string GetCSharpTypeNameForField(ProtocolField field)
        {
            if (field.Type == FieldType.Struct && !string.IsNullOrEmpty(field.CustomType))
            {
                return field.CustomType;
            }
            return GetCSharpTypeName(field.Type);
        }

        private string GetDefaultValue(ProtocolField field)
        {
            if (field.IsRepeated) return $"new List<{GetCSharpTypeNameForField(field)}>()";

            return GetDefaultValueForType(field.Type);
        }

        private string GetDefaultValueForType(FieldType type)
        {
            return type switch
            {
                FieldType.Int32 => "0",
                FieldType.Int64 => "0L",
                FieldType.Float => "0.0f",
                FieldType.Double => "0.0",
                FieldType.Bool => "false",
                FieldType.String => "string.Empty",
                _ => "default"
            };
        }

        private string GenerateWriteField(ProtocolField field)
        {
            if (field.IsRepeated)
            {
                return $"writer.WriteList(message.{field.Name}, x => {GenerateWriteValueForField(field, "x")});";
            }

            return $"{GenerateWriteValueForField(field, $"message.{field.Name}")};";
        }

        private string GenerateWriteValueForField(ProtocolField field, string value)
        {
            if (field.Type == FieldType.Struct && !string.IsNullOrEmpty(field.CustomType))
            {
                return $"{field.CustomType}Serializer.Write({value}, writer)";
            }
            return GenerateWriteValue(field.Type, value);
        }

        private string GenerateWriteValue(FieldType type, string value)
        {
            return type switch
            {
                FieldType.Int32 => $"writer.WriteInt32({value})",
                FieldType.Int64 => $"writer.WriteInt64({value})",
                FieldType.Float => $"writer.WriteFloat({value})",
                FieldType.Double => $"writer.WriteDouble({value})",
                FieldType.Bool => $"writer.WriteBool({value})",
                FieldType.String => $"writer.WriteString({value})",
                _ => $"writer.WriteObject({value})"
            };
        }

        private string GenerateReadField(ProtocolField field)
        {
            if (field.IsRepeated)
            {
                return $"message.{field.Name} = reader.ReadList(() => {GenerateReadValueForField(field)});";
            }

            return $"message.{field.Name} = {GenerateReadValueForField(field)};";
        }

        private string GenerateReadValueForField(ProtocolField field)
        {
            if (field.Type == FieldType.Struct && !string.IsNullOrEmpty(field.CustomType))
            {
                return $"{field.CustomType}Serializer.ReadStatic(reader)";
            }
            return GenerateReadValue(field.Type);
        }

        private string GenerateReadValue(FieldType type)
        {
            return type switch
            {
                FieldType.Int32 => "reader.ReadInt32()",
                FieldType.Int64 => "reader.ReadInt64()",
                FieldType.Float => "reader.ReadFloat()",
                FieldType.Double => "reader.ReadDouble()",
                FieldType.Bool => "reader.ReadBool()",
                FieldType.String => "reader.ReadString()",
                FieldType.DateTime => "(DateTime)reader.ReadObject()",
                _ => "reader.ReadObject()"
            };
        }
    }
}