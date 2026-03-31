using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.IO;

namespace AdminPanel
{
    public class ModInit : IModuleLoaded
    {
        public void Loaded(InitspaceModel conf)
        {
            string src = Path.Combine(conf.path, "wwwroot_control");
            string dst = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "control");

            if (Directory.Exists(src) && !Directory.Exists(dst))
                CopyDirectory(src, dst);
        }

        public void Dispose()
        {
        }

        static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);

            foreach (string file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);

            foreach (string dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }
    }
}
