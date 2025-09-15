using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BitRPC.Protocol.Parser;

namespace BitRPC.Protocol.Generator
{
    public class CppCodeGenerator : BaseCodeGenerator
    {
        public CppCodeGenerator() : base("Templates/Cpp")
        {
        }

        public override void Generate(ProtocolDefinition definition, GenerationOptions options)
        {
            var baseDir = GetOutputPath(options);

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

            GenerateCMakeFile(definition, options, baseDir);
        }

        private void GenerateDataStructures(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var includeDir = Path.Combine(baseDir, "include");
            var sourceDir = Path.Combine(baseDir, "src");
            EnsureDirectoryExists(includeDir);
            EnsureDirectoryExists(sourceDir);

            var headerPath = Path.Combine(includeDir, "models.h");
            var sourcePath = Path.Combine(sourceDir, "models.cpp");

            var headerContent = GenerateModelsHeader(definition, options);
            var sourceContent = GenerateModelsSource(definition, options);

            File.WriteAllText(headerPath, headerContent);
            File.WriteAllText(sourcePath, sourceContent);
        }

        private string GenerateModelsHeader(ProtocolDefinition definition, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader("models.h", options));
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            sb.AppendLine("#include <vector>");
            sb.AppendLine("#include <string>");
            sb.AppendLine("#include <optional>");
            sb.AppendLine("#include <chrono>");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();

            foreach (var message in definition.Messages)
            {
                sb.AppendLine($"struct {message.Name} {{");
                sb.AppendLine();

                foreach (var field in message.Fields)
                {
                    sb.AppendLine($"    {GetCppType(field)} {field.Name};");
                }

                sb.AppendLine();
                sb.AppendLine($"    {message.Name}();");
                sb.AppendLine("};");
                sb.AppendLine();
            }

            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private string GenerateModelsSource(ProtocolDefinition definition, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader("models.cpp", options));
            sb.AppendLine($"#include \"{GetCppNamespace(options.Namespace)}/models.h\"");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();

            foreach (var message in definition.Messages)
            {
                sb.AppendLine($"{message.Name}::{message.Name}() {{");
                foreach (var field in message.Fields)
                {
                    var defaultValue = GetCppDefaultValue(field);
                    if (!string.IsNullOrEmpty(defaultValue))
                    {
                        sb.AppendLine($"    {field.Name} = {defaultValue};");
                    }
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private void GenerateSerializationCode(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var includeDir = Path.Combine(baseDir, "include");
            var sourceDir = Path.Combine(baseDir, "src");
            EnsureDirectoryExists(includeDir);
            EnsureDirectoryExists(sourceDir);

            foreach (var message in definition.Messages)
            {
                var headerPath = Path.Combine(includeDir, $"{message.Name.ToLower()}_serializer.h");
                var sourcePath = Path.Combine(sourceDir, $"{message.Name.ToLower()}_serializer.cpp");

                var headerContent = GenerateMessageSerializerHeader(message, options);
                var sourceContent = GenerateMessageSerializerSource(message, options);

                File.WriteAllText(headerPath, headerContent);
                File.WriteAllText(sourcePath, sourceContent);
            }

            GenerateSerializerRegistry(definition, options, includeDir, sourceDir);
        }

        private string GenerateMessageSerializerHeader(ProtocolMessage message, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"{message.Name}_serializer.h", options));
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            sb.AppendLine("#include <bitrpc/serialization/type_handler.h>");
            sb.AppendLine($"#include \"{GetCppNamespace(options.Namespace)}/models.h\"");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine($"class {message.Name}Serializer : public TypeHandler {{");
            sb.AppendLine("public:");
            sb.AppendLine($"    int hash_code() const override;");
            sb.AppendLine("    void write(const void* obj, StreamWriter& writer) const override;");
            sb.AppendLine("    void* read(StreamReader& reader) const override;");
            sb.AppendLine();
            sb.AppendLine("private:");
            // Add default check methods for each field type
            var fieldTypes = message.Fields.Select(f => f.Type).Distinct();
            foreach (var fieldType in fieldTypes)
            {
                sb.AppendLine($"    bool is_default_{fieldType.ToString().ToLower()}(const {GetCppTypeName(fieldType)}& value) const;");
            }
            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private string GenerateMessageSerializerSource(ProtocolMessage message, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"{message.Name}_serializer.cpp", options));
            sb.AppendLine($"#include \"{GetCppNamespace(options.Namespace)}/{message.Name.ToLower()}_serializer.h\"");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine($"int {message.Name}Serializer::hash_code() const {{");
            sb.AppendLine($"    return static_cast<int>(std::hash<std::string>{{}}(\"{message.Name}\"));");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"void {message.Name}Serializer::write(const void* obj, StreamWriter& writer) const {{");
            sb.AppendLine($"    const auto& message = *static_cast<const {message.Name}*>(obj);");
            sb.AppendLine("    BitMask mask;");
            sb.AppendLine();

            var fieldGroups = message.Fields.Select((f, i) => new { Field = f, Index = i })
                                           .GroupBy(x => x.Index / 32)
                                           .ToList();

            for (int group = 0; group < fieldGroups.Count; group++)
            {
                var fields = fieldGroups[group].ToList();
                sb.AppendLine($"    // Bit mask group {group}");
                foreach (var fieldInfo in fields)
                {
                    var field = fieldInfo.Field;
                    var bitIndex = fieldInfo.Index % 32;
                    sb.AppendLine($"    mask.set_bit({bitIndex}, !is_default_{field.Type.ToString().ToLower()}(message.{field.Name}));");
                }
                sb.AppendLine($"    mask.write(writer);");
                sb.AppendLine();
            }

            sb.AppendLine("    // Write field values");
            foreach (var field in message.Fields)
            {
                var fieldIndex = message.Fields.IndexOf(field);
                var bitIndex = fieldIndex % 32;
                sb.AppendLine($"    if (mask.get_bit({bitIndex})) {{");
                sb.AppendLine($"        {GenerateCppWriteField(field)}");
                sb.AppendLine("    }");
            }

            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"void* {message.Name}Serializer::read(StreamReader& reader) const {{");
            sb.AppendLine($"    auto message = std::make_unique<{message.Name}>();");
            sb.AppendLine();

            for (int group = 0; group < fieldGroups.Count; group++)
            {
                var fields = fieldGroups[group].ToList();
                sb.AppendLine($"    // Read bit mask group {group}");
                sb.AppendLine($"    BitMask mask{group};");
                sb.AppendLine($"    mask{group}.read(reader);");
                sb.AppendLine();
            }

            foreach (var field in message.Fields)
            {
                var fieldIndex = message.Fields.IndexOf(field);
                var groupIndex = fieldIndex / 32;
                var bitIndex = fieldIndex % 32;
                sb.AppendLine($"    if (mask{groupIndex}.get_bit({bitIndex})) {{");
                sb.AppendLine($"        {GenerateCppReadField(field)}");
                sb.AppendLine("    }");
            }

            sb.AppendLine("    return message.release();");
            sb.AppendLine("}");
            sb.AppendLine();
            // Add default check methods for each field type
            var fieldTypes = message.Fields.Select(f => f.Type).Distinct();
            foreach (var fieldType in fieldTypes)
            {
                sb.AppendLine($"    bool is_default_{fieldType.ToString().ToLower()}(const {GetCppTypeName(fieldType)}& value) const;");
            }
            sb.AppendLine();

            // Implement default check methods
            foreach (var fieldType in fieldTypes)
            {
                sb.AppendLine($"bool {message.Name}Serializer::is_default_{fieldType.ToString().ToLower()}(const {GetCppTypeName(fieldType)}& value) const {{");
                sb.AppendLine($"    return value == {GetCppDefaultValueForType(fieldType)};");
                sb.AppendLine("}");
            }
            sb.AppendLine();

            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private void GenerateSerializerRegistry(ProtocolDefinition definition, GenerationOptions options, string includeDir, string sourceDir)
        {
            var headerPath = Path.Combine(includeDir, "serializer_registry.h");
            var sourcePath = Path.Combine(sourceDir, "serializer_registry.cpp");

            var headerContent = GenerateSerializerRegistryHeader(definition, options);
            var sourceContent = GenerateSerializerRegistrySource(definition, options);

            File.WriteAllText(headerPath, headerContent);
            File.WriteAllText(sourcePath, sourceContent);
        }

        private string GenerateSerializerRegistryHeader(ProtocolDefinition definition, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader("serializer_registry.h", options));
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine("class BufferSerializer;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine("void register_serializers(BufferSerializer& serializer);");
            sb.AppendLine();
            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private string GenerateSerializerRegistrySource(ProtocolDefinition definition, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader("serializer_registry.cpp", options));
            sb.AppendLine("#include <bitrpc/serialization/buffer_serializer.h>");
            sb.AppendLine();

            foreach (var message in definition.Messages)
            {
                sb.AppendLine($"#include \"{GetCppNamespace(options.Namespace)}/{message.Name.ToLower()}_serializer.h\"");
            }

            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine("void register_serializers(BufferSerializer& serializer) {");

            foreach (var message in definition.Messages)
            {
                sb.AppendLine($"    serializer.register_handler<{message.Name}>(std::make_shared<{message.Name}Serializer>());");
            }

            sb.AppendLine("}");
            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private void GenerateClientCode(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var includeDir = Path.Combine(baseDir, "include");
            var sourceDir = Path.Combine(baseDir, "src");
            EnsureDirectoryExists(includeDir);
            EnsureDirectoryExists(sourceDir);

            foreach (var service in definition.Services)
            {
                var headerPath = Path.Combine(includeDir, $"{service.Name.ToLower()}_client.h");
                var sourcePath = Path.Combine(sourceDir, $"{service.Name.ToLower()}_client.cpp");

                var headerContent = GenerateServiceClientHeader(service, options);
                var sourceContent = GenerateServiceClientSource(service, options);

                File.WriteAllText(headerPath, headerContent);
                File.WriteAllText(sourcePath, sourceContent);
            }
        }

        private string GenerateServiceClientHeader(ProtocolService service, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"{service.Name}_client.h", options));
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            sb.AppendLine("#include <bitrpc/client/base_client.h>");
            sb.AppendLine("#include <future>");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine($"class {service.Name}Client : public BaseClient {{");
            sb.AppendLine("public:");
            sb.AppendLine($"    explicit {service.Name}Client(std::shared_ptr<RpcClient> client);");
            sb.AppendLine();

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"    std::future<{method.ResponseType}> {method.Name}_async(const {method.RequestType}& request);");
            }

            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private string GenerateServiceClientSource(ProtocolService service, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"{service.Name}_client.cpp", options));
            sb.AppendLine($"#include \"{GetCppNamespace(options.Namespace)}/{service.Name.ToLower()}_client.h\"");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine($"{service.Name}Client::{service.Name}Client(std::shared_ptr<RpcClient> client)");
            sb.AppendLine("    : BaseClient(client) {}");
            sb.AppendLine();

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"std::future<{method.ResponseType}> {service.Name}Client::{method.Name}_async(const {method.RequestType}& request) {{");
                sb.AppendLine($"    return call_async<{method.RequestType}, {method.ResponseType}>(\"{method.Name}\", request);");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private void GenerateServerCode(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var includeDir = Path.Combine(baseDir, "include");
            var sourceDir = Path.Combine(baseDir, "src");
            EnsureDirectoryExists(includeDir);
            EnsureDirectoryExists(sourceDir);

            foreach (var service in definition.Services)
            {
                var interfacePath = Path.Combine(includeDir, $"i{service.Name.ToLower()}_service.h");
                var basePath = Path.Combine(includeDir, $"{service.Name.ToLower()}_service_base.h");
                var baseSourcePath = Path.Combine(sourceDir, $"{service.Name.ToLower()}_service_base.cpp");

                var interfaceContent = GenerateServiceInterface(service, options);
                var baseContent = GenerateServiceBase(service, options);
                var baseSourceContent = GenerateServiceBaseSource(service, options);

                File.WriteAllText(interfacePath, interfaceContent);
                File.WriteAllText(basePath, baseContent);
                File.WriteAllText(baseSourcePath, baseSourceContent);
            }
        }

        private string GenerateServiceInterface(ProtocolService service, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"i{service.Name}_service.h", options));
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            sb.AppendLine("#include <future>");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine($"class I{service.Name}Service {{");
            sb.AppendLine("public:");
            sb.AppendLine("    virtual ~I{service.Name}Service() = default;");

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"    virtual std::future<{method.ResponseType}> {method.Name}_async(const {method.RequestType}& request) = 0;");
            }

            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private string GenerateServiceBase(ProtocolService service, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"{service.Name}_service_base.h", options));
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            sb.AppendLine("#include <bitrpc/server/base_service.h>");
            sb.AppendLine($"#include \"{GetCppNamespace(options.Namespace)}/i{service.Name.ToLower()}_service.h\"");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine($"class {service.Name}ServiceBase : public BaseService, public I{service.Name}Service {{");
            sb.AppendLine("public:");
            sb.AppendLine($"    {service.Name}ServiceBase();");

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"    std::future<{method.ResponseType}> {method.Name}_async(const {method.RequestType}& request) override;");
            }

            sb.AppendLine();
            sb.AppendLine("protected:");
            sb.AppendLine("    void register_methods() override;");

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"    virtual std::future<{method.ResponseType}> {method.Name}_async_impl(const {method.RequestType}& request) = 0;");
            }

            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private string GenerateServiceBaseSource(ProtocolService service, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader($"{service.Name}_service_base.cpp", options));
            sb.AppendLine($"#include \"{GetCppNamespace(options.Namespace)}/{service.Name.ToLower()}_service_base.h\"");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine($"{service.Name}ServiceBase::{service.Name}ServiceBase() {{");
            sb.AppendLine("    register_methods();");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("void {service.Name}ServiceBase::register_methods() {");

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"    register_method(\"{method.Name}\", [this](const void* request) -> void* {{");
                sb.AppendLine($"        auto result = {method.Name}_async_impl(*static_cast<const {method.RequestType}*>(request));");
                sb.AppendLine($"        return new {method.ResponseType}(result.get());");
                sb.AppendLine("    });");
            }

            sb.AppendLine("}");
            sb.AppendLine();

            foreach (var method in service.Methods)
            {
                sb.AppendLine($"std::future<{method.ResponseType}> {service.Name}ServiceBase::{method.Name}_async(const {method.RequestType}& request) {{");
                sb.AppendLine($"    return {method.Name}_async_impl(request);");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private void GenerateFactoryCode(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var includeDir = Path.Combine(baseDir, "include");
            EnsureDirectoryExists(includeDir);

            var filePath = Path.Combine(includeDir, "protocol_factory.h");
            var content = GenerateProtocolFactory(definition, options);
            File.WriteAllText(filePath, content);
        }

        private string GenerateProtocolFactory(ProtocolDefinition definition, GenerationOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader("protocol_factory.h", options));
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine("class BufferSerializer;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("namespace bitrpc {");
            sb.AppendLine($"namespace {GetCppNamespace(options.Namespace)} {{");
            sb.AppendLine();
            sb.AppendLine("class ProtocolFactory {");
            sb.AppendLine("public:");
            sb.AppendLine("    static void initialize();");
            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("}} // namespace bitrpc");

            return sb.ToString();
        }

        private void GenerateCMakeFile(ProtocolDefinition definition, GenerationOptions options, string baseDir)
        {
            var filePath = Path.Combine(baseDir, "CMakeLists.txt");
            var sb = new StringBuilder();
            sb.AppendLine("cmake_minimum_required(VERSION 3.10)");
            sb.AppendLine($"project({options.Namespace}Protocol)");
            sb.AppendLine();
            sb.AppendLine("set(CMAKE_CXX_STANDARD 17)");
            sb.AppendLine("set(CMAKE_CXX_STANDARD_REQUIRED ON)");
            sb.AppendLine();
            sb.AppendLine("include_directories(include)");
            sb.AppendLine();
            sb.AppendLine("set(SOURCES");
            sb.AppendLine("    src/models.cpp");
            
            foreach (var message in definition.Messages)
            {
                sb.AppendLine($"    src/{message.Name.ToLower()}_serializer.cpp");
            }
            
            sb.AppendLine("    src/serializer_registry.cpp");
            
            foreach (var service in definition.Services)
            {
                sb.AppendLine($"    src/{service.Name.ToLower()}_client.cpp");
                sb.AppendLine($"    src/{service.Name.ToLower()}_service_base.cpp");
            }
            
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("add_library(${PROJECT_NAME} STATIC ${SOURCES})");
            sb.AppendLine();
            sb.AppendLine("target_link_libraries(${PROJECT_NAME}");
            sb.AppendLine("    bitrpc::core");
            sb.AppendLine(")");

            File.WriteAllText(filePath, sb.ToString());
        }

        private string GetCppNamespace(string ns)
        {
            return string.IsNullOrEmpty(ns) ? "generated" : ns.Replace(".", "::");
        }

        private string GetCppType(ProtocolField field)
        {
            if (field.IsRepeated)
            {
                return $"std::vector<{GetCppTypeNameForField(field)}>";
            }

            return GetCppTypeNameForField(field);
        }

        private string GetCppTypeName(FieldType type)
        {
            return type switch
            {
                FieldType.Int32 => "int32_t",
                FieldType.Int64 => "int64_t",
                FieldType.Float => "float",
                FieldType.Double => "double",
                FieldType.Bool => "bool",
                FieldType.String => "std::string",
                FieldType.Vector3 => "Vector3",
                FieldType.DateTime => "std::chrono::system_clock::time_point",
                _ => "void*"
            };
        }

        private string GetCppTypeNameForField(ProtocolField field)
        {
            if (field.Type == FieldType.Struct && !string.IsNullOrEmpty(field.CustomType))
            {
                return field.CustomType;
            }
            return GetCppTypeName(field.Type);
        }

        private string GetCppDefaultValue(ProtocolField field)
        {
            if (field.IsRepeated) return "{}";

            return field.Type switch
            {
                FieldType.Int32 => "0",
                FieldType.Int64 => "0",
                FieldType.Float => "0.0f",
                FieldType.Double => "0.0",
                FieldType.Bool => "false",
                FieldType.String => "\"\"",
                _ => ""
            };
        }

        private string GetCppDefaultValueForType(FieldType type)
        {
            return type switch
            {
                FieldType.Int32 => "0",
                FieldType.Int64 => "0",
                FieldType.Float => "0.0f",
                FieldType.Double => "0.0",
                FieldType.Bool => "false",
                FieldType.String => "\"\"",
                _ => ""
            };
        }

        private string GenerateCppWriteField(ProtocolField field)
        {
            if (field.IsRepeated)
            {
                return $"writer.write_vector(message.{field.Name}, [](const auto& x) {{ {GenerateCppWriteValue(field.Type, "x")} }});";
            }

            return $"{GenerateCppWriteValue(field.Type, $"message.{field.Name}")};";
        }

        private string GenerateCppWriteValue(FieldType type, string value)
        {
            return type switch
            {
                FieldType.Int32 => $"writer.write_int32({value})",
                FieldType.Int64 => $"writer.write_int64({value})",
                FieldType.Float => $"writer.write_float({value})",
                FieldType.Double => $"writer.write_double({value})",
                FieldType.Bool => $"writer.write_bool({value})",
                FieldType.String => $"writer.write_string({value})",
                _ => $"writer.write_object({value})"
            };
        }

        private string GenerateCppReadField(ProtocolField field)
        {
            if (field.IsRepeated)
            {
                return $"message->{field.Name} = reader.read_vector([]() {{ return {GenerateCppReadValue(field.Type)}; }});";
            }

            return $"message->{field.Name} = {GenerateCppReadValue(field.Type)};";
        }

        private string GenerateCppReadValue(FieldType type)
        {
            return type switch
            {
                FieldType.Int32 => "reader.read_int32()",
                FieldType.Int64 => "reader.read_int64()",
                FieldType.Float => "reader.read_float()",
                FieldType.Double => "reader.read_double()",
                FieldType.Bool => "reader.read_bool()",
                FieldType.String => "reader.read_string()",
                _ => "reader.read_object()"
            };
        }
    }
}