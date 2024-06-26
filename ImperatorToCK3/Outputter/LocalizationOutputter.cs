﻿using commonItems;
using commonItems.Localization;
using commonItems.Mods;
using ImperatorToCK3.CK3;
using ImperatorToCK3.CommonUtils;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ImperatorToCK3.Outputter;
public static class LocalizationOutputter {
	public static async Task OutputLocalization(string outputModPath, World ck3World) {
		Logger.Info("Writing Localization...");
		var baseLocDir = Path.Join(outputModPath, "localization");
		var baseReplaceLocDir = Path.Join(baseLocDir, "replace");

		var sb = new StringBuilder();
		foreach (var language in ConverterGlobals.SupportedLanguages) {
			sb.AppendLine($"l_{language}:");

			// title localization
			foreach (var title in ck3World.LandedTitles) {
				foreach (var locBlock in title.Localizations) {
					sb.AppendLine(locBlock.GetYmlLocLineForLanguage(language));
				}
			}

			// character name localization
			var uniqueKeys = new HashSet<string>();
			foreach (var character in ck3World.Characters) {
				foreach (var (key, locBlock) in character.Localizations) {
					if (uniqueKeys.Contains(key)) {
						continue;
					}

					sb.AppendLine(locBlock.GetYmlLocLineForLanguage(language));
					uniqueKeys.Add(key);
				}
			}
			
			var locFilePath = Path.Join(baseReplaceLocDir, language, $"converter_l_{language}.yml");
			await using var locWriter = FileOpeningHelper.OpenWriteWithRetries(locFilePath, encoding: System.Text.Encoding.UTF8);
			await locWriter.WriteLineAsync(sb.ToString());
			sb.Clear();
		}

		// dynasty localization
		foreach (var language in ConverterGlobals.SupportedLanguages) {
			sb.AppendLine($"l_{language}:");

			foreach (var dynasty in ck3World.Dynasties) {
				var localizedName = dynasty.LocalizedName;
				if (localizedName is not null) {
					sb.AppendLine(localizedName.GetYmlLocLineForLanguage(language));
				} else if (dynasty.FromImperator) {
					Logger.Warn($"Dynasty {dynasty.Id} has no localizations!");
					sb.AppendLine($" {dynasty.Name}: \"{dynasty.Name}\"");
				}
			}
			
			var dynastyLocFilePath = Path.Combine(baseLocDir, $"{language}/irtock3_dynasty_l_{language}.yml");
			await using var dynastyLocWriter = FileOpeningHelper.OpenWriteWithRetries(dynastyLocFilePath, System.Text.Encoding.UTF8);
			await dynastyLocWriter.WriteAsync(sb.ToString());
			sb.Clear();
		}
		
		await OutputFallbackLocForMissingSecondaryLanguageLoc(baseLocDir, ck3World.ModFS);
		
		Logger.IncrementProgress();
	}

	private static async Task OutputFallbackLocForMissingSecondaryLanguageLoc(string baseLocDir, ModFilesystem ck3ModFS) {
		var primaryLanguage = ConverterGlobals.PrimaryLanguage;
		var secondaryLanguages = ConverterGlobals.SecondaryLanguages;
		
		// Read loc from CK3 and selected CK3 mods.
		var ck3LocDB = new LocDB(primaryLanguage, secondaryLanguages);
		ck3LocDB.ScrapeLocalizations(ck3ModFS);

		// Also read already outputted loc from the output directory.
		var locFilesInOutputDir = Directory.GetFiles(baseLocDir, "*.yml", SearchOption.AllDirectories);
		foreach (var outputtedLocFilePath in locFilesInOutputDir) {
			ck3LocDB.ScrapeFile(outputtedLocFilePath);
		}

		var languageToLocLinesDict = new Dictionary<string, List<string>>();
		foreach (var language in secondaryLanguages) {
			languageToLocLinesDict[language] = [];
		}
		
		foreach (var locBlock in ck3LocDB) {
			if (!locBlock.HasLocForLanguage(primaryLanguage)) {
				continue;
			}

			foreach (var secondaryLanguage in secondaryLanguages) {
				if (locBlock.HasLocForLanguage(secondaryLanguage)) {
					continue;
				}
				
				languageToLocLinesDict[secondaryLanguage].Add(locBlock.GetYmlLocLineForLanguage(primaryLanguage));
			}
		}
		
		var sb = new StringBuilder();
		foreach (var language in secondaryLanguages) {
			var linesToOutput = languageToLocLinesDict[language];
			if (linesToOutput.Count == 0) {
				continue;
			}
			
			Logger.Debug($"Outputting {linesToOutput.Count} fallback loc lines for {language}...");

			sb.AppendLine($"l_{language}:");
			foreach (var line in linesToOutput) {
				sb.AppendLine(line);
			}
			
			var locFilePath = Path.Combine(baseLocDir, $"{language}/irtock3_fallback_loc_l_{language}.yml");
			await using var locWriter = FileOpeningHelper.OpenWriteWithRetries(locFilePath, System.Text.Encoding.UTF8);
			await locWriter.WriteAsync(sb.ToString());
			sb.Clear();
		}
	}
}
