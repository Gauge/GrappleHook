using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GrappleHook.Tests
{
    /// <summary>
    /// Checks the SBC definitions against each other, against the files on disk and against
    /// the block subtypes the scripts attach game logic to. None of this needs the game, and
    /// all of it fails at world load if it is wrong.
    /// </summary>
    public class DefinitionTests
    {
        /// <summary>The mod project folder: the scripts, with the packed content under Content/.</summary>
        static readonly string ProjectRoot = FindProjectRoot();

        /// <summary>What MDK2 packs to the root of the deployed mod, so SBC paths resolve against it.</summary>
        static readonly string ModRoot = Path.Combine(ProjectRoot, "Content");

        static string FindProjectRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string project = Path.Combine(dir.FullName, "GrappleHook");
                if (File.Exists(Path.Combine(project, "Content", "Data", "CubeBlock.sbc")))
                {
                    return project;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("could not find the mod project from " + AppContext.BaseDirectory);
        }

        /// <summary>Every source the game compiles: the project folder minus build output and content.</summary>
        static IEnumerable<string> ScriptFiles()
        {
            string[] skipped = { "bin", "obj", "Content" };
            return Directory.GetFiles(ProjectRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !skipped.Contains(Path.GetRelativePath(ProjectRoot, f).Split(Path.DirectorySeparatorChar)[0]));
        }

        static XDocument Load(string name)
        {
            return XDocument.Load(Path.Combine(ModRoot, "Data", name));
        }

        static IEnumerable<XElement> BlockDefinitions()
        {
            return Load("CubeBlock.sbc").Descendants("Definition");
        }

        static string SubtypeOf(XElement definition)
        {
            return definition.Element("Id")?.Element("SubtypeId")?.Value;
        }

        static string ModPath(string sbcPath)
        {
            return Path.Combine(ModRoot, sbcPath.Replace('\\', Path.DirectorySeparatorChar));
        }

        [Fact]
        public void EverySbcIsWellFormedXml()
        {
            foreach (string file in Directory.GetFiles(Path.Combine(ModRoot, "Data"), "*.sbc"))
            {
                XDocument.Load(file);
            }
        }

        [Fact]
        public void BlockSubtypesAreUnique()
        {
            List<string> subtypes = BlockDefinitions().Select(SubtypeOf).ToList();

            Assert.Equal(subtypes.Count, subtypes.Distinct().Count());
        }

        [Fact]
        public void TheModShipsBothTurretsAndBothFixedVariants()
        {
            List<string> subtypes = BlockDefinitions().Select(SubtypeOf).ToList();

            Assert.Contains("GrappleHookTurretLarge", subtypes);
            Assert.Contains("GrappleHookTurretSmall", subtypes);
            Assert.Contains("GrappleHookFixedLarge", subtypes);
            Assert.Contains("GrappleHookFixedSmall", subtypes);
        }

        [Fact]
        public void EveryBlockThatTheScriptsAttachToExists()
        {
            // [MyEntityComponentDescriptor(typeof(MyObjectBuilder_X), true, "SubtypeA", "SubtypeB")]
            Regex descriptor = new Regex(@"MyEntityComponentDescriptor\s*\(\s*typeof\(\s*MyObjectBuilder_(\w+)\s*\)\s*,\s*\w+\s*,\s*([^)]*)\)");
            List<string> defined = BlockDefinitions().Select(SubtypeOf).ToList();

            int checkedSubtypes = 0;
            foreach (string file in ScriptFiles())
            {
                foreach (Match match in descriptor.Matches(File.ReadAllText(file)))
                {
                    string typeId = match.Groups[1].Value;

                    foreach (Match subtype in Regex.Matches(match.Groups[2].Value, "\"([^\"]+)\""))
                    {
                        string id = subtype.Groups[1].Value;
                        checkedSubtypes++;

                        XElement definition = BlockDefinitions().FirstOrDefault(d => SubtypeOf(d) == id);
                        Assert.True(definition != null, $"{Path.GetFileName(file)} attaches logic to block subtype {id}, which no definition declares");
                        Assert.Equal(typeId, definition.Element("Id").Element("TypeId").Value);
                    }
                }
            }

            Assert.Equal(4, checkedSubtypes);
        }

        [Fact]
        public void EveryBlockPointsAtAWeaponThatExists()
        {
            List<string> weapons = Load("Weapons.sbc").Descendants("Weapon")
                .Select(w => w.Element("Id").Element("SubtypeId").Value).ToList();

            foreach (XElement definition in BlockDefinitions())
            {
                string weapon = definition.Element("WeaponDefinitionId")?.Attribute("Subtype")?.Value;

                Assert.True(weapon != null, $"{SubtypeOf(definition)} has no weapon");
                Assert.Contains(weapon, weapons);
            }
        }

        [Fact]
        public void EveryWeaponPointsAtMagazinesAndAmmoThatExist()
        {
            List<string> magazines = Load("AmmoMagazines.sbc").Descendants("AmmoMagazine")
                .Select(m => m.Element("Id").Element("SubtypeId").Value).ToList();
            List<string> ammos = Load("Ammos.sbc").Descendants("Ammo")
                .Select(a => a.Element("Id").Element("SubtypeId").Value).ToList();

            foreach (XElement weapon in Load("Weapons.sbc").Descendants("Weapon"))
            {
                foreach (XElement magazine in weapon.Descendants("AmmoMagazine"))
                {
                    Assert.Contains(magazine.Attribute("Subtype").Value, magazines);
                }
            }

            foreach (XElement magazine in Load("AmmoMagazines.sbc").Descendants("AmmoMagazine"))
            {
                Assert.Contains(magazine.Element("AmmoDefinitionId").Attribute("Subtype").Value, ammos);
            }
        }

        [Fact]
        public void EveryBlueprintProducesSomethingThatExists()
        {
            List<string> magazines = Load("AmmoMagazines.sbc").Descendants("AmmoMagazine")
                .Select(m => m.Element("Id").Element("SubtypeId").Value).ToList();
            List<string> blueprints = Load("Blueprints.sbc").Descendants("Blueprint")
                .Select(b => b.Element("Id").Element("SubtypeId").Value).ToList();

            foreach (XElement blueprint in Load("Blueprints.sbc").Descendants("Blueprint"))
            {
                XElement result = blueprint.Element("Result");

                Assert.Equal("AmmoMagazine", result.Attribute("TypeId").Value);
                Assert.Contains(result.Attribute("SubtypeId").Value, magazines);
            }

            foreach (XElement entry in Load("BlueprintClasses.sbc").Descendants("Entry"))
            {
                Assert.Contains(entry.Attribute("BlueprintSubtypeId").Value, blueprints);
            }
        }

        [Fact]
        public void EveryModelAndIconReferencedIsInTheMod()
        {
            List<string> missing = new List<string>();

            foreach (XElement definition in BlockDefinitions())
            {
                foreach (string path in new[] { definition.Element("Model")?.Value, definition.Element("Icon")?.Value })
                {
                    if (path == null || path.StartsWith("Textures\\GUI\\Screens")) continue;
                    if (!File.Exists(ModPath(path))) missing.Add($"{SubtypeOf(definition)}: {path}");
                }

                foreach (XElement stage in definition.Descendants("BuildProgressModels").Elements("Model"))
                {
                    string path = stage.Attribute("File").Value;
                    if (!File.Exists(ModPath(path))) missing.Add($"{SubtypeOf(definition)}: {path}");
                }
            }

            foreach (XElement magazine in Load("AmmoMagazines.sbc").Descendants("AmmoMagazine"))
            {
                foreach (string path in new[] { magazine.Element("Model")?.Value, magazine.Element("Icon")?.Value })
                {
                    if (path != null && !File.Exists(ModPath(path))) missing.Add($"magazine: {path}");
                }
            }

            Assert.Empty(missing);
        }

        [Fact]
        public void TheFixedVariantsSharePairNameAndCoverBothGridSizes()
        {
            List<XElement> fixedGuns = BlockDefinitions()
                .Where(d => d.Element("BlockPairName")?.Value == "GrappleHookFixed").ToList();

            Assert.Equal(2, fixedGuns.Count);
            Assert.Equal(new[] { "Large", "Small" }, fixedGuns.Select(d => d.Element("CubeSize").Value).OrderBy(v => v).ToArray());

            foreach (XElement definition in fixedGuns)
            {
                Assert.Equal("SmallMissileLauncher", definition.Element("Id").Element("TypeId").Value);
            }
        }

        [Fact]
        public void TheFixedLaunchersAreSelectableAsShipWeapons()
        {
            // a cockpit lists a weapon group only for guns a ship category names
            XElement category = Load("BlockCategories.sbc").Descendants("Category")
                .First(c => c.Element("Name")?.Value == "ShipWeapons");

            Assert.Equal("true", category.Element("IsShipCategory").Value);
            List<string> items = category.Element("ItemIds").Elements("string").Select(e => e.Value).ToList();
            Assert.Contains("SmallMissileLauncher/GrappleHookFixedLarge", items);
            Assert.Contains("SmallMissileLauncher/GrappleHookFixedSmall", items);
        }

        [Fact]
        public void EveryActionIconTheScriptsNameIsInTheMod()
        {
            // TerminalControls points each action at Textures\GUI\Icons\Actions\<name>.dds
            string source = string.Join("\n", ScriptFiles().Select(File.ReadAllText));
            List<string> icons = Regex.Matches(source, @"\.Icon = icons \+ ""([^""]+)""").Cast<Match>().Select(m => m.Groups[1].Value).ToList();

            Assert.Equal(4, icons.Count);
            foreach (string icon in icons)
            {
                Assert.True(File.Exists(ModPath("Textures\\GUI\\Icons\\Actions\\" + icon)), icon + " is not in the mod");
            }
        }

        [Fact]
        public void TheTurretsSharePairNameAndCoverBothGridSizes()
        {
            List<XElement> turrets = BlockDefinitions()
                .Where(d => d.Element("BlockPairName")?.Value == "GrappleHook").ToList();

            Assert.Equal(2, turrets.Count);
            Assert.Equal(new[] { "Large", "Small" }, turrets.Select(d => d.Element("CubeSize").Value).OrderBy(v => v).ToArray());

            foreach (XElement definition in turrets)
            {
                Assert.Equal("LargeMissileTurret", definition.Element("Id").Element("TypeId").Value);
            }
        }

        [Fact]
        public void TheFixedVariantsAreLaunchTubesWithTheirOwnModels()
        {
            // they used to borrow the turret's mesh, which drew a turret that could not turn
            foreach (string subtype in new[] { "GrappleHookFixedLarge", "GrappleHookFixedSmall" })
            {
                XElement definition = BlockDefinitions().First(d => SubtypeOf(d) == subtype);
                string model = definition.Element("Model").Value;

                Assert.Contains("GrappleFixed", model);
                Assert.All(definition.Element("BuildProgressModels").Elements("Model"),
                    m => Assert.Contains("GrappleFixed", m.Attribute("File").Value));
                // long along Z like the vanilla fixed launchers: one cell across, several deep
                XElement size = definition.Element("Size");
                Assert.Equal("1", size.Attribute("x").Value);
                Assert.Equal("1", size.Attribute("y").Value);
                Assert.True(int.Parse(size.Attribute("z").Value) > 1, subtype + " should be longer than it is wide");
            }
        }
    }
}
