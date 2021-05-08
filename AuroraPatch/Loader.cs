﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace AuroraPatch
{
    internal class Loader
    {
        internal readonly string AuroraExecutable;
        internal readonly string AuroraChecksum;
        internal readonly List<Patch> LoadedPatches = new List<Patch>();
        internal Assembly AuroraAssembly { get; set; } = null;
        internal Form TacticalMap { get; set; } = null;

        internal Loader(string exe, string checksum)
        {
            AuroraExecutable = exe;
            AuroraChecksum = checksum;
        }

        internal List<Patch> FindPatches()
        {
            var patches = new List<Patch>();
            string patchesDirectory = Path.Combine(Path.GetDirectoryName(AuroraExecutable), "Patches");
            Directory.CreateDirectory(patchesDirectory);

            Program.Logger.LogInfo($"Loading patches from {patchesDirectory}");

            var assemblies = new List<Assembly>();
            foreach (var dir in Directory.EnumerateDirectories(patchesDirectory))
            {
                Program.Logger.LogInfo($"Looking for assemblies in {dir}");
                foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(dll);
                        assemblies.Add(assembly);

                        Program.Logger.LogInfo($"Found asembly {Path.GetFileName(dll)}");
                    }
                    catch (Exception)
                    {
                        Program.Logger.LogDebug($"File {dll} can not be loaded as Assembly.");
                    }
                }
            }

            foreach (var assembly in assemblies)
            {
                Program.Logger.LogInfo($"Trying to retrieve types from assembly {Path.GetFileName(assembly.Location)}");

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (typeof(Patch).IsAssignableFrom(type))
                        {
                            Program.Logger.LogInfo($"Found patch {type.Name}");
                            var patch = (Patch)Activator.CreateInstance(type);
                            patch.Loader = this;
                            patches.Add(patch);
                        }
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    Program.Logger.LogError(e.LoaderExceptions.First().Message + " Are you missing a dependency?", false);
                }
            }

            return patches;
        }

        internal void SortPatches(List<Patch> patches)
        {
            // TODO check for circular dependencies

            patches.Sort((a, b) =>
            {
                if (a.Dependencies.Contains(b.Name))
                {
                    return 1;
                }
                else if (b.Dependencies.Contains(a.Name))
                {
                    return -1;
                }
                else
                {
                    return a.Name.CompareTo(b.Name);
                }
            });
        }

        internal IEnumerable<KeyValuePair<Patch, string>> GetUnmetDependencies(List<Patch> patches)
        {
            var available = new HashSet<string>();
            patches.ForEach(p => available.Add(p.Name));

            foreach (var patch in patches)
            {
                foreach (var dep in patch.Dependencies)
                {
                    if (!available.Contains(dep))
                    {
                        yield return new KeyValuePair<Patch, string>(patch, dep);
                    }
                }
            }
        }

        internal void StartAurora(List<Patch> patches)
        {
            TacticalMap = null;
            LoadedPatches.Clear();

            Program.Logger.LogInfo("Loading Aurora " + AuroraExecutable + " with checksum " + AuroraChecksum);
            AuroraAssembly = Assembly.LoadFile(AuroraExecutable);

            Program.Logger.LogInfo("Loading patches");
            foreach (var patch in patches)
            {
                Program.Logger.LogInfo("Loading patch " + patch.Name);

                try
                {
                    patch.LoadInternal();
                    LoadedPatches.Add(patch);
                }
                catch (Exception e)
                {
                    Program.Logger.LogError($"Patch Load exception {e.Message}");
                }
            }
            Program.Logger.LogInfo("Done loading patches");

            Program.Logger.LogInfo("Starting Aurora");
            TacticalMap = GetTacticalMap(AuroraAssembly);
            TacticalMap.Shown += MapShown;
            TacticalMap.Show();
        }

        /// <summary>
        /// Method called when the TacticalMap Form is shown.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MapShown(object sender, EventArgs e)
        {
            Program.Logger.LogInfo("Starting patches");
            foreach (var patch in LoadedPatches)
            {
                Program.Logger.LogInfo($"Starting patch {patch.Name}");

                try
                {
                    patch.StartInternal();
                }
                catch (Exception ex)
                {
                    Program.Logger.LogError($"Patch Start exception {ex.Message}");
                }
            }
            Program.Logger.LogInfo("Done starting patches");
        }

        /// <summary>
        /// Given the Aurora.exe assembly, find and return the TacticalMap Form object.
        /// This can be a bit tricky due to the obfuscation. We're going in blind and counting buttons/checkboxes.
        /// As of May 3rd 2021 (Aurora 1.13), the TacticalMap had 66 buttons and 68 checkboxes so that's our signature.
        /// We got a bit of wiggle room as we're looking for a Form object with anywhere between 60 and 80 of each.
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns>TacticalMap Form object</returns>
        private Form GetTacticalMap(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.BaseType.Equals(typeof(Form)))
                {
                    var buttons = 0;
                    var checkboxes = 0;

                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (field.FieldType.Equals(typeof(Button)))
                        {
                            buttons++;
                        }
                        else if (field.FieldType.Equals(typeof(CheckBox)))
                        {
                            checkboxes++;
                        }
                    }

                    if (buttons >= 60 && buttons <= 80 && checkboxes >= 60 && checkboxes <= 80)
                    {
                        Program.Logger.LogInfo("TacticalMap found: " + type.Name);
                        var map = (Form)Activator.CreateInstance(type);

                        return map;
                    }
                }
            }

            Program.Logger.LogCritical("TacticalMap not found");
            throw new Exception("TacticalMap not found");
        }
    }
}
