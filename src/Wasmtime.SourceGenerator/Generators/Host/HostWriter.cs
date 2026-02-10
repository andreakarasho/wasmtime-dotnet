using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SGF;
using Wasmtime.SourceGenerator.Models;

namespace Wasmtime.SourceGenerator.Generators.Host;

public static class HostWriter
{
    [ThreadStatic] private static System.Text.StringBuilder? _stringBuilder;
    [ThreadStatic] private static IndentedStringBuilder? _indentedStringBuilder;

    /// <summary>
    /// Generates the C# constants for WIT names.
    /// </summary>
    /// <param name="ctx">The source production context.</param>
    /// <param name="names">The constant names.</param>
    public static void GenerateConstants(SgfSourceProductionContext ctx, ImmutableArray<string> names)
    {
        var sb = _indentedStringBuilder ??= new IndentedStringBuilder();

        sb.Clear();

        sb.AppendLine("internal static partial class Wit");
        sb.AppendLine("{");
        sb.IncrementIndent();

        sb.AppendLine("public static partial class Constants");
        sb.AppendLine("{");
        sb.IncrementIndent();

        foreach (var name in names)
        {
            sb.Append("public static readonly global::Wasmtime.ByteVector ")
                .Append(StringUtils.GetName(name))
                .Append(" = new global::Wasmtime.ByteVector(\"")
                .Append(name)
                .AppendLine("\");");
        }

        sb.DecrementIndent();
        sb.AppendLine("}");

        sb.DecrementIndent();
        sb.AppendLine("}");

        ctx.AddSource("Wit.Constants.g.cs", sb.ToString());

        sb.Clear();
    }

    /// <summary>
    /// Generates the C# accessor for a WIT package.
    /// </summary>
    /// <param name="ctx">The source production context.</param>
    /// <param name="packages">All WIT packages.</param>
    public static void GenerateWitAccessor(
        SgfSourceProductionContext ctx,
        ImmutableArray<KeyValuePair<WitPackageName, WitPackage>> packages)
    {
        var projectTypeResolver = new ProjectTypeContainerResolver(packages.Select(x => x.Value));

        foreach (var kv in projectTypeResolver.Packages)
        {
            try
            {
                var (name, content) = GenerateWitAccessor(kv, projectTypeResolver);

                ctx.AddSource(name, content);
            }
            catch (Exception e)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.Error,
                    Location.None,
                    kv.Key,
                    e.Message));
            }
        }
    }

    /// <summary>
    /// Generates the C# accessor for a WIT package.
    /// </summary>
    /// <param name="package">The WIT package to generate the accessor for.</param>
    public static (string Path, string Content) GenerateWitAccessor(KeyValuePair<WitPackageName, WitPackage> package, ProjectTypeContainerResolver projectResolver)
    {
        var nameBuilder = _stringBuilder ??= new System.Text.StringBuilder(256);
        var sb = _indentedStringBuilder ??= new IndentedStringBuilder();

        // Ensure the builders are empty
        nameBuilder.Clear();
        sb.Clear();

        sb.AppendLine("internal static partial class Wit");
        sb.AppendLine("{");

        sb.IncrementIndent();

        string? lastPart = null;

        nameBuilder.Append("Wit");

        foreach (var part in package.Key.AllParts)
        {
            var name = StringUtils.GetName(part);

            sb.AppendLine($"public static partial class {name}");
            nameBuilder.Append('.');
            nameBuilder.Append(name);

            if (part == lastPart)
            {
                // It's not possible to have two nested classes with the same name, so use an underscore
                // to differentiate them.
                sb.Append('_');
                nameBuilder.Append('_');
            }
            else
            {
                lastPart = part;
            }

            sb.AppendLine("{");
            sb.IncrementIndent();
        }

        var version = package.Value.Versions[package.Value.LastVersion];

        foreach (var world in version.Worlds)
        {
            var allImports = world.Value.Definitions.FindAll<WitWorldImport>(projectResolver);
            var allExports = world.Value.Definitions.FindAll<WitWorldExport>(projectResolver);

            var className = world.Value.CSharpName;

            if (world.Value.Definitions.Items.Any(i => i is not (WitWorldExport or WitUse or WitWorldImport)))
            {
                sb.Append("public partial class ").Append(className).AppendLine();

                sb.AppendLine("{");
                sb.IncrementIndent();

                WriteItems(sb, world.Value.Definitions.Items, projectResolver, className);

                sb.DecrementIndent();
                sb.AppendLine("}");
                sb.AppendLine();
            }

            if (allExports.Length > 0)
            {
                sb.Append("public partial class ").Append(className).AppendLine("Exports");

                sb.AppendLine("{");
                sb.IncrementIndent();

                // Fields
                sb.AppendLine("private readonly global::Wasmtime.ComponentInstance _instance;");
                sb.AppendLine("private readonly global::Wasmtime.Store _store;");
                sb.AppendLine();

                // Constructor
                sb.Append("public ").Append(className).AppendLine("Exports(global::Wasmtime.ComponentInstance instance, global::Wasmtime.Store store)");
                sb.AppendLine("{");
                sb.IncrementIndent();
                sb.AppendLine("_instance = instance;");
                sb.AppendLine("_store = store;");
                sb.DecrementIndent();
                sb.AppendLine("}");
                sb.AppendLine();

                // Exports
                foreach (var (name, type) in allExports)
                {
                    WriteExport(sb, name, type, projectResolver);
                }

                sb.DecrementIndent();
                sb.AppendLine("}");
                sb.AppendLine();
            }

            if (allImports.Length > 0)
            {
                sb.Append("public abstract partial class ").Append(className).AppendLine("Imports : global::Wasmtime.IComponentImports");

                sb.AppendLine("{");
                sb.IncrementIndent();

                // imports now tracks: (abiName, instancePath, funcType)
                var imports = new List<(string Name, string? InstancePath, WitFuncType Type)>();
                // resourceDefs tracks: (resourceName, instancePath, typeId) for DefineResource calls
                var resourceDefs = new List<(string ResourceName, string InstancePath, uint TypeId)>();

                // Imports
                uint nextResourceTypeId = 0;
                foreach (var (name, type) in allImports)
                {
                    WriteImport(sb, name, type, imports, resourceDefs, projectResolver, ref nextResourceTypeId);
                }

                sb.AppendLine();

                // Register method — group by instance path
                sb.AppendLine("unsafe void global::Wasmtime.IComponentImports.Register(global::Wasmtime.Linker linker)");
                sb.AppendLine("{");
                sb.IncrementIndent();

                // Root-level resources used by root-level imports must be defined before functions
                var rootResourceDefs = CollectRootResourceDefs(imports, projectResolver);
                foreach (var (resourceName, typeId) in rootResourceDefs)
                {
                    var dropMethodName = "ResourceDrop" + StringUtils.GetName(resourceName);
                    sb.Append("linker.DefineResource(\"").Append(resourceName).Append("\", ").Append(typeId).Append(", ").Append(dropMethodName).AppendLine(");");
                }

                // Root-level imports (instancePath == null)
                foreach (var (name, instancePath, _) in imports)
                {
                    if (instancePath != null) continue;
                    var importName = StringUtils.GetName(name);
                    sb.Append("linker.DefineFunction(\"").Append(name).Append("\", ").Append("Invoke").Append(importName).AppendLine(", this);");
                }

                // Instance-scoped imports (grouped by instancePath)
                var instanceGroups = imports
                    .Where(x => x.InstancePath != null)
                    .GroupBy(x => x.InstancePath!);

                // Build a lookup of resource defs per instance path
                var resourcesByInstance = resourceDefs
                    .GroupBy(x => x.InstancePath)
                    .ToDictionary(g => g.Key, g => g.ToList());
                foreach (var group in instanceGroups)
                {
                    sb.AppendLine();
                    var instanceVarName = "instance_" + group.Key.Replace(":", "_").Replace("/", "_");
                    sb.Append("using var ").Append(instanceVarName).Append(" = linker.DefineInstance(\"").Append(group.Key).AppendLine("\");");

                    // DefineResource for each resource in this instance
                    if (resourcesByInstance.TryGetValue(group.Key, out var resources))
                    {
                        foreach (var res in resources)
                        {
                            var dropMethodName = "ResourceDrop" + StringUtils.GetName(res.ResourceName);
                            sb.Append(instanceVarName).Append(".DefineResource(\"").Append(res.ResourceName).Append("\", ").Append(res.TypeId).Append(", ").Append(dropMethodName).AppendLine(");");
                        }
                    }

                    foreach (var (name, _, _) in group)
                    {
                        var importName = StringUtils.GetName(name);
                        sb.Append(instanceVarName).Append(".DefineFunction(\"").Append(name).Append("\", ").Append("Invoke").Append(importName).AppendLine(", this);");
                    }
                }

                sb.DecrementIndent();
                sb.AppendLine("}");
                sb.AppendLine();

                // Import invokers
                foreach (var (name, _, type) in imports)
                {
                    var resetter = sb.CreateResetter();

                    try
                    {
                        WriteImportRegistration(sb, className, type, name, projectResolver);
                    }
                    catch (Exception e)
                    {
                        resetter.Reset();
                        sb.AppendLine($"// Failed to generate function '{name}': {e.Message}");
                    }
                }

                sb.DecrementIndent();
                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        var enclosingClassName = lastPart != null ? StringUtils.GetName(lastPart) : null;
        WriteItems(sb, version.Definitions.Items, projectResolver, enclosingClassName);

        foreach (var unused in package.Key.AllParts)
        {
            sb.DecrementIndent();
            sb.AppendLine("}");
        }

        sb.DecrementIndent();
        sb.AppendLine("}");

        nameBuilder.Append(".g.cs");

        var result = (Path: nameBuilder.ToString(), Content: sb.ToString());

        // Clean up
        nameBuilder.Clear();
        sb.Clear();

        return result;
    }

    private static void WriteImport(
        IndentedStringBuilder sb,
        string name,
        WitType type,
        List<(string Name, string? InstancePath, WitFuncType Type)> imports,
        List<(string ResourceName, string InstancePath, uint TypeId)> resourceDefs,
        ProjectTypeContainerResolver projectResolver,
        ref uint nextResourceTypeId)
    {
        WitCustomType? originalCustomType = null;
        if (type is WitCustomType customType)
        {
            originalCustomType = customType;
            type = customType.Resolve(projectResolver);
        }

        var resetter = sb.CreateResetter();

        try
        {
            if (type is WitFuncType funcType)
            {
                WriteImport(sb, funcType, name, projectResolver);
                imports.Add((name, null, funcType));
            }
            else if (type is WitInterfaceType interfaceType)
            {
                // Determine the interface path for instance-scoped registration
                string? interfacePath = null;
                WitInterface? witInterface = null;

                if (originalCustomType != null)
                {
                    // Resolve the package path for the interface
                    interfacePath = BuildInterfacePath(originalCustomType);

                    // Find the actual WitInterface to access resources
                    try
                    {
                        var container = originalCustomType.GetContainer(projectResolver, allowContainer: true);
                        if (container.TryGetContainer(originalCustomType.Name, out var interfaceContainer) &&
                            interfaceContainer is WitInterface iface)
                        {
                            witInterface = iface;
                        }
                    }
                    catch
                    {
                        // Fallback: no resource support for this interface
                    }
                }

                // Write flat function imports from the interface
                foreach (var field in interfaceType.Fields)
                {
                    if (field.Type is WitFuncType fieldFunc)
                    {
                        WriteImport(sb, fieldFunc, field.Name, projectResolver);
                        imports.Add((field.Name, interfacePath, fieldFunc));
                    }
                    else
                    {
                        WriteImport(sb, field.Name, field.Type, imports, resourceDefs, projectResolver, ref nextResourceTypeId);
                    }
                }

                // Write resource imports (constructor, methods, drop) from the interface
                if (witInterface != null && interfacePath != null)
                {
                    WriteResourceImports(sb, interfacePath, witInterface, imports, resourceDefs, projectResolver, ref nextResourceTypeId);
                }
            }
            else
            {
                sb.AppendLine($"// Unsupported export '{name}' of type '{type.Kind}'");
            }
        }
        catch (Exception e)
        {
            resetter.Reset();
            sb.AppendLine($"// Failed to generate function '{name}': {e.Message}");
        }
    }

    private static string BuildInterfacePath(WitCustomType customType)
    {
        // Build the interface path like "tecs:ecs/ecs" from the custom type
        return customType.Package.PackageName.FullName + "/" + customType.Name;
    }

    private static void WriteResourceImports(
        IndentedStringBuilder sb,
        string interfacePath,
        WitInterface witInterface,
        List<(string Name, string? InstancePath, WitFuncType Type)> imports,
        List<(string ResourceName, string InstancePath, uint TypeId)> resourceDefs,
        ITypeContainerResolver resolver,
        ref uint nextResourceTypeId)
    {
        foreach (var item in witInterface.Definitions.Items)
        {
            if (item is not WitResource resource) continue;

            var resName = resource.Name; // e.g., "system"
            var resType = resource.Type; // WitResourceType — uses ResourceHostWriter
            var typeId = nextResourceTypeId++;

            // Set the type ID on the resource's HostWriter so generated code knows it
            if (resType.HostWriter is ResourceHostWriter rhw)
            {
                rhw.TypeId = typeId;
            }

            resourceDefs.Add((resName, interfacePath, typeId));

            // Constructor(s): [constructor]system
            foreach (var ctor in resource.Constructors)
            {
                var ctorFunc = new WitFuncType(
                    ctor.Parameters,
                    new EquatableArray<WitType>(new WitType[] { resType })
                );
                var abiName = $"[constructor]{resName}";
                WriteImport(sb, ctorFunc, abiName, resolver);
                imports.Add((abiName, interfacePath, ctorFunc));
            }

            // Methods: [method]system.add-commands
            foreach (var method in resource.Fields)
            {
                if (method.Type is WitFuncType methodFunc)
                {
                    // Prepend "self: resource" as first parameter
                    var selfParam = new WitFuncParameter("self", resType);
                    var allParams = new WitFuncParameter[methodFunc.Parameters.Length + 1];
                    allParams[0] = selfParam;
                    for (var i = 0; i < methodFunc.Parameters.Length; i++)
                    {
                        allParams[i + 1] = methodFunc.Parameters[i];
                    }

                    var withSelf = new WitFuncType(
                        new EquatableArray<WitFuncParameter>(allParams),
                        methodFunc.Results
                    );

                    var abiName = $"[method]{resName}.{method.Name}";
                    WriteImport(sb, withSelf, abiName, resolver);
                    imports.Add((abiName, interfacePath, withSelf));
                }
            }

            // Drop: [resource-drop]system
            var dropFunc = new WitFuncType(
                new EquatableArray<WitFuncParameter>(new WitFuncParameter[] { new WitFuncParameter("self", resType) }),
                new EquatableArray<WitType>(Array.Empty<WitType>())
            );
            var dropAbiName = $"[resource-drop]{resName}";
            WriteImport(sb, dropFunc, dropAbiName, resolver);
            imports.Add((dropAbiName, interfacePath, dropFunc));
        }
    }

    private static void WriteExport(IndentedStringBuilder sb, string name, WitType type, ProjectTypeContainerResolver projectResolver)
    {
        if (type is WitCustomType customType)
        {
            type = customType.Resolve(projectResolver);
        }

        var resetter = sb.CreateResetter();

        try
        {
            if (type is WitFuncType funcType)
            {
                WriteExport(sb, funcType, name, projectResolver);
            }
            else if (type is WitInterfaceType interfaceType)
            {
                foreach (var field in interfaceType.Fields)
                {
                    WriteExport(sb, field.Name, field.Type, projectResolver);
                }
            }
            else
            {
                sb.AppendLine($"// Unsupported export '{name}' of type '{type.Kind}'");
            }
        }
        catch (Exception e)
        {
            resetter.Reset();
            sb.AppendLine($"// Failed to generate function '{name}': {e.Message}");
        }
    }

    private static void WriteItems(IndentedStringBuilder sb, EquatableArray<WitTypeDef> valueItems, ITypeContainerResolver resolver, string? enclosingClassName = null)
    {
        foreach (var item in valueItems)
        {
            if (item is WitWorldExport or WitUse or WitWorldImport or WitTypeAlias)
            {
                // Ignore world exports: they are handled in 'GenerateWitAccessor'.
                continue;
            }

            if (item is WitRecord record)
            {
                WriteRecord(sb, record, resolver);
                sb.AppendLine();
            }
            else if (item is WitInterface interf)
            {
                WriteInterface(sb, interf, resolver, enclosingClassName);
            }
            else if (item is WitEnumBase @enum)
            {
                WriteEnum(sb, @enum);
            }
            else if (item is WitVariant variant)
            {
                WriteVariant(sb, variant, resolver);
            }
            else if (item is WitResource resource)
            {
                sb.AppendLine($"// resource {resource.Name} — handle methods generated on imports class");
            }
            else if (item is WitWorldInclude include)
            {
                if (resolver.Resolve(include.Package) is WitPackageVersion version &&
                    version.Worlds.TryGetValue(include.WorldName, out var world))
                {
                    WriteItems(sb, world.Definitions.Items, resolver, enclosingClassName);
                }
            }
            else
            {
                sb.AppendLine($"// Unsupported item of type '{item.GetType().Name}'");
            }
        }
    }

    private static void WriteVariant(IndentedStringBuilder sb, WitVariant variant, ITypeContainerResolver resolver)
    {
        var name = StringUtils.GetName(variant.Name);
        var hasPayloads = variant.Cases.Any(c => c.Type != null);

        if (!hasPayloads)
        {
            // Simple variant (no payloads) — generate as enum + helper like WitEnum
            sb.Append("public enum ").AppendLine(name);
            sb.AppendLine("{");
            sb.IncrementIndent();

            for (var i = 0; i < variant.Cases.Length; i++)
            {
                var c = StringUtils.GetName(variant.Cases[i].Name);
                sb.AppendLine(i > 0 ? "," : "");
                sb.Append(c).Append(" = ").Append(i);
            }

            sb.DecrementIndent();
            sb.AppendLine();
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate helper class for enum-based variant serialization
            sb.Append("public static class ").Append(name).AppendLine("Helper");
            sb.AppendLine("{");
            sb.IncrementIndent();

            // ToByteVector
            sb.Append("public static global::Wasmtime.ByteVector ToByteVector(").Append(name).AppendLine(" value)");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.AppendLine("switch (value)");
            sb.AppendLine("{");
            sb.IncrementIndent();
            for (var i = 0; i < variant.Cases.Length; i++)
            {
                var c = StringUtils.GetName(variant.Cases[i].Name);
                sb.Append("case ").Append(name).Append('.').Append(c).Append(": return global::Wit.Constants.").Append(c).AppendLine(";");
            }
            sb.AppendLine("default: throw new global::System.InvalidOperationException($\"Invalid variant value: {value}\");");
            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.DecrementIndent();
            sb.AppendLine("}");

            // FromByteVector
            sb.AppendLine();
            sb.Append("public static ").Append(name).AppendLine(" FromByteVector(global::Wasmtime.ByteVector value)");
            sb.AppendLine("{");
            sb.IncrementIndent();
            for (var i = 0; i < variant.Cases.Length; i++)
            {
                var c = StringUtils.GetName(variant.Cases[i].Name);
                sb.Append(i > 0 ? "else " : "").Append("if (value.Equals(global::Wit.Constants.").Append(c).AppendLine("))");
                sb.AppendLine("{");
                sb.IncrementIndent();
                sb.Append("return ").Append(name).Append('.').Append(c).AppendLine(";");
                sb.DecrementIndent();
                sb.AppendLine("}");
            }
            sb.AppendLine("else");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.AppendLine("throw new global::System.InvalidOperationException($\"Invalid variant value: {value}\");");
            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.DecrementIndent();
            sb.AppendLine("}");

            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();
        }
        else
        {
            // Variant with payloads — generate a struct with discriminant + payload
            sb.Append("public struct ").AppendLine(name);
            sb.AppendLine("{");
            sb.IncrementIndent();

            // Discriminant enum
            sb.Append("public enum Case").AppendLine();
            sb.AppendLine("{");
            sb.IncrementIndent();
            for (var i = 0; i < variant.Cases.Length; i++)
            {
                var c = StringUtils.GetName(variant.Cases[i].Name);
                sb.AppendLine(i > 0 ? "," : "");
                sb.Append(c).Append(" = ").Append(i);
            }
            sb.DecrementIndent();
            sb.AppendLine();
            sb.AppendLine("}");
            sb.AppendLine();

            // Fields
            sb.AppendLine("public Case Discriminant;");

            // Payload fields for cases that have a type
            foreach (var caseItem in variant.Cases)
            {
                if (caseItem.Type != null)
                {
                    var caseName = StringUtils.GetName(caseItem.Name);
                    sb.Append("public ");
                    caseItem.Type.HostWriter.WriteCSharpType(sb, resolver);
                    sb.Append(' ').Append(caseName).AppendLine("Payload;");
                }
            }

            sb.AppendLine();

            // Static factory methods
            foreach (var caseItem in variant.Cases)
            {
                var caseName = StringUtils.GetName(caseItem.Name);
                if (caseItem.Type != null)
                {
                    sb.Append("public static ").Append(name).Append(" Create").Append(caseName).Append("(");
                    caseItem.Type.HostWriter.WriteCSharpType(sb, resolver);
                    sb.AppendLine(" value)");
                    sb.AppendLine("{");
                    sb.IncrementIndent();
                    sb.Append("return new ").Append(name).Append(" { Discriminant = Case.").Append(caseName);
                    sb.Append(", ").Append(caseName).AppendLine("Payload = value };");
                    sb.DecrementIndent();
                    sb.AppendLine("}");
                }
                else
                {
                    sb.Append("public static ").Append(name).Append(" Create").AppendLine(caseName).Append("()");
                    sb.AppendLine("{");
                    sb.IncrementIndent();
                    sb.Append("return new ").Append(name).Append(" { Discriminant = Case.").Append(caseName).AppendLine(" };");
                    sb.DecrementIndent();
                    sb.AppendLine("}");
                }
            }

            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private static void WriteEnum(IndentedStringBuilder sb, WitEnumBase @enum)
    {
        var name = @enum.CSharpName;
        var isFlags = @enum is WitFlags;

        if (isFlags)
        {
            sb.Append("[System.Flags]");
            sb.AppendLine();
        }

        sb.Append("public enum ").AppendLine(name);
        sb.Append("{");
        sb.IncrementIndent();

        var value = isFlags ? 1 : 0;

        for (var i = 0; i < @enum.Values.Length; i++)
        {
            var c = @enum.Values[i].CSharpName;
            sb.AppendLine(i > 0 ? "," : "");
            sb.Append(c).Append(" = ").Append(value);

            if (!isFlags)
            {
                value++;
            }
            else
            {
                value <<= 1;
            }
        }

        sb.DecrementIndent();
        sb.AppendLine();
        sb.AppendLine("}");
        sb.AppendLine();

        sb.Append("public static class ").Append(name).AppendLine("Helper");
        sb.AppendLine("{");
        sb.IncrementIndent();

        // ToByteVector
        sb.Append("public static global::Wasmtime.ByteVector ToByteVector(").Append(name).AppendLine(" value)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.AppendLine("switch (value)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        for (var i = 0; i < @enum.Values.Length; i++)
        {
            var c = @enum.Values[i].CSharpName;
            sb.Append("case ").Append(name).Append('.').Append(c).Append(": return global::Wit.Constants.").Append(c).AppendLine(";");
        }
        sb.AppendLine("default: throw new global::System.InvalidOperationException($\"Invalid enum value: {value}\");");
        sb.DecrementIndent();
        sb.AppendLine("}");
        sb.DecrementIndent();
        sb.AppendLine("}");

        // FromByteVector
        sb.AppendLine();
        sb.Append("public static ").Append(name).AppendLine(" FromByteVector(global::Wasmtime.ByteVector value)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        for (var i = 0; i < @enum.Values.Length; i++)
        {
            var c = @enum.Values[i].CSharpName;
            sb.Append(i > 0 ? "else " : "").Append("if (value.Equals(global::Wit.Constants.").Append(c).AppendLine("))");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.Append("return ").Append(name).Append('.').Append(c).AppendLine(";");
            sb.DecrementIndent();
            sb.AppendLine("}");
        }
        sb.AppendLine("else");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.AppendLine("throw new global::System.InvalidOperationException($\"Invalid enum value: {value}\");");
        sb.DecrementIndent();
        sb.AppendLine("}");
        sb.DecrementIndent();
        sb.AppendLine("}");

        if (isFlags)
        {
            sb.AppendLine();

            // Expand(T, Span<T>) -> Int32
            sb.Append("public static int Expand(").Append(name).Append(" value, global::System.Span<").Append(name).AppendLine("> results)");
            sb.AppendLine("{");
            sb.IncrementIndent();

            sb.AppendLine("int index = 0;");
            sb.AppendLine();
            for (var i = 0; i < @enum.Values.Length; i++)
            {
                var c = @enum.Values[i].CSharpName;

                sb.Append("if ((value & ").Append(name).Append('.').Append(c).Append(") != 0) ");
                sb.Append("results[index++] = ").Append(name).Append('.').Append(c).AppendLine(";");
            }

            sb.AppendLine();
            sb.AppendLine("return index;");

            sb.DecrementIndent();
            sb.AppendLine("}");

            // Combine(ReadOnlySpan<T>) -> T
            sb.AppendLine();
            sb.Append("public static ").Append(name).Append(" Combine(global::System.ReadOnlySpan<").Append(name).AppendLine("> values)");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.Append(name).AppendLine(" result = default;");
            sb.AppendLine();
            sb.AppendLine("foreach (var value in values)");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.AppendLine("result |= value;");
            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("return result;");
            sb.DecrementIndent();
            sb.AppendLine("}");
        }

        sb.DecrementIndent();
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteInterface(IndentedStringBuilder sb, WitInterface interf, ITypeContainerResolver resolver, string? enclosingClassName = null)
    {
        var name = interf.CSharpName;
        // Avoid CS0542: member names cannot be the same as their enclosing type
        if (name == enclosingClassName)
        {
            name += "_";
        }

        sb.Append("public class ").AppendLine(name);
        sb.AppendLine("{");
        sb.IncrementIndent();

        if (interf.Definitions.Items.Length > 0)
        {
            WriteItems(sb, interf.Definitions.Items, resolver);
        }

        sb.DecrementIndent();
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteRecord(IndentedStringBuilder sb, WitRecord record, ITypeContainerResolver resolver)
    {
        sb.Append("public struct ").AppendLine(record.CSharpName);
        sb.AppendLine("{");
        sb.IncrementIndent();

        foreach (var field in record.Fields)
        {
            sb.Append("public ");
            field.Type.HostWriter.WriteCSharpType(sb, resolver);
            sb.Append(' ').Append(field.CSharpName).AppendLine(";");
        }

        // ToRecordBuilder
        sb.AppendLine();
        sb.Append("public global::Wasmtime.RecordBuilder ToRecordBuilder(bool copyConstants)");
        sb.AppendLine();
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.Append("var builder = new global::Wasmtime.RecordBuilder(").Append(record.Fields.Length).AppendLine(", disposeNames: false);");
        sb.AppendLine();

        sb.AppendLine("if (copyConstants)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        for (var index = 0; index < record.Fields.Length; index++)
        {
            var field = record.Fields[index];

            field.Type.HostWriter.WriteParameterInitializer(sb, field.CSharpVariableName, resolver, ignoreDispose: true, externallyOwned: true);
            sb.Append("builder.Set(").Append(index).Append(", new global::Wasmtime.ByteVector(global::Wit.Constants.").Append(field.CSharpName).Append("), ");
            field.Type.HostWriter.WriteComponentValue(sb, field.CSharpName, ignoreDispose: true, resolver, externallyOwned: true);
            sb.AppendLine(");");
        }

        sb.DecrementIndent();
        sb.AppendLine("}");
        sb.AppendLine("else");
        sb.AppendLine("{");
        sb.IncrementIndent();

        for (var index = 0; index < record.Fields.Length; index++)
        {
            var field = record.Fields[index];

            field.Type.HostWriter.WriteParameterInitializer(sb, field.CSharpVariableName, resolver, ignoreDispose: true, externallyOwned: false);
            sb.Append("builder.Set(").Append(index).Append(", global::Wit.Constants.").Append(field.CSharpName).Append(", ");
            field.Type.HostWriter.WriteComponentValue(sb, field.CSharpName, ignoreDispose: true, resolver, externallyOwned: false);
            sb.AppendLine(");");
        }

        sb.DecrementIndent();
        sb.AppendLine("}");

        sb.AppendLine();
        sb.AppendLine("return builder;");
        sb.DecrementIndent();
        sb.AppendLine("}");

        // Create
        sb.AppendLine();
        sb.Append("public static ").Append(record.CSharpName).AppendLine(" FromRecordBuilder(global::Wasmtime.RecordBuilder builder)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.Append(record.CSharpName).Append(" result = new ").Append(record.CSharpName).AppendLine("();");
        sb.AppendLine();
        sb.AppendLine("foreach (var (name, value) in builder)");
        sb.AppendLine("{");
        sb.IncrementIndent();

        for (var index = 0; index < record.Fields.Length; index++)
        {
            if (index > 0) sb.AppendLine();

            var field = record.Fields[index];

            sb.Append("if (name.Equals(global::Wit.Constants.").Append(field.CSharpName).AppendLine("))");
            sb.AppendLine("{");
            sb.IncrementIndent();
            field.Type.HostWriter.WriteValueGetterInitializer(sb, "value", field.CSharpVariableName, resolver);
            sb.Append("result.").Append(field.CSharpName).Append(" = ");
            field.Type.HostWriter.WriteValueGetter(sb, "value", field.CSharpVariableName, resolver);
            sb.AppendLine(";");
            sb.AppendLine("continue;");
            sb.DecrementIndent();
            sb.AppendLine("}");
        }

        sb.DecrementIndent();
        sb.AppendLine("}");

        sb.AppendLine();
        sb.AppendLine("return result;");

        sb.DecrementIndent();
        sb.AppendLine("}");

        sb.DecrementIndent();
        sb.AppendLine("}");
    }

    private static List<(string ResourceName, uint TypeId)> CollectRootResourceDefs(
        List<(string Name, string? InstancePath, WitFuncType Type)> imports,
        ITypeContainerResolver resolver)
    {
        var rootResources = new List<(string ResourceName, uint TypeId)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, instancePath, funcType) in imports)
        {
            if (instancePath != null)
            {
                continue;
            }

            CollectRootResourceDefs(funcType, resolver, rootResources, seen);
        }

        return rootResources;
    }

    private static void CollectRootResourceDefs(
        WitFuncType funcType,
        ITypeContainerResolver resolver,
        List<(string ResourceName, uint TypeId)> rootResources,
        HashSet<string> seen)
    {
        foreach (var param in funcType.Parameters)
        {
            CollectRootResourceDefs(param.Type, resolver, rootResources, seen);
        }

        foreach (var result in funcType.Results)
        {
            CollectRootResourceDefs(result, resolver, rootResources, seen);
        }
    }

    private static void CollectRootResourceDefs(
        WitType type,
        ITypeContainerResolver resolver,
        List<(string ResourceName, uint TypeId)> rootResources,
        HashSet<string> seen)
    {
        while (type is WitCustomType customType)
        {
            type = customType.Resolve(resolver);
        }

        switch (type)
        {
            case WitResourceType resourceType:
            {
                if (resourceType.HostWriter is ResourceHostWriter rhw && seen.Add(resourceType.Name))
                {
                    rootResources.Add((resourceType.Name, rhw.TypeId));
                }
                break;
            }
            case WitBorrowType borrowType:
                CollectRootResourceDefs(borrowType.ElementType, resolver, rootResources, seen);
                break;
            case WitOptionType optionType:
                CollectRootResourceDefs(optionType.ElementType, resolver, rootResources, seen);
                break;
            case WitListType listType:
                CollectRootResourceDefs(listType.ElementType, resolver, rootResources, seen);
                break;
            case WitTupleType tupleType:
                foreach (var element in tupleType.ElementTypes)
                {
                    CollectRootResourceDefs(element, resolver, rootResources, seen);
                }
                break;
            case WitRecordType recordType:
                foreach (var field in recordType.Fields)
                {
                    CollectRootResourceDefs(field.Type, resolver, rootResources, seen);
                }
                break;
            case WitVariantType variantType:
                foreach (var variantCase in variantType.Values)
                {
                    if (variantCase.Type != null)
                    {
                        CollectRootResourceDefs(variantCase.Type, resolver, rootResources, seen);
                    }
                }
                break;
            case WitResultType resultType:
                CollectRootResourceDefs(resultType.OkType, resolver, rootResources, seen);
                CollectRootResourceDefs(resultType.ErrType, resolver, rootResources, seen);
                break;
            case WitResultNoErrorType resultNoErrorType:
                CollectRootResourceDefs(resultNoErrorType.OkType, resolver, rootResources, seen);
                break;
            case WitResultNoResultType resultNoResultType:
                CollectRootResourceDefs(resultNoResultType.ErrType, resolver, rootResources, seen);
                break;
            case WitStreamType streamType:
                CollectRootResourceDefs(streamType.ElementType, resolver, rootResources, seen);
                break;
        }
    }

    private static void WriteExport(
        IndentedStringBuilder sb,
        WitFuncType funcType,
        string name,
        ITypeContainerResolver resolver)
    {
        sb.Append("");
        var resetter = sb.CreateResetter();

        try
        {
            sb.Append("public unsafe ");
            WriteParameters(sb, resolver, funcType.Results);
            sb.Append(' ').Append(StringUtils.GetName(name)).Append('(');

            for (var i = 0; i < funcType.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var param = funcType.Parameters[i];

                param.Type.HostWriter.WriteParameter(sb, param.CSharpVariableName, resolver);
            }

            sb.AppendLine(")");
            sb.AppendLine("{");
            sb.IncrementIndent();

            // Provide store context for resource value creation in exports
            sb.AppendLine("var context = global::Wasmtime.StoreContext.FromStore(_store);");

            if (funcType.Parameters.Length > 0)
            {
                var length = sb.Length;

                for (var index = 0; index < funcType.Parameters.Length; index++)
                {
                    var param = funcType.Parameters[index];
                    param.Type.HostWriter.WriteParameterInitializer(sb, param.CSharpVariableName, resolver, ignoreDispose: false, externallyOwned: false);
                }

                if (sb.Length > length) sb.AppendLine();

                var parameterSize = funcType.Parameters.Sum(p => p.Type.HostWriter.GetParameterSize(resolver));

                sb.Append("global::Wasmtime.ComponentValue* parameters = ")
                    .Append("stackalloc global::Wasmtime.ComponentValue[")
                    .Append(parameterSize)
                    .AppendLine("];");

                for (var i = 0; i < funcType.Parameters.Length;)
                {
                    var param = funcType.Parameters[i];
                    param.Type.HostWriter.WriteParameterSetter(sb, "parameters", param.CSharpVariableName, i, ignoreDispose: false, resolver: resolver, externallyOwned: false);
                    i += param.Type.HostWriter.GetParameterSize(resolver);
                }

                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("global::Wasmtime.ComponentValue* parameters = null;");
            }

            sb.Append("using global::Wasmtime.ComponentCallResults result = _instance.Call(\"")
                .Append(name)
                .Append("\", ")
                .Append(funcType.Results.Length)
                .Append(", parameters, ")
                .Append(funcType.Parameters.Length)
                .AppendLine(");");

            if (funcType.Results.Length > 0)
            {
                for (var i = 0; i < funcType.Results.Length; i++)
                {
                    funcType.Results[i].HostWriter.WriteResultGetterInitializer(sb, "result", i, resolver);
                }
            }

            if (funcType.Results.Length == 1)
            {
                sb.Append("return ");
                funcType.Results[0].HostWriter.WriteResultGetter(sb, "result", 0, resolver);
                sb.AppendLine(";");
            }
            else if (funcType.Results.Length > 1)
            {
                sb.AppendLine();
                sb.AppendLine("return (");
                sb.IncrementIndent();
                for (var i = 0; i < funcType.Results.Length; i++)
                {
                    if (i > 0) sb.AppendLine(",");
                    funcType.Results[i].HostWriter.WriteResultGetter(sb, "result", i, resolver);
                }

                sb.AppendLine();
                sb.DecrementIndent();
                sb.AppendLine(");");
            }

            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();
        }
        catch (Exception e)
        {
            resetter.Reset();
            sb.AppendLine($"// Failed to generate function '{name}': {e.Message}");
            sb.AppendLine();
        }
    }

    private static void WriteImport(
        IndentedStringBuilder sb,
        WitFuncType funcType,
        string name,
        ITypeContainerResolver resolver)
    {
        sb.Append("");
        var resetter = sb.CreateResetter();

        try
        {
            var importName = StringUtils.GetName(name);

            sb.Append("public abstract ");
            WriteParameters(sb, resolver, funcType.Results);
            sb.Append(' ').Append(importName).Append('(');

            for (var i = 0; i < funcType.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var param = funcType.Parameters[i];

                param.Type.HostWriter.WriteParameter(sb, param.CSharpVariableName, resolver);
            }

            sb.AppendLine(");");
        }
        catch (Exception e)
        {
            resetter.Reset();
            sb.AppendLine($"// Failed to generate function '{name}': {e.Message}");
        }
    }

    private static void WriteImportRegistration(IndentedStringBuilder sb,
        string className,
        WitFuncType funcType,
        string name,
        ITypeContainerResolver resolver)
    {
        sb.Append("");

        var resetter = sb.CreateResetter();

        try
        {
            var importName = StringUtils.GetName(name);

            sb.Append("private unsafe static void Invoke").Append(importName);
            sb.AppendLine("(object state, global::Wasmtime.ComponentCallResults args, global::Wasmtime.ComponentValue* results, global::Wasmtime.StoreContext context)");
            sb.AppendLine("{");

            sb.IncrementIndent();
            sb.Append("var @this = (").Append(className).Append("Imports").AppendLine(")state;");
            sb.AppendLine();

            if (funcType.Parameters.Length > 0)
            {
                for (var i = 0; i < funcType.Parameters.Length; i++)
                {
                    var param = funcType.Parameters[i];

                    param.Type.HostWriter.WriteResultGetterInitializer(sb, "args", i, resolver);
                }
            }

            if (funcType.Results.Length > 0)
            {
                WriteParameters(sb, resolver, funcType.Results);
                sb.Append(" result = ");
            }

            sb.Append("@this.").Append(importName);

            if (funcType.Parameters.Length > 0)
            {
                sb.Append('(');
                sb.IncrementIndent();

                for (var i = 0; i < funcType.Parameters.Length; i++)
                {
                    sb.AppendLine(i > 0 ? "," : "");

                    var param = funcType.Parameters[i];

                    param.Type.HostWriter.WriteResultGetter(sb, "args", i, resolver);
                }

                sb.DecrementIndent();
                sb.AppendLine();
                sb.AppendLine(");");
            }
            else
            {
                sb.AppendLine("();");
            }

            if (funcType.Results.Length > 0)
            {
                sb.AppendLine();

                for (var i = 0; i < funcType.Results.Length; i++)
                {
                    var param = funcType.Results[i];
                    var variable = GetName(funcType, i);
                    param.HostWriter.WriteParameterInitializer(sb, variable, resolver, ignoreDispose: true, externallyOwned: true);
                }

                for (var i = 0; i < funcType.Results.Length; i++)
                {
                    var variable = GetName(funcType, i);
                    var param = funcType.Results[i];
                    param.HostWriter.WriteParameterSetter(sb, "results", variable, i, ignoreDispose: true, resolver, externallyOwned: true);
                    i += param.HostWriter.GetParameterSize(resolver);
                }
            }

            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();
        }
        catch (Exception e)
        {
            resetter.Reset();
            sb.AppendLine($"// Failed to generate function '{name}': {e.Message}");
            sb.AppendLine();

            if (e is not NotSupportedException)
            {
            }
        }
    }

    private static string GetName(WitFuncType funcType, int i)
    {
        var variable = "result";
        if (funcType.Results.Length > 1)
        {
            variable += '.' + (i switch
            {
                0 => "Item1",
                1 => "Item2",
                2 => "Item3",
                3 => "Item4",
                4 => "Item5",
                5 => "Item6",
                6 => "Item7",
                _ => throw new InvalidOperationException("Too many return values.")
            });
        }

        return variable;
    }

    private static void WriteParameters(IndentedStringBuilder sb, ITypeContainerResolver resolver, EquatableArray<WitType> items)
    {
        if (items.Length == 0)
        {
            sb.Append("void");
        }
        else if (items.Length == 1)
        {
            items[0].HostWriter.WriteCSharpType(sb, resolver);
        }
        else
        {
            sb.Append('(');
            for (var i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                items[i].HostWriter.WriteCSharpType(sb, resolver);
            }

            sb.Append(')');
        }
    }
}
