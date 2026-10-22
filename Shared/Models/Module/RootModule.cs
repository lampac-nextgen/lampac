using Newtonsoft.Json;
using System.Reflection;
using System.Runtime.Loader;

namespace Shared.Models.Module;

public class RootModule
{
    public bool enable { get; set; }

    public bool dynamic { get; set; }

    public string name { get; set; }

    public string path { get; set; }

    public string[] references { get; set; }

    public string[] syntaxPaths { get; set; }

    public string[] tree { get; set; }


    /// <summary>
    /// Set at load. Source modules: FNV cache id of Shared.dll plus the compiled sources.
    /// A dropped dll: MD5 of that file. Not read from manifest.json.
    /// </summary>
    [JsonIgnore]
    public string hash { get; set; }

    public Assembly assembly { get; set; }

    public AssemblyLoadContext assemblyLoadContext { get; set; }
}
