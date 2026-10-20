// Сборка папки языка: официальный перевод (tar) как база + правки 2ClauZ поверх, по ключам.
// Репо хранит только наши правки; без базы игра показывает английский для всего остального.
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TranslationUpdater
{
    public static class LangMerge
    {
        public static string FindOfficialTar(string languagesDir)
        {
            if (!Directory.Exists(languagesDir)) return null;
            return Directory.GetFiles(languagesDir, "Russian*.tar").FirstOrDefault();
        }

        // Keyed — единое пространство ключей; DefInjected — своё на каждый тип дефа; остальное — пофайлово.
        private static string Category(string rel)
        {
            string[] parts = rel.Split('/');
            if (parts.Length >= 2 && parts[0].Equals("Keyed", StringComparison.OrdinalIgnoreCase)) return "Keyed";
            if (parts.Length >= 3 && parts[0].Equals("DefInjected", StringComparison.OrdinalIgnoreCase)) return "DefInjected/" + parts[1];
            return null;
        }

        private static string Rel(string root, string file)
        {
            return file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
        }

        public static string Build(string officialTar, string repoDir, string dstDir)
        {
            string tmp = dstDir.TrimEnd('\\', '/') + ".building";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            if (officialTar != null && File.Exists(officialTar))
                TarFile.ExtractToDirectory(officialTar, tmp, true);

            if (!Directory.Exists(repoDir)) Directory.CreateDirectory(repoDir = tmp + ".emptyrepo");
            var repoKeys = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var repoDocs = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(repoDir, "*", SearchOption.AllDirectories))
            {
                string rel = Rel(repoDir, f);
                string cat = Category(rel);
                if (cat == null || !f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    XDocument d = XDocument.Load(f, LoadOptions.PreserveWhitespace);
                    repoDocs[rel] = d;
                    HashSet<string> set;
                    if (!repoKeys.TryGetValue(cat, out set)) repoKeys[cat] = set = new HashSet<string>();
                    foreach (XElement e in d.Root.Elements()) set.Add(e.Name.LocalName);
                }
                catch { } // битый файл репо — ниже просто ляжет поверх как есть
            }

            int overridden = 0, mergedFiles = 0, copied = 0;
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(tmp, "*.xml", SearchOption.AllDirectories))
            {
                string rel = Rel(tmp, f);
                string cat = Category(rel);
                HashSet<string> set;
                if (cat == null || !repoKeys.TryGetValue(cat, out set)) continue;
                XDocument d;
                try { d = XDocument.Load(f, LoadOptions.PreserveWhitespace); }
                catch { continue; }
                List<XElement> dup = d.Root.Elements().Where(e => set.Contains(e.Name.LocalName)).ToList();
                foreach (XElement e in dup) e.Remove();
                overridden += dup.Count;
                XDocument ours;
                if (repoDocs.TryGetValue(rel, out ours))
                {
                    foreach (XElement e in ours.Root.Elements()) d.Root.Add(new XElement(e), "\n");
                    handled.Add(rel);
                    mergedFiles++;
                }
                if (dup.Count > 0 || handled.Contains(rel)) d.Save(f);
            }

            foreach (string f in Directory.GetFiles(repoDir, "*", SearchOption.AllDirectories))
            {
                string rel = Rel(repoDir, f);
                if (handled.Contains(rel)) continue;
                string target = Path.Combine(tmp, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(f, target, true);
                copied++;
            }

            if (repoDir == tmp + ".emptyrepo") Directory.Delete(repoDir, true);
            if (Directory.Exists(dstDir)) Directory.Delete(dstDir, true);
            Directory.Move(tmp, dstDir);
            return string.Format("base={0}, overridden keys={1}, merged files={2}, repo files copied={3}",
                officialTar == null ? "none" : Path.GetFileName(officialTar), overridden, mergedFiles, copied);
        }
    }
}
