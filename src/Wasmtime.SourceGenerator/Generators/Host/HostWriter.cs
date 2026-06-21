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

        sb.AppendLine("#nullable enable");
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
                // Export methods must use uint for resource types (nested classes not in scope)
                ResourceHostWriter.ExportContext = true;

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

                ResourceHostWriter.ExportContext = false;
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
                    // Register the drop as the resource destructor — wasmtime invokes this when the
                    // component drops a handle. (The [resource-drop] import is never called.)
                    sb.Append("linker.DefineResource(\"").Append(resourceName).Append("\", ").Append(typeId)
                        .Append(", Drop").Append(StringUtils.GetName(resourceName)).AppendLine(");");
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
                    var instanceVarName = "instance_" + group.Key.Replace(":", "_").Replace("/", "_").Replace("@", "_").Replace(".", "_").Replace("-", "_");
                    sb.Append("using var ").Append(instanceVarName).Append(" = linker.DefineInstance(\"").Append(group.Key).AppendLine("\");");

                    // DefineResource for each resource in this instance
                    if (resourcesByInstance.TryGetValue(group.Key, out var resources))
                    {
                        foreach (var res in resources)
                        {
                            // Register the drop as the resource destructor — wasmtime invokes this
                            // when the component drops a handle. ([resource-drop] is never called.)
                            sb.Append(instanceVarName).Append(".DefineResource(\"").Append(res.ResourceName).Append("\", ").Append(res.TypeId)
                                .Append(", Drop").Append(StringUtils.GetName(res.ResourceName)).AppendLine(");");
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
        // Build the interface path like "example:calculator/logger@1.0.0" from the custom type
        var versionStr = customType.Package.Version.IsDefault ? "" : $"@{customType.Package.Version}";
        return customType.Package.PackageName.FullName + "/" + customType.Name + versionStr;
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
        // Pass 1: Set all ResourceHostWriter properties BEFORE generating any code.
        // This ensures that when resource A references borrow<resource B> in its methods,
        // resource B's ClassName/HandleTableField are already set, producing the correct type.
        var resourceInfos = new List<(WitResource Resource, string ResName, string ClassName,
            string HandleTableField, string HandleCounterField, string RegisterMethodName, string DropMethodName, uint TypeId)>();

        foreach (var item in witInterface.Definitions.Items)
        {
            if (item is not WitResource resource) continue;

            var resName = resource.Name;
            var resType = resource.Type;
            var typeId = nextResourceTypeId++;
            var className = StringUtils.GetName(resName);
            var fieldName = "_" + StringUtils.GetName(resName, uppercaseFirst: false);
            var handleTableField = fieldName + "Handles";
            var handleCounterField = "_next" + className + "Handle";
            var registerMethodName = "Register" + className;
            var dropMethodName = "Drop" + className;

            if (resType.HostWriter is ResourceHostWriter rhw)
            {
                rhw.TypeId = typeId;
                rhw.ClassName = className;
                rhw.HandleTableField = handleTableField;
                rhw.HandleCounterField = handleCounterField;
                rhw.StoreMethodName = registerMethodName;
            }

            resourceDefs.Add((resName, interfacePath, typeId));
            resourceInfos.Add((resource, resName, className, handleTableField, handleCounterField, registerMethodName, dropMethodName, typeId));
        }

        // Pass 1.5: Emit public type ID constants
        foreach (var (resource, resName, className, handleTableField, handleCounterField, registerMethodName, dropMethodName, typeId) in resourceInfos)
        {
            sb.Append("public const uint ").Append(className).Append("TypeId = ").Append(typeId).AppendLine(";");
        }
        sb.AppendLine();

        // Pass 2: Generate code now that all resource properties are set.
        foreach (var (resource, resName, className, handleTableField, handleCounterField, registerMethodName, dropMethodName, typeId) in resourceInfos)
        {
            var resType = resource.Type;

            // --- Emit handle table field, counter, free list, and debug generation tracking ---
            sb.Append("private readonly global::System.Collections.Generic.Dictionary<uint, ").Append(className).Append("> ")
                .Append(handleTableField).Append(" = new global::System.Collections.Generic.Dictionary<uint, ").Append(className).AppendLine(">();");
            sb.Append("private uint ").Append(handleCounterField).AppendLine(" = 1;");
            sb.Append("private readonly global::System.Collections.Generic.Stack<uint> _free").Append(className).AppendLine("Handles = new();");
            sb.AppendLine("#if DEBUG");
            sb.Append("private readonly global::System.Collections.Generic.Dictionary<uint, uint> _").Append(StringUtils.GetName(resName, uppercaseFirst: false)).AppendLine("Generations = new();");
            sb.Append("private uint _").Append(StringUtils.GetName(resName, uppercaseFirst: false)).AppendLine("Generation = 0;");
            sb.AppendLine("#endif");
            sb.AppendLine();

            // --- Emit nested abstract class ---
            sb.Append("public abstract class ").Append(className).AppendLine(" : global::System.IDisposable");
            sb.AppendLine("{");
            sb.IncrementIndent();

            // Methods on the nested class (instance methods only; static methods have no
            // `self` so they are emitted on the imports class below).
            foreach (var method in resource.Fields)
            {
                if (method.IsStatic) continue;
                if (method.Type is WitFuncType methodFunc)
                {
                    var methodName = StringUtils.GetName(method.Name);
                    sb.Append("public abstract ");
                    WriteParameters(sb, resolver, methodFunc.Results);
                    sb.Append(' ').Append(methodName).Append('(');

                    for (var i = 0; i < methodFunc.Parameters.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        var param = methodFunc.Parameters[i];
                        param.Type.HostWriter.WriteParameter(sb, param.CSharpVariableName, resolver);
                    }

                    sb.AppendLine(");");
                }
            }

            // Dispose method
            sb.AppendLine("public abstract void Dispose();");

            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();

            // --- Emit static methods on the imports class (no instance / no self) ---
            foreach (var method in resource.Fields)
            {
                if (!method.IsStatic || method.Type is not WitFuncType staticFunc) continue;

                sb.Append("public abstract ");
                WriteParameters(sb, resolver, staticFunc.Results);
                sb.Append(' ').Append(className).Append(StringUtils.GetName(method.Name)).Append('(');

                for (var i = 0; i < staticFunc.Parameters.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var param = staticFunc.Parameters[i];
                    param.Type.HostWriter.WriteParameter(sb, param.CSharpVariableName, resolver);
                }

                sb.AppendLine(");");
            }
            sb.AppendLine();

            // --- Emit factory method for constructors ---
            foreach (var ctor in resource.Constructors)
            {
                var factoryName = "New" + className;
                sb.Append("public abstract ").Append(className).Append(' ').Append(factoryName).Append('(');

                for (var i = 0; i < ctor.Parameters.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var param = ctor.Parameters[i];
                    param.Type.HostWriter.WriteParameter(sb, param.CSharpVariableName, resolver);
                }

                sb.AppendLine(");");
            }

            // --- Emit Register method (public, for host-created resources) ---
            var genField = "_" + StringUtils.GetName(resName, uppercaseFirst: false) + "Generation";
            var gensField = "_" + StringUtils.GetName(resName, uppercaseFirst: false) + "Generations";
            sb.Append("public uint ").Append(registerMethodName).Append("(").Append(className).AppendLine(" obj)");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.Append("var h = _free").Append(className).Append("Handles.Count > 0 ? _free").Append(className).Append("Handles.Pop() : checked(").Append(handleCounterField).AppendLine("++);");
            sb.Append(handleTableField).AppendLine("[h] = obj;");
            sb.AppendLine("#if DEBUG");
            sb.Append(gensField).Append("[h] = ++").Append(genField).AppendLine(";");
            sb.AppendLine("#endif");
            sb.AppendLine("return h;");
            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();

            // --- Emit Drop helper (protected, called by InvokeDrop and available for subclass cleanup) ---
            sb.Append("protected void ").Append(dropMethodName).AppendLine("(uint handle)");
            sb.AppendLine("{");
            sb.IncrementIndent();
            // TryGetValue + Remove rather than Remove(key, out) — the 2-arg overload is
            // unavailable on .NET Framework (netstandard2.0 target).
            sb.Append("if (").Append(handleTableField).AppendLine(".TryGetValue(handle, out var obj))");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.Append(handleTableField).AppendLine(".Remove(handle);");
            sb.Append("_free").Append(className).AppendLine("Handles.Push(handle);");
            sb.AppendLine("#if DEBUG");
            sb.Append(gensField).Append("[handle] = ++").Append(genField).AppendLine(";");
            sb.AppendLine("#endif");
            sb.AppendLine("obj.Dispose();");
            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();

            // --- Emit debug-only validated lookup helper ---
            sb.AppendLine("#if DEBUG");
            sb.Append("private ").Append(className).Append(" Get").Append(className).AppendLine("(uint handle)");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.Append("if (!").Append(handleTableField).AppendLine(".TryGetValue(handle, out var obj))");
            sb.IncrementIndent();
            sb.Append("throw new global::System.InvalidOperationException($\"Invalid ").Append(className).AppendLine(" handle: {handle}\");");
            sb.DecrementIndent();
            sb.AppendLine("return obj;");
            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine("#endif");
            sb.AppendLine();

            // --- Still add imports for Invoke generation (ABI names stay the same) ---

            // Constructor(s): [constructor]system
            foreach (var ctor in resource.Constructors)
            {
                var ctorFunc = new WitFuncType(
                    ctor.Parameters,
                    new EquatableArray<WitType>(new WitType[] { resType })
                );
                var abiName = $"[constructor]{resName}";
                imports.Add((abiName, interfacePath, ctorFunc));
            }

            // Methods: [method]system.add-commands — with self prepended for ABI.
            // Static methods: [static]system.foo — no self.
            foreach (var method in resource.Fields)
            {
                if (method.Type is not WitFuncType methodFunc) continue;

                if (method.IsStatic)
                {
                    var abiName = $"[static]{resName}.{method.Name}";
                    imports.Add((abiName, interfacePath, methodFunc));
                    continue;
                }

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

                var methodAbiName = $"[method]{resName}.{method.Name}";
                imports.Add((methodAbiName, interfacePath, withSelf));
            }

            // Drop is handled by the resource destructor registered via DefineResource
            // (wasmtime's intrinsic resource.drop calls it); no [resource-drop] import is needed.
        }
    }

    private static void WriteExport(IndentedStringBuilder sb, string name, WitType type, ProjectTypeContainerResolver projectResolver)
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
                WriteExport(sb, funcType, name, projectResolver);
            }
            else if (type is WitInterfaceType interfaceType)
            {
                // Resolve the interface path + definition so exported resources can be reached
                // (their [constructor]/[method] functions are namespaced under the interface).
                string? interfacePath = null;
                WitInterface? witInterface = null;
                if (originalCustomType != null)
                {
                    interfacePath = BuildInterfacePath(originalCustomType);
                    try
                    {
                        var container = originalCustomType.GetContainer(projectResolver, allowContainer: true);
                        if (container.TryGetContainer(originalCustomType.Name, out var ic) && ic is WitInterface iface)
                        {
                            witInterface = iface;
                        }
                    }
                    catch
                    {
                        // No resource support for this interface.
                    }
                }

                foreach (var field in interfaceType.Fields)
                {
                    if (field.Type is WitFuncType fieldFunc)
                    {
                        WriteExport(sb, fieldFunc, field.Name, projectResolver, interfacePath);
                    }
                }

                if (witInterface != null && interfacePath != null)
                {
                    foreach (var item in witInterface.Definitions.Items)
                    {
                        if (item is WitResource resource)
                        {
                            WriteExportedResource(sb, interfacePath, resource, projectResolver);
                        }
                    }
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
            sb.AppendLine($"// Failed to generate export '{name}': {e.Message}");
        }
    }

    /// <summary>
    /// Generates host-side accessors for a component-exported resource: a factory method on the
    /// Exports class plus a wrapper class (holding the own&lt;resource&gt; handle) with the instance
    /// methods and a Dispose that drops the handle. Constructor/method functions are resolved
    /// under the exporting interface.
    /// </summary>
    private static void WriteExportedResource(IndentedStringBuilder sb, string interfacePath, WitResource resource, ITypeContainerResolver resolver)
    {
        var className = StringUtils.GetName(resource.Name);
        var resName = resource.Name;

        // --- Factory method(s) on the Exports class ---
        foreach (var ctor in resource.Constructors)
        {
            sb.Append("public unsafe ").Append(className).Append(" New").Append(className).Append('(');
            for (var i = 0; i < ctor.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = ctor.Parameters[i];
                p.Type.HostWriter.WriteParameter(sb, p.CSharpVariableName, resolver);
            }
            sb.AppendLine(")");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.Append("var __fn = _instance.GetFunction(\"").Append(interfacePath).Append("\", \"[constructor]").Append(resName).AppendLine("\");");

            // The constructor returns exactly one result: the own<resource> handle.
            WriteExportedResourceCall(sb, ctor.Parameters, new EquatableArray<WitType>(new WitType[] { resource.Type }), selfHandle: null, resolver,
                onResult: () =>
                {
                    sb.Append("return new ").Append(className).AppendLine("(__result[0], _instance, _store);");
                });

            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // --- Static methods on the Exports class (no instance / no self) ---
        foreach (var method in resource.Fields)
        {
            if (!method.IsStatic || method.Type is not WitFuncType sf) continue;

            sb.Append("public unsafe ");
            WriteParameters(sb, resolver, sf.Results);
            sb.Append(' ').Append(className).Append(StringUtils.GetName(method.Name)).Append('(');
            for (var i = 0; i < sf.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = sf.Parameters[i];
                p.Type.HostWriter.WriteParameter(sb, p.CSharpVariableName, resolver);
            }
            sb.AppendLine(")");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.Append("var __fn = _instance.GetFunction(\"").Append(interfacePath).Append("\", \"[static]").Append(resName).Append('.').Append(method.Name).AppendLine("\");");

            WriteExportedResourceCall(sb, sf.Parameters, sf.Results, selfHandle: null, resolver,
                onResult: () =>
                {
                    if (sf.Results.Length == 1)
                    {
                        sf.Results[0].HostWriter.WriteResultGetterInitializer(sb, "__result", 0, resolver);
                        sb.Append("return ");
                        sf.Results[0].HostWriter.WriteResultGetter(sb, "__result", 0, resolver);
                        sb.AppendLine(";");
                    }
                });

            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // --- Wrapper class holding the own<resource> handle ---
        sb.Append("public sealed unsafe class ").Append(className).AppendLine(" : global::System.IDisposable");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.AppendLine("private global::Wasmtime.ComponentValue _handle;");
        sb.AppendLine("private readonly global::Wasmtime.ComponentInstance _instance;");
        sb.AppendLine("private readonly global::Wasmtime.Store _store;");
        sb.AppendLine("private bool _disposed;");
        sb.AppendLine();
        sb.Append("internal ").Append(className).AppendLine("(global::Wasmtime.ComponentValue handle, global::Wasmtime.ComponentInstance instance, global::Wasmtime.Store store)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.AppendLine("_handle = handle; _instance = instance; _store = store;");
        sb.DecrementIndent();
        sb.AppendLine("}");
        sb.AppendLine();

        foreach (var method in resource.Fields)
        {
            if (method.IsStatic || method.Type is not WitFuncType mf) continue;

            sb.Append("public unsafe ");
            WriteParameters(sb, resolver, mf.Results);
            sb.Append(' ').Append(StringUtils.GetName(method.Name)).Append('(');
            for (var i = 0; i < mf.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = mf.Parameters[i];
                p.Type.HostWriter.WriteParameter(sb, p.CSharpVariableName, resolver);
            }
            sb.AppendLine(")");
            sb.AppendLine("{");
            sb.IncrementIndent();
            sb.Append("var __fn = _instance.GetFunction(\"").Append(interfacePath).Append("\", \"[method]").Append(resName).Append('.').Append(method.Name).AppendLine("\");");

            WriteExportedResourceCall(sb, mf.Parameters, mf.Results, selfHandle: "_handle", resolver,
                onResult: () =>
                {
                    if (mf.Results.Length == 1)
                    {
                        mf.Results[0].HostWriter.WriteResultGetterInitializer(sb, "__result", 0, resolver);
                        sb.Append("return ");
                        mf.Results[0].HostWriter.WriteResultGetter(sb, "__result", 0, resolver);
                        sb.AppendLine(";");
                    }
                });

            sb.DecrementIndent();
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine("public void Dispose()");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.AppendLine("if (_disposed) return;");
        sb.AppendLine("_disposed = true;");
        sb.AppendLine("_handle.DropResource(global::Wasmtime.StoreContext.FromStore(_store));");
        sb.DecrementIndent();
        sb.AppendLine("}");

        sb.DecrementIndent();
        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits the parameter marshalling, native call, and result handling for an exported-resource
    /// constructor or method. When <paramref name="selfHandle"/> is non-null it is passed as the
    /// first (self) argument and is NOT disposed (it is retained by the wrapper).
    /// </summary>
    private static void WriteExportedResourceCall(IndentedStringBuilder sb, EquatableArray<WitFuncParameter> parameters,
        EquatableArray<WitType> results, string? selfHandle, ITypeContainerResolver resolver, Action onResult)
    {
        var selfOffset = selfHandle != null ? 1 : 0;

        // Initialize parameter wrappers (ignoreDispose: true — the finally is the single owner).
        for (var i = 0; i < parameters.Length; i++)
        {
            parameters[i].Type.HostWriter.WriteParameterInitializer(sb, parameters[i].CSharpVariableName, resolver, ignoreDispose: true, externallyOwned: false);
        }

        var parameterSize = selfOffset + parameters.Sum(p => p.Type.HostWriter.GetParameterSize(resolver));

        if (parameterSize > 0)
        {
            sb.Append("global::Wasmtime.ComponentValue* __params = stackalloc global::Wasmtime.ComponentValue[").Append(parameterSize).AppendLine("];");
            if (selfHandle != null)
            {
                sb.Append("__params[0] = ").Append(selfHandle).AppendLine(";");
            }

            var index = selfOffset;
            for (var i = 0; i < parameters.Length;)
            {
                var p = parameters[i];
                p.Type.HostWriter.WriteParameterSetter(sb, "__params", p.CSharpVariableName, index, ignoreDispose: true, resolver, externallyOwned: false);
                var size = p.Type.HostWriter.GetParameterSize(resolver);
                index += size;
                i += size;
            }
        }
        else
        {
            sb.AppendLine("global::Wasmtime.ComponentValue* __params = null;");
        }

        sb.Append("try");
        sb.AppendLine();
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.Append("using global::Wasmtime.ComponentCallResults __result = _instance.Call(__fn, ")
            .Append(results.Length).Append(", __params, ").Append(parameterSize).AppendLine(");");
        onResult();
        sb.DecrementIndent();
        sb.AppendLine("}");
        sb.AppendLine("finally");
        sb.AppendLine("{");
        sb.IncrementIndent();
        // Dispose parameter wrappers, but never the retained self handle at index 0.
        sb.Append("for (int __i = ").Append(selfOffset).Append("; __i < ").Append(parameterSize).AppendLine("; __i++)");
        sb.IncrementIndent();
        sb.AppendLine("__params[__i].Dispose(_store);");
        sb.DecrementIndent();
        sb.DecrementIndent();
        sb.AppendLine("}");
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

        // Create — uses index-based access (O(N)) instead of name-matching (O(N^2))
        // since the native record builder preserves field order matching the WIT definition.
        sb.AppendLine();
        sb.Append("public static ").Append(record.CSharpName).AppendLine(" FromRecordBuilder(global::Wasmtime.RecordBuilder builder)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.Append(record.CSharpName).Append(" result = new ").Append(record.CSharpName).AppendLine("();");

        for (var index = 0; index < record.Fields.Length; index++)
        {
            sb.AppendLine();

            var field = record.Fields[index];
            var valueExpr = $"builder.Get({index})";

            field.Type.HostWriter.WriteValueGetterInitializer(sb, valueExpr, field.CSharpVariableName, resolver);
            sb.Append("result.").Append(field.CSharpName).Append(" = ");
            field.Type.HostWriter.WriteValueGetter(sb, valueExpr, field.CSharpVariableName, resolver);
            sb.AppendLine(";");
        }

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
        ITypeContainerResolver resolver,
        string? interfacePath = null)
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

            int parameterSize;
            if (funcType.Parameters.Length > 0)
            {
                var length = sb.Length;

                // ignoreDispose: true — wasmtime_component_func_call takes ownership of parameter data
                for (var index = 0; index < funcType.Parameters.Length; index++)
                {
                    var param = funcType.Parameters[index];
                    param.Type.HostWriter.WriteParameterInitializer(sb, param.CSharpVariableName, resolver, ignoreDispose: true, externallyOwned: false);
                }

                if (sb.Length > length) sb.AppendLine();

                parameterSize = funcType.Parameters.Sum(p => p.Type.HostWriter.GetParameterSize(resolver));

                sb.Append("global::Wasmtime.ComponentValue* parameters = ")
                    .Append("stackalloc global::Wasmtime.ComponentValue[")
                    .Append(parameterSize)
                    .AppendLine("];");

                for (var i = 0; i < funcType.Parameters.Length;)
                {
                    var param = funcType.Parameters[i];
                    param.Type.HostWriter.WriteParameterSetter(sb, "parameters", param.CSharpVariableName, i, ignoreDispose: true, resolver: resolver, externallyOwned: false);
                    i += param.Type.HostWriter.GetParameterSize(resolver);
                }

                sb.AppendLine();
            }
            else
            {
                parameterSize = 0;
                sb.AppendLine("global::Wasmtime.ComponentValue* parameters = null;");
            }

            // Wrap call + result handling in try/finally to dispose parameter native memory
            // exactly once. ignoreDispose: true above prevents 'using' declarations, so the
            // finally is the sole owner; a bare try (no params) would be invalid C#.
            if (parameterSize > 0)
            {
                sb.AppendLine("try");
                sb.AppendLine("{");
                sb.IncrementIndent();
            }

            if (interfacePath != null)
            {
                // Function exported within an interface: resolve under the interface instance.
                sb.Append("var __callFn = _instance.GetFunction(\"").Append(interfacePath).Append("\", \"").Append(name).AppendLine("\");");
                sb.Append("using global::Wasmtime.ComponentCallResults result = _instance.Call(__callFn, ")
                    .Append(funcType.Results.Length)
                    .Append(", parameters, ")
                    .Append(parameterSize)
                    .AppendLine(");");
            }
            else
            {
                sb.Append("using global::Wasmtime.ComponentCallResults result = _instance.Call(\"")
                    .Append(name)
                    .Append("\", ")
                    .Append(funcType.Results.Length)
                    .Append(", parameters, ")
                    .Append(parameterSize)
                    .AppendLine(");");
            }

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

            // Close the try and dispose parameter native memory in the finally.
            if (parameterSize > 0)
            {
                sb.DecrementIndent();
                sb.AppendLine("}");
                sb.AppendLine("finally");
                sb.AppendLine("{");
                sb.IncrementIndent();
                sb.Append("for (int _i = 0; _i < ").Append(parameterSize).AppendLine("; _i++)");
                sb.IncrementIndent();
                sb.AppendLine("parameters[_i].Dispose(_store);");
                sb.DecrementIndent();
                sb.DecrementIndent();
                sb.AppendLine("}");
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
            // Detect resource ABI name patterns
            if (name.StartsWith("[constructor]"))
            {
                WriteConstructorInvoke(sb, className, funcType, name, resolver);
                return;
            }
            if (name.StartsWith("[method]"))
            {
                WriteMethodInvoke(sb, className, funcType, name, resolver);
                return;
            }
            if (name.StartsWith("[static]"))
            {
                WriteStaticInvoke(sb, className, funcType, name, resolver);
                return;
            }
            // Note: [resource-drop] is not an import — drop is handled by the resource
            // destructor registered via DefineResource (see WriteResourceImports).

            // Generic path for non-resource imports
            var importName = StringUtils.GetName(name);

            sb.Append("private unsafe static void Invoke").Append(importName);
            sb.AppendLine("(object? state, global::Wasmtime.ComponentCallResults args, global::Wasmtime.ComponentValue* results, global::Wasmtime.StoreContext context)");
            sb.AppendLine("{");

            sb.IncrementIndent();
            sb.Append("var @this = (").Append(className).Append("Imports").AppendLine(")state!;");
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

            // Return any pooled/rented arrays after the user method has consumed the spans
            for (var i = 0; i < funcType.Parameters.Length; i++)
            {
                funcType.Parameters[i].Type.HostWriter.WriteResultCleanup(sb, "args", i, resolver);
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

    /// <summary>
    /// Generates Invoke for [constructor]resource — calls factory, stores in table, returns handle.
    /// </summary>
    private static void WriteConstructorInvoke(IndentedStringBuilder sb,
        string className,
        WitFuncType funcType,
        string name,
        ITypeContainerResolver resolver)
    {
        var importName = StringUtils.GetName(name);
        var resName = name.Substring("[constructor]".Length);
        var resClassName = StringUtils.GetName(resName);
        var factoryName = "New" + resClassName;

        // Resolve the resource HostWriter for handle table info
        var rhw = ResolveResourceHostWriter(funcType.Results.Length > 0 ? funcType.Results[0] : null, resolver);

        sb.Append("private unsafe static void Invoke").Append(importName);
        sb.AppendLine("(object? state, global::Wasmtime.ComponentCallResults args, global::Wasmtime.ComponentValue* results, global::Wasmtime.StoreContext context)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.Append("var @this = (").Append(className).Append("Imports").AppendLine(")state!;");
        sb.AppendLine();

        // Extract constructor parameters
        if (funcType.Parameters.Length > 0)
        {
            for (var i = 0; i < funcType.Parameters.Length; i++)
            {
                var param = funcType.Parameters[i];
                param.Type.HostWriter.WriteResultGetterInitializer(sb, "args", i, resolver);
            }
        }

        // Call factory method
        sb.Append("var obj = @this.").Append(factoryName);
        if (funcType.Parameters.Length > 0)
        {
            sb.Append('(');
            for (var i = 0; i < funcType.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var param = funcType.Parameters[i];
                param.Type.HostWriter.WriteResultGetter(sb, "args", i, resolver);
            }
            sb.AppendLine(");");
        }
        else
        {
            sb.AppendLine("();");
        }

        // Return any pooled/rented arrays after the factory method has consumed the spans
        for (var i = 0; i < funcType.Parameters.Length; i++)
        {
            funcType.Parameters[i].Type.HostWriter.WriteResultCleanup(sb, "args", i, resolver);
        }

        // Store in handle table and return handle
        if (rhw != null)
        {
            sb.Append("var handle = @this.").Append(rhw.StoreMethodName!).AppendLine("(obj);");
            sb.Append("results[0] = global::Wasmtime.ComponentValue.CreateOwnResource(context, handle, ").Append(rhw.TypeId).AppendLine(");");
        }

        sb.DecrementIndent();
        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Generates Invoke for [method]resource.method — looks up self, calls method on object.
    /// </summary>
    private static void WriteMethodInvoke(IndentedStringBuilder sb,
        string className,
        WitFuncType funcType,
        string name,
        ITypeContainerResolver resolver)
    {
        var importName = StringUtils.GetName(name);

        // Parse: [method]system.add-commands → resource="system", method="add-commands"
        var afterPrefix = name.Substring("[method]".Length);
        var dotIndex = afterPrefix.IndexOf('.');
        var methodName = dotIndex >= 0 ? StringUtils.GetName(afterPrefix.Substring(dotIndex + 1)) : importName;

        // Self is the first parameter — resolve its resource type
        var selfType = funcType.Parameters[0].Type;
        var selfRhw = ResolveResourceHostWriter(selfType, resolver);

        sb.Append("private unsafe static void Invoke").Append(importName);
        sb.AppendLine("(object? state, global::Wasmtime.ComponentCallResults args, global::Wasmtime.ComponentValue* results, global::Wasmtime.StoreContext context)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.Append("var @this = (").Append(className).Append("Imports").AppendLine(")state!;");
        sb.AppendLine();

        // Look up self from handle table (debug build validates handle)
        if (selfRhw != null)
        {
            sb.AppendLine("#if DEBUG");
            sb.Append("var self = @this.Get").Append(selfRhw.ClassName!).AppendLine("(args[0].ToResourceRep(context));");
            sb.AppendLine("#else");
            sb.Append("var self = @this.").Append(selfRhw.HandleTableField!).AppendLine("[args[0].ToResourceRep(context)];");
            sb.AppendLine("#endif");
        }
        else
        {
            sb.AppendLine("var self = args[0].ToResourceRep(context);");
        }

        // Extract remaining parameters (indices 1+)
        for (var i = 1; i < funcType.Parameters.Length; i++)
        {
            var param = funcType.Parameters[i];
            param.Type.HostWriter.WriteResultGetterInitializer(sb, "args", i, resolver);
        }

        // Call method on self and handle return
        if (funcType.Results.Length > 0)
        {
            WriteParameters(sb, resolver, funcType.Results);
            sb.Append(" result = ");
        }

        sb.Append("self.").Append(methodName);

        if (funcType.Parameters.Length > 1)
        {
            sb.Append('(');
            sb.IncrementIndent();
            for (var i = 1; i < funcType.Parameters.Length; i++)
            {
                sb.AppendLine(i > 1 ? "," : "");
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

        // Return any pooled/rented arrays after the user method has consumed the spans
        for (var i = 1; i < funcType.Parameters.Length; i++)
        {
            funcType.Parameters[i].Type.HostWriter.WriteResultCleanup(sb, "args", i, resolver);
        }

        // Handle return values
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

    /// <summary>
    /// Generates Invoke for [static]resource.method — no self; calls the static method
    /// emitted on the imports class (named &lt;Resource&gt;&lt;Method&gt;).
    /// </summary>
    private static void WriteStaticInvoke(IndentedStringBuilder sb,
        string className,
        WitFuncType funcType,
        string name,
        ITypeContainerResolver resolver)
    {
        var importName = StringUtils.GetName(name);

        // Parse: [static]counter.merge -> resource="counter", method="merge"
        var afterPrefix = name.Substring("[static]".Length);
        var dotIndex = afterPrefix.IndexOf('.');
        var resClass = dotIndex >= 0 ? StringUtils.GetName(afterPrefix.Substring(0, dotIndex)) : "";
        var methodName = dotIndex >= 0 ? StringUtils.GetName(afterPrefix.Substring(dotIndex + 1)) : importName;
        var hostMethod = resClass + methodName;

        sb.Append("private unsafe static void Invoke").Append(importName);
        sb.AppendLine("(object? state, global::Wasmtime.ComponentCallResults args, global::Wasmtime.ComponentValue* results, global::Wasmtime.StoreContext context)");
        sb.AppendLine("{");
        sb.IncrementIndent();
        sb.Append("var @this = (").Append(className).Append("Imports").AppendLine(")state!;");
        sb.AppendLine();

        // Extract parameters (no self for static)
        for (var i = 0; i < funcType.Parameters.Length; i++)
        {
            var param = funcType.Parameters[i];
            param.Type.HostWriter.WriteResultGetterInitializer(sb, "args", i, resolver);
        }

        if (funcType.Results.Length > 0)
        {
            WriteParameters(sb, resolver, funcType.Results);
            sb.Append(" result = ");
        }

        sb.Append("@this.").Append(hostMethod);

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

    /// <summary>
    /// Resolves a WitType to its ResourceHostWriter if it is a resource type.
    /// </summary>
    private static ResourceHostWriter? ResolveResourceHostWriter(WitType? type, ITypeContainerResolver resolver)
    {
        if (type == null) return null;

        while (type is WitCustomType customType)
        {
            type = customType.Resolve(resolver);
        }

        if (type is WitResourceType resourceType && resourceType.HostWriter is ResourceHostWriter rhw)
        {
            return rhw;
        }

        return null;
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
            items[0].HostWriter.WriteReturnType(sb, resolver);
        }
        else
        {
            sb.Append('(');
            for (var i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                items[i].HostWriter.WriteReturnType(sb, resolver);
            }

            sb.Append(')');
        }
    }
}
