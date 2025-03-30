using Mono.Cecil;

namespace FlexPkg;

/// <summary>
/// From https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Il2CppInteropManager.AsmToCecilConverter.cs
/// </summary>
public class AsmToCecilConverter : IAssemblyResolver
{
    private readonly Dictionary<string, AsmResolver.DotNet.AssemblyDefinition> asmResolverDictionary;
    private readonly Dictionary<string, AssemblyDefinition> cecilDictionary = new();
    private readonly Dictionary<AsmResolver.DotNet.AssemblyDefinition, AssemblyDefinition> asmToCecil = new();

    public AsmToCecilConverter(List<AsmResolver.DotNet.AssemblyDefinition> list)
    {
        asmResolverDictionary = list.ToDictionary(a => a.Name!.ToString(), a => a);
    }

    public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters { AssemblyResolver = this });

    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        var assemblyName = name.Name;
        if (!cecilDictionary.TryGetValue(assemblyName, out var cecilAssembly) && asmResolverDictionary.TryGetValue(assemblyName, out var asmAssembly))
        {
            cecilAssembly = Convert(asmAssembly, parameters);
        }

        return cecilAssembly!;
    }

    public List<AssemblyDefinition> ConvertAll()
    {
        List<AssemblyDefinition> cecilAssemblies = new(asmResolverDictionary.Count);
        foreach (var asmResolverAssembly in asmResolverDictionary.Values)
        {
            var cecilAssembly = Convert(asmResolverAssembly);
            cecilAssemblies.Add(cecilAssembly);
        }

        return cecilAssemblies;
    }

    private AssemblyDefinition Convert(AsmResolver.DotNet.AssemblyDefinition asmResolverAssembly)
    {
        return Convert(asmResolverAssembly, new ReaderParameters() { AssemblyResolver = this });
    }

    private AssemblyDefinition Convert(AsmResolver.DotNet.AssemblyDefinition asmResolverAssembly, ReaderParameters readerParameters)
    {
        if (asmToCecil.TryGetValue(asmResolverAssembly, out var cecilAssembly))
        {
            return cecilAssembly;
        }

        MemoryStream stream = new();
        asmResolverAssembly.WriteManifest(stream);
        stream.Position = 0;
        cecilAssembly = AssemblyDefinition.ReadAssembly(stream, readerParameters);
        cecilDictionary.Add(cecilAssembly.Name.Name, cecilAssembly);
        asmToCecil.Add(asmResolverAssembly, cecilAssembly);
        return cecilAssembly;
    }

    public void Dispose()
    {
    }
}