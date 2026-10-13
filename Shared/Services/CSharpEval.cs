using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyModel;
using Shared.Models.Module;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Shared.Services;

public static class CSharpEval
{
    static ConcurrentDictionary<Fnv1aHash, dynamic> scripts = new();

    #region Execute<T>
    public static T Execute<T>(string cs, object model, ScriptOptions options = null)
    {
        return ExecuteAsync<T>(cs, model, options).GetAwaiter().GetResult();
    }

    public static Task<T> ExecuteAsync<T>(string cs, object model, ScriptOptions options = null)
    {
        var entry = scripts.GetOrAdd(Fnv1a.Hash(cs), _ =>
        {
            if (options == null)
                options = ScriptOptions.Default;

            options = options
                .AddReferences(typeof(Console).Assembly).AddImports("System")
                .AddReferences(typeof(HttpUtility).Assembly).AddImports("System.Web")
                .AddReferences(typeof(Enumerable).Assembly).AddImports("System.Linq")
                .AddReferences(typeof(List<>).Assembly).AddImports("System.Collections.Generic")
                .AddReferences(typeof(Regex).Assembly).AddImports("System.Text.RegularExpressions");

            return CSharpScript.Create<T>(
                cs,
                options,
                globalsType: model.GetType(),
                assemblyLoader: new InteractiveAssemblyLoader()
            ).CreateDelegate();
        });

        return entry(model);
    }
    #endregion

    #region BaseExecute<T>
    public static T BaseExecute<T>(string cs, object model, ScriptOptions options = null, InteractiveAssemblyLoader loader = null)
    {
        return BaseExecuteAsync<T>(cs, model, options, loader).GetAwaiter().GetResult();
    }

    public static Task<T> BaseExecuteAsync<T>(string cs, object model, ScriptOptions options = null, InteractiveAssemblyLoader loader = null)
    {
        var entry = scripts.GetOrAdd(Fnv1a.Hash(cs), _ =>
        {
            return CSharpScript.Create<T>(
                cs,
                options,
                globalsType: model.GetType(),
                assemblyLoader: loader
            ).CreateDelegate();
        });

        return entry(model);
    }
    #endregion

    #region Execute
    public static void Execute(string cs, object model, ScriptOptions options = null)
    {
        ExecuteAsync(cs, model, options).GetAwaiter().GetResult();
    }

    public static Task ExecuteAsync(string cs, object model, ScriptOptions options = null)
    {
        var entry = scripts.GetOrAdd(Fnv1a.Hash(cs), _ =>
        {
            if (options == null)
                options = ScriptOptions.Default;

            options = options
                .AddReferences(typeof(Console).Assembly).AddImports("System")
                .AddReferences(typeof(HttpUtility).Assembly).AddImports("System.Web")
                .AddReferences(typeof(Enumerable).Assembly).AddImports("System.Linq")
                .AddReferences(typeof(List<>).Assembly).AddImports("System.Collections.Generic")
                .AddReferences(typeof(Regex).Assembly).AddImports("System.Text.RegularExpressions");

            return CSharpScript.Create(
                cs,
                options,
                globalsType: model.GetType(),
                assemblyLoader: new InteractiveAssemblyLoader()
            ).CreateDelegate();
        });

        return entry(model);
    }
    #endregion


    #region Compilation
    public static List<PortableExecutableReference> appReferences;
    static readonly string vshared = CrypTo.md5File("Shared.dll");
    static readonly object lockCompilationObj = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (Fnv1aHash sumhash, Assembly assembly, AssemblyLoadContext alc, string path) Compilation(RootModule mod)
    {
        lock (lockCompilationObj)
        {
            string path = mod.path;

            if (Directory.Exists(path))
            {
                var sumhash = Fnv1a.Empty;
                Fnv1a.Append(ref sumhash, vshared);

                #region syntaxTree
                var syntaxTree = new List<SyntaxTree>();
                var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

                List<string> syntaxPaths = new();

                if (mod.tree != null && mod.tree.Length > 0)
                {
                    foreach (string sp in mod.tree)
                    {
                        string cspath = Path.GetFullPath(Path.Combine(path, sp));

                        if (sp.EndsWith(".cs"))
                            syntaxPaths.Add(cspath);
                        else
                        {
                            foreach (string csfile in Directory.GetFiles(cspath, "*.cs", SearchOption.AllDirectories))
                                syntaxPaths.Add(csfile);
                        }
                    }
                }
                else
                {
                    foreach (string csfile in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
                    {
                        string _file = csfile.Replace("\\", "/").Replace(path.Replace("\\", "/"), "").Replace(AppContext.BaseDirectory.Replace("\\", "/"), "");
                        if (!Regex.IsMatch(_file, "(\\.vs|bin|obj|Properties)/", RegexOptions.IgnoreCase))
                            syntaxPaths.Add(csfile);
                    }
                }

                if (mod.syntaxPaths != null)
                {
                    foreach (string sp in mod.syntaxPaths)
                    {
                        string cspath = Path.GetFullPath(Path.Combine(path, sp));

                        if (sp.EndsWith(".cs"))
                            syntaxPaths.Add(cspath);
                        else
                        {
                            foreach (string csfile in Directory.GetFiles(cspath, "*.cs", SearchOption.AllDirectories))
                                syntaxPaths.Add(csfile);
                        }
                    }
                }

                foreach (string csfile in syntaxPaths)
                {
                    using (var fileStream = new FileStream(csfile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var sourceText = SourceText.From(fileStream, Encoding.UTF8);
                        syntaxTree.Add(CSharpSyntaxTree.ParseText(sourceText, parseOptions, csfile));

                        var checksum = sourceText.GetChecksum();
                        Fnv1a.Append(ref sumhash, checksum);
                    }
                }
                #endregion

                #region references
                if (mod.references != null)
                {
                    foreach (string refns in mod.references)
                    {
                        string dlrns = Path.Combine(AppContext.BaseDirectory, mod.path, refns);

                        if (refns.EndsWith("/"))
                        {
                            foreach (string dlPath in Directory.GetFiles(dlrns, "*.dll"))
                            {
                                if (appReferences.FirstOrDefault(a => a.FilePath == dlPath) == null)
                                {
                                    var assembly = Assembly.LoadFrom(dlPath);
                                    appReferences.Add(MetadataReference.CreateFromFile(assembly.Location));
                                }
                            }
                        }
                        else if (File.Exists(dlrns))
                        {
                            if (appReferences.FirstOrDefault(a => a.FilePath == dlrns) == null)
                            {
                                var assembly = Assembly.LoadFrom(dlrns);
                                appReferences.Add(MetadataReference.CreateFromFile(assembly.Location));
                            }
                        }
                        else
                        {
                            var dependencyContext = DependencyContext.Default;

                            foreach (var library in dependencyContext.RuntimeLibraries.SelectMany(library => library.GetDefaultAssemblyNames(dependencyContext)))
                            {
                                if (library.Name.Equals(refns, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!appReferences.Any(r => string.Equals(Path.GetFileNameWithoutExtension(r.FilePath), library.Name, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        var assembly = Assembly.Load(library);
                                        appReferences.Add(MetadataReference.CreateFromFile(assembly.Location));
                                    }
                                }
                            }
                        }
                    }
                }
                #endregion

                #region cache dll
                string cachePath = Path.Combine("cache", "module", Fnv1a.Base64Url(sumhash));

                if (File.Exists(cachePath))
                {
                    using (var fs = File.OpenRead(cachePath))
                    {
                        var alc = new AssemblyLoadContext(mod.name, isCollectible: true);
                        var assembly = alc.LoadFromStream(fs);
                        return (sumhash, assembly, alc, path);
                    }
                }
                #endregion

                // Same product version as Shared.dll. The module cache key already includes that file hash.
                syntaxTree.Add(ModuleVersionSyntax());

                var compilation = CSharpCompilation.Create(mod.name, syntaxTree, references: appReferences, options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                using (var ms = PoolInvk.msm.GetStream())
                {
                    var result = compilation.Emit(ms);

                    if (result.Success)
                    {
                        ms.Seek(0, SeekOrigin.Begin);

                        using (var file = File.Create(cachePath))
                            ms.CopyTo(file);

                        ms.Seek(0, SeekOrigin.Begin);

                        var alc = new AssemblyLoadContext(mod.name, isCollectible: true);
                        var assembly = alc.LoadFromStream(ms);

                        return (sumhash, assembly, alc, path);
                    }
                    else
                    {
                        Console.WriteLine($"\ncompilation error: {mod.name}");
                        foreach (var diagnostic in result.Diagnostics)
                        {
                            if (diagnostic.Severity == DiagnosticSeverity.Error)
                                Console.WriteLine(diagnostic);
                        }
                        Console.WriteLine("\n");
                    }
                }
            }

            return default;
        }
    }

    /// <summary>
    /// Assembly attributes copied from Shared so a module compiled at startup reports the same version.
    /// </summary>
    static SyntaxTree ModuleVersionSyntax()
    {
        var asm = typeof(CSharpEval).Assembly;
        string informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        string fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "0.0.0.0";
        string assemblyVersion = asm.GetName().Version?.ToString() ?? "0.0.0.0";

        string src =
            "[assembly: System.Reflection.AssemblyVersionAttribute(\"" + Escape(assemblyVersion) + "\")]\n" +
            "[assembly: System.Reflection.AssemblyFileVersionAttribute(\"" + Escape(fileVersion) + "\")]\n" +
            "[assembly: System.Reflection.AssemblyInformationalVersionAttribute(\"" + Escape(informational) + "\")]\n";

        return CSharpSyntaxTree.ParseText(src, new CSharpParseOptions(LanguageVersion.Latest));
    }

    static string Escape(string value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");
    #endregion
}
