using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;

namespace BookSmart
{
    public class Program
    {
        // Settings
        static Lazy<Settings> LazySettings = new Lazy<Settings>();
        static Settings settings => LazySettings.Value;
        
        // Initial setup
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(
                    nickname: "Settings",
                    path: "settings.json",
                    out LazySettings
                )
                .SetTypicalOpen(GameRelease.SkyrimSE, "WeightlessThings.esp")
                .Run(args);
        }

        // Let's get to work!
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            // If quest labels are enabled, create the Quest Book cache first
            List<String> questBookCache = new();
            if (settings.addQuestLabels) { questBookCache = CreateQuestBookCache(state); }
            
            // Iterate all winning books from the load order
            foreach (var book in state.LoadOrder.PriorityOrder.OnlyEnabled().Book().WinningOverrides())
            {
                // If the book has no name, skip it
                if (book.Name == null) { continue; }

                // Store our new tags
                List<String> newTags = new();

                // Add Skill labels
                if (settings.addSkillLabels && book.Teaches is IBookSkillGetter skillTeach)
                {
                    var skillLabel = GetSkillLabelName(book);
                    if (skillLabel is not null) { newTags.Add(skillLabel); }
                }

                //Add Map Marker labels
                if (settings.addMapMarkerLabels && (book.VirtualMachineAdapter is not null && book.VirtualMachineAdapter.Scripts.Count > 0)                    )
                {
                    var mapMarkerLabel = GetMapLabelName(book);
                    if (mapMarkerLabel is not null) { newTags.Add(mapMarkerLabel); }
                }

                // Add Quest labels
                if (settings.addQuestLabels)
                {
                    var questLabel = GetQuestLabelName(book, questBookCache);
                    if (questLabel is not null) { newTags.Add(questLabel); }
                }

                // If we don't have any new tags, no need for an override record
                if (newTags.Count == 0) { continue; }

                // Actually create the override record
                var bookOverride = state.PatchMod.Books.GetOrAddAsOverride(book);
                
                // Special handling for a labelFormat of Star
                if (settings.labelFormat == Settings.LabelFormat.Star)
                {
                    switch (settings.labelPosition) {
                        case Settings.LabelPosition.Before_Name: { bookOverride.Name = $"*{book.Name.ToString()}"; break; }
                        case Settings.LabelPosition.After_Name: { bookOverride.Name = $"{book.Name.ToString()}*"; break; }
                        default: throw new NotImplementedException("Somehow your set Label Position to something that isn't supported.");
                    }
                }
                // All other labelFormats
                else
                {
                    bookOverride.Name = GetLabel(book.Name.ToString()!, String.Join("/", newTags));
                }

                // Console output
                Console.WriteLine($"{book.FormKey}: '{book.Name}' -> '{bookOverride.Name}'");
            };
        }

        
        public static string GetLabel(string existingName, string newLabel)
        {
            // set the open and close characters that go around the skill name
            string open = "";
            string close = "";
            switch (settings.encapsulatingCharacters)
            {
                case Settings.EncapsulatingCharacters.Chevrons: { open = "<"; close = ">"; break; }
                case Settings.EncapsulatingCharacters.Curly_Brackets: { open = "{"; close = "}"; break; }
                case Settings.EncapsulatingCharacters.Parenthesis: { open = "("; close = ")"; break; }
                case Settings.EncapsulatingCharacters.Square_Brackets: { open = "["; close = "]"; break; }
                case Settings.EncapsulatingCharacters.Stars: { open = "*"; close = "*"; break; }
                default: throw new NotImplementedException("Somehow you set Encapsulating Characters to something that isn't supported.");
            }

            return settings.labelPosition switch
            {
                Settings.LabelPosition.Before_Name => $"{open}{newLabel}{close} {existingName}",
                Settings.LabelPosition.After_Name => $"{existingName} {open}{newLabel}{close}",
                _ => throw new NotImplementedException("Somehow your set Label Position to something that isn't supported.")
            };
        }
        
        public static List<string> CreateQuestBookCache(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Console.WriteLine("--------------------------------------------------------------------");
            Console.WriteLine("Flipping through the quest library looking for books, please wait...");
            Console.WriteLine("--------------------------------------------------------------------");

            List<string> questBookCache = new List<String>();

            // Search all quests
            foreach (var quest in state.LoadOrder.PriorityOrder.OnlyEnabled().Quest().WinningOverrides())
            {
                if ((quest.Aliases is not null) && (quest.Aliases.Count > 0))
                {
                    // Examine each alias
                    foreach (var alias in quest.Aliases)
                    {
                        // CreateReferenceToObject alias
                        if (alias.CreateReferenceToObject is not null)
                        {
                            // try to resolve the quest object to the actual records
                            if (alias.CreateReferenceToObject.Object.TryResolve<IBookGetter>(state.LinkCache, out var questObject))
                            {
                                //Console.WriteLine($"{quest.FormKey}: '{questObject.FormKey}' is used in quest '{quest.Name}'");
                                questBookCache.Add(questObject.FormKey.ToString());
                            }
                        }

                        // Items alias
                        if (alias.Items is not null)
                        {
                            // try to resolve the quest object ot the actual records
                            foreach (var item in alias.Items)
                            {
                                // try to resolve the quest object to the actual records
                                // item.item.item.item
                                if (item.Item.Item.TryResolve<IBookGetter>(state.LinkCache, out var questObject))
                                {
                                    //Console.WriteLine($"{quest.FormKey}: '{questObject.FormKey}' is used in quest '{quest.Name}'");
                                    questBookCache.Add(questObject.FormKey.ToString());
                                }
                            }
                        }
                    }
                }
            }

            return questBookCache;
        }


        public static string? GetSkillLabelName(IBookGetter book)
        {
            if (book.Teaches is not IBookSkillGetter skillTeach) return null;
            if (skillTeach.Skill == null) return null;
            if ((int)skillTeach.Skill == -1) return null;

            // Label Format: Long
            if (settings.labelFormat == Settings.LabelFormat.Long)
            {
                return skillTeach.Skill switch
                {
                    Skill.HeavyArmor => "Heavy Armor",
                    Skill.LightArmor => "Light Armor",
                    Skill.OneHanded => "One-Handed",
                    Skill.TwoHanded => "Two-Handed",
                    _ => skillTeach.Skill.ToString()
                };
            }
            // Label Format: Short
            else if (settings.labelFormat == Settings.LabelFormat.Short)
            {
                return skillTeach.Skill switch
                {
                    Skill.Alchemy => "Alch",
                    Skill.Alteration => "Altr",
                    Skill.Archery => "Arch",
                    Skill.Block => "Blck",
                    Skill.Conjuration => "Conj",
                    Skill.Destruction => "Dest",
                    Skill.Enchanting => "Ench",
                    Skill.HeavyArmor => "H.Arm",
                    Skill.Illusion => "Illu",
                    Skill.LightArmor => "L.Arm",
                    Skill.Lockpicking => "Lock",
                    Skill.OneHanded => "1H",
                    Skill.Pickpocket => "Pick",
                    Skill.Restoration => "Resto",
                    Skill.Smithing => "Smith",
                    Skill.Sneak => "Snk",
                    Skill.Speech => "Spch",
                    Skill.TwoHanded => "2H",
                    _ => skillTeach.Skill.ToString()
                };
            }
            // Label Format: Star
            else if (settings.labelFormat == Settings.LabelFormat.Star)
            {
                return "*";
            }
            else
            {
                throw new NotImplementedException("Somehow you set labelFormat to something that isn't supported.");
            }
        }


        public static string? GetMapLabelName(IBookGetter book)
        {
            // variables for use in this section
            if (book == null) { return null; }
            if (book.VirtualMachineAdapter == null) { return null; }

            foreach (var script in book.VirtualMachineAdapter.Scripts)
            {
                // Any script with MapMarker in the script name will do
                if (script.Name.Contains("MapMarker", StringComparison.OrdinalIgnoreCase))
                {
                    return settings.labelFormat switch
                    {
                        Settings.LabelFormat.Long => "Map Marker",
                        Settings.LabelFormat.Short => "Map",
                        Settings.LabelFormat.Star => "*",
                        _ => throw new NotImplementedException("Somehow you set labelFormat to something that isn't supported.")
                    };
                }
            }

            return null;
        }


        public static string? GetQuestLabelName(IBookGetter book, List<string> questBookCache)
        {
            bool isBookQuestRealted = false;

            // Check the Quest Book Cache
            
            if (questBookCache.Contains(book.FormKey.ToString()))
            {
                isBookQuestRealted = true;
            }
            // Check for quest-related Book scripts
            else if ((book.VirtualMachineAdapter is not null) &&
                    (book.VirtualMachineAdapter.Scripts is not null) &&
                    (book.VirtualMachineAdapter.Scripts.Count > 0))
            {
                foreach (var script in book.VirtualMachineAdapter.Scripts)
                {
                    if (script.Name.Contains("Quest", StringComparison.OrdinalIgnoreCase) || settings.assumeBookScriptsAreQuests)
                    {
                        Console.WriteLine($"{book.FormKey}: '{book.Name}' has a quest script called '{script.Name}'.");
                        isBookQuestRealted = true;
                    }
                }
            }

            if (isBookQuestRealted)
            {
                return settings.labelFormat switch
                {
                    Settings.LabelFormat.Long => "Quest",
                    Settings.LabelFormat.Short => "Q",
                    Settings.LabelFormat.Star => "*",
                    _ => throw new NotImplementedException("Somehow you set labelFormat to something that isn't supported.")
                };
            } else
            {
                return null;
            }
            
        }


        
    }
}
